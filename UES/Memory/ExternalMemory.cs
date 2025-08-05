using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace UES.Memory
{
    /// <summary>
    /// External memory access implementation for reading/writing memory from an external process
    /// Uses Windows API calls for cross-process memory access
    /// AOT-compatible implementation
    /// </summary>
    public class ExternalMemory : IMemoryAccess
    {
        private readonly Process _process;
        private readonly nint _baseAddress;
        private readonly bool _isValid;

        /// <summary>
        /// Creates external memory access for the specified process name
        /// </summary>
        /// <param name="processName">Name of the target process</param>
        /// <exception cref="InvalidOperationException">Thrown when process cannot be found</exception>
        public ExternalMemory(string processName)
        {
            var processes = Process.GetProcessesByName(processName);
            _process = processes.FirstOrDefault() ?? throw new InvalidOperationException($"Failed to find process: {processName}");
            
            _baseAddress = _process.MainModule?.BaseAddress ?? 0;
            _isValid = _process != null && !_process.HasExited && _baseAddress != 0;
            
            if (_isValid)
            {
                Logger.LogDualColor("External memory access initialized for process", ConsoleColor.Green, 
                    $"{_process.ProcessName} (PID: {_process.Id})", ConsoleColor.Cyan);
                InitializeDefaults();
            }
            else
            {
                Logger.LogError($"Failed to initialize external memory access for process: {processName}");
            }
        }

        /// <summary>
        /// Creates external memory access for the specified process object
        /// </summary>
        /// <param name="process">Target process object</param>
        /// <exception cref="ArgumentNullException">Thrown when process is null</exception>
        public ExternalMemory(Process process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
            _baseAddress = _process.MainModule?.BaseAddress ?? 0;
            _isValid = !_process.HasExited && _baseAddress != 0;
            
            if (_isValid)
            {
                Logger.LogDualColor("External memory access initialized for process", ConsoleColor.Green, 
                    $"{_process.ProcessName} (PID: {_process.Id})", ConsoleColor.Cyan);
                InitializeDefaults();
            }
            else
            {
                Logger.LogError($"Failed to initialize external memory access for process: {_process.ProcessName}");
            }
        }

        public int MaxReadSize { get; set; }
        public int MaxStringLength { get; set; }

        private void InitializeDefaults()
        {
            MaxReadSize = UESConfig.MaxReadSize;
            MaxStringLength = UESConfig.MaxStringLength;
        }

        public nint GetBaseAddress() => _baseAddress;

        public bool IsValid() => _isValid && !_process.HasExited;

        #region Pattern Scanning

        public nint FindPattern(string pattern)
        {
            if (!IsValid()) return 0;
            return FindPattern(pattern, _baseAddress, _process.MainModule!.ModuleMemorySize);
        }

        public nint FindPattern(string pattern, nint start, int length)
        {
            if (!IsValid()) return 0;
            
            try
            {
                var sigScan = new SigScan(_process, start, length);
                var arrayOfBytes = pattern.Split(' ')
                    .Select(b => b.Contains("?") ? (byte)0 : Convert.ToByte(b, 16))
                    .ToArray();
                var strMask = string.Join("", pattern.Split(' ').Select(b => b.Contains("?") ? '?' : 'x'));
                return sigScan.FindPattern(arrayOfBytes, strMask, 0);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Pattern scan failed: {ex.Message}");
                return 0;
            }
        }

        public nint FindStringRef(string str)
        {
            if (!IsValid()) return 0;
            
            try
            {
                var stringAddr = FindPattern(BitConverter.ToString(Encoding.Unicode.GetBytes(str)).Replace("-", " "));
                if (stringAddr == 0) return 0;

                var sigScan = new SigScan(_process, _baseAddress, _process.MainModule!.ModuleMemorySize);
                sigScan.DumpMemory();
                
                for (var i = 0; i < sigScan.Size; i++)
                {
                    if ((sigScan.m_vDumpedRegion[i] == 0x48 || sigScan.m_vDumpedRegion[i] == 0x4c) && 
                        sigScan.m_vDumpedRegion[i + 1] == 0x8d)
                    {
                        var jmpTo = BitConverter.ToInt32(sigScan.m_vDumpedRegion, i + 3);
                        var addr = sigScan.Address + i + jmpTo + 7;
                        if (addr == stringAddr)
                        {
                            return _baseAddress + i;
                        }
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Logger.LogError($"String reference search failed: {ex.Message}");
                return 0;
            }
        }

        #endregion

        #region Memory Reading

        public byte[] ReadMemory(nint address, int length)
        {
            if (!IsValid() || length <= 0) return Array.Empty<byte>();
            
            try
            {
                return ReadMemoryChunked(address, length, MaxReadSize);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to read memory at 0x{address:X}: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        public T ReadMemory<T>(nint address) where T : unmanaged
        {
            if (!IsValid()) return default;
            
            try
            {
                int size = Marshal.SizeOf<T>();
                byte[] buffer = ReadMemory(address, size);
                if (buffer.Length == 0) return default;

                unsafe
                {
                    fixed (byte* ptr = buffer)
                    {
                        return *(T*)ptr;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to read {typeof(T).Name} at 0x{address:X}: {ex.Message}");
                return default;
            }
        }

        public string ReadStringFromMemory(nint address, int length, Encoding encoding)
        {
            if (!IsValid() || length <= 0) return string.Empty;
            
            try
            {
                int byteLength = encoding == Encoding.Unicode ? length * 2 : length;
                var buffer = ReadMemory(address, byteLength);
                if (buffer.Length == 0) return string.Empty;
                
                return encoding.GetString(buffer).TrimEnd('\0');
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to read string at 0x{address:X}: {ex.Message}");
                return string.Empty;
            }
        }

        public string ReadAsciiString(nint address, int maxLength = 256)
        {
            return ReadStringFromMemory(address, Math.Min(maxLength, MaxStringLength), Encoding.ASCII);
        }

        public string ReadUnicodeString(nint address, int maxLength = 256)
        {
            return ReadStringFromMemory(address, Math.Min(maxLength, MaxStringLength), Encoding.Unicode);
        }

        public byte[] ReadMemoryChunked(nint address, int totalSize, int chunkSize = 4096)
        {
            if (!IsValid() || totalSize <= 0) return Array.Empty<byte>();
            
            var buffer = new byte[totalSize];
            int blocks = (totalSize + chunkSize - 1) / chunkSize;
            
            for (int i = 0; i < blocks; i++)
            {
                int blockSize = Math.Min(chunkSize, totalSize - (i * chunkSize));
                var chunk = new byte[blockSize];
                
                try
                {
                    if (!WinAPI.ReadProcessMemory(_process.Handle, address + i * chunkSize, chunk, blockSize, out _))
                    {
                        Logger.LogWarning($"Failed to read memory chunk {i + 1}/{blocks} at 0x{address + i * chunkSize:X}");
                        break;
                    }
                    Array.Copy(chunk, 0, buffer, i * chunkSize, blockSize);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Exception reading memory chunk {i + 1}/{blocks}: {ex.Message}");
                    break;
                }
            }
            
            return buffer;
        }

        #endregion

        #region Memory Writing

        public bool WriteMemory(nint address, byte[] buffer)
        {
            if (!IsValid() || buffer.Length == 0) return false;
            
            try
            {
                return WinAPI.WriteProcessMemory(_process.Handle, address, buffer, buffer.Length, out _);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to write memory at 0x{address:X}: {ex.Message}");
                return false;
            }
        }

        public bool WriteMemory<T>(nint address, T value) where T : unmanaged
        {
            if (!IsValid()) return false;
            
            try
            {
                int objSize = Marshal.SizeOf<T>();
                byte[] objBytes = new byte[objSize];
                
                unsafe
                {
                    fixed (byte* ptr = objBytes)
                    {
                        *(T*)ptr = value;
                    }
                }
                
                return WriteMemory(address, objBytes);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to write {typeof(T).Name} at 0x{address:X}: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Function Execution

        public nint Execute(nint functionPtr, nint a1, nint a2, nint a3, nint a4, params nint[] args)
        {
            if (!IsValid()) return 0;
            
            try
            {
                var retValPtr = AllocateMemory(0x40);
                if (retValPtr == 0) return 0;
                
                WriteMemory(retValPtr, BitConverter.GetBytes((nint)0xcafeb00));

                var asm = BuildAssemblyCode(functionPtr, a1, a2, a3, a4, retValPtr, args);
                var codePtr = AllocateMemory(asm.Count);
                if (codePtr == 0)
                {
                    FreeMemory(retValPtr);
                    return 0;
                }
                
                WriteMemory(codePtr, asm.ToArray());

                var thread = WinAPI.CreateRemoteThread(_process.Handle, IntPtr.Zero, 0, codePtr, IntPtr.Zero, 0, IntPtr.Zero);
                WinAPI.WaitForSingleObject(thread, 10000);
                
                var returnValue = ReadMemory<nint>(retValPtr);
                
                FreeMemory(codePtr);
                FreeMemory(retValPtr);
                WinAPI.CloseHandle(thread);
                
                return returnValue;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Function execution failed: {ex.Message}");
                return 0;
            }
        }

        private System.Collections.Generic.List<byte> BuildAssemblyCode(nint functionPtr, nint a1, nint a2, nint a3, nint a4, nint retValPtr, nint[] args)
        {
            var asm = new System.Collections.Generic.List<byte>();
            
            // sub rsp, 104
            asm.AddRange(new byte[] { 0x48, 0x83, 0xEC });
            asm.Add(104);

            // mov rcx, a1
            asm.AddRange(new byte[] { 0x48, 0xB9 });
            asm.AddRange(BitConverter.GetBytes(a1));

            // mov rdx, a2
            asm.AddRange(new byte[] { 0x48, 0xBA });
            asm.AddRange(BitConverter.GetBytes(a2));

            // mov r8, a3
            asm.AddRange(new byte[] { 0x49, 0xB8 });
            asm.AddRange(BitConverter.GetBytes(a3));

            // mov r9, a4
            asm.AddRange(new byte[] { 0x49, 0xB9 });
            asm.AddRange(BitConverter.GetBytes(a4));

            // Additional arguments on stack
            uint offset = 0u;
            foreach (var arg in args)
            {
                asm.AddRange(new byte[] { 0x48, 0xB8 }); // mov rax, arg
                asm.AddRange(BitConverter.GetBytes(arg));
                asm.AddRange(new byte[] { 0x48, 0x89, 0x44, 0x24, (byte)(0x28 + 8 * offset++) }); // mov [rsp+offset], rax
            }
            
            // mov rax, functionPtr
            asm.AddRange(new byte[] { 0x48, 0xB8 });
            asm.AddRange(BitConverter.GetBytes(functionPtr));

            // call rax
            asm.AddRange(new byte[] { 0xFF, 0xD0 });
            
            // add rsp, 104
            asm.AddRange(new byte[] { 0x48, 0x83, 0xC4 });
            asm.Add(104);

            // mov [retValPtr], rax
            asm.AddRange(new byte[] { 0x48, 0xA3 });
            asm.AddRange(BitConverter.GetBytes((ulong)retValPtr));
            
            // ret
            asm.Add(0xC3);
            
            return asm;
        }

        #endregion

        #region Memory Protection and Allocation

        public nint AllocateMemory(int size)
        {
            if (!IsValid() || size <= 0) return 0;
            
            try
            {
                return WinAPI.VirtualAllocEx(_process.Handle, IntPtr.Zero, size, 0x1000, 0x40);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Memory allocation failed: {ex.Message}");
                return 0;
            }
        }

        public bool FreeMemory(nint address)
        {
            if (!IsValid() || address == 0) return false;
            
            try
            {
                return WinAPI.VirtualFreeEx(_process.Handle, address, 0, 0x8000);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Memory deallocation failed: {ex.Message}");
                return false;
            }
        }

        public uint ProtectMemory(nint address, int size, uint newProtection)
        {
            if (!IsValid() || address == 0 || size <= 0) return 0;
            
            try
            {
                WinAPI.VirtualProtectEx(_process.Handle, address, size, newProtection, out uint oldProtection);
                return oldProtection;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Memory protection change failed: {ex.Message}");
                return 0;
            }
        }

        #endregion

        #region Utility Methods

        public string GetProcessInfo()
        {
            if (!IsValid()) return "Invalid process";
            
            try
            {
                return $"Process: {_process.ProcessName} (PID: {_process.Id}), Base: 0x{_baseAddress:X}, " +
                       $"Memory: {_process.WorkingSet64 / 1024 / 1024} MB";
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to get process info: {ex.Message}");
                return "Unknown process info";
            }
        }

        #endregion
    }
}
