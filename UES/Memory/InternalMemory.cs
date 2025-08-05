using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace UES.Memory
{
    /// <summary>
    /// Internal memory access implementation for direct memory access within the same process
    /// Uses unsafe pointers for maximum performance when code is injected
    /// AOT-compatible implementation
    /// </summary>
    public unsafe class InternalMemory : IMemoryAccess
    {
        private readonly Process _process;
        private readonly nint _baseAddress;
        private readonly bool _isValid;

        /// <summary>
        /// Creates internal memory access for the current process
        /// </summary>
        public InternalMemory()
        {
            _process = Process.GetCurrentProcess();
            _baseAddress = _process.MainModule?.BaseAddress ?? 0;
            _isValid = _baseAddress != 0;
            
            if (_isValid)
            {
                Logger.LogDualColor("Internal memory access initialized for process", ConsoleColor.Green, 
                    $"{_process.ProcessName} (PID: {_process.Id})", ConsoleColor.Cyan);
                InitializeDefaults();
            }
            else
            {
                Logger.LogError("Failed to initialize internal memory access");
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
                // For internal memory, we can scan more efficiently
                var stringBytes = Encoding.Unicode.GetBytes(str);
                var stringAddr = FindPattern(BitConverter.ToString(stringBytes).Replace("-", " "));
                if (stringAddr == 0) return 0;

                // Search for references to this string
                var baseAddr = (byte*)_baseAddress;
                var moduleSize = _process.MainModule!.ModuleMemorySize;
                
                for (int i = 0; i < moduleSize - 8; i++)
                {
                    // Look for LEA instructions that reference our string
                    if ((baseAddr[i] == 0x48 || baseAddr[i] == 0x4c) && baseAddr[i + 1] == 0x8d)
                    {
                        var offset = *(int*)(baseAddr + i + 3);
                        var refAddr = _baseAddress + i + offset + 7;
                        if (refAddr == stringAddr)
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
                var buffer = new byte[length];
                var src = (byte*)address;
                
                fixed (byte* dest = buffer)
                {
                    Buffer.MemoryCopy(src, dest, length, length);
                }
                
                return buffer;
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
                return *(T*)address;
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
                var buffer = ReadMemory(address, length);
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
            if (!IsValid()) return string.Empty;
            
            try
            {
                var ptr = (byte*)address;
                var length = 0;
                
                // Find null terminator
                while (length < Math.Min(maxLength, MaxStringLength) && ptr[length] != 0)
                {
                    length++;
                }
                
                return ReadStringFromMemory(address, length, Encoding.ASCII);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to read ASCII string at 0x{address:X}: {ex.Message}");
                return string.Empty;
            }
        }

        public string ReadUnicodeString(nint address, int maxLength = 256)
        {
            if (!IsValid()) return string.Empty;
            
            try
            {
                var ptr = (char*)address;
                var length = 0;
                
                // Find null terminator
                while (length < Math.Min(maxLength, MaxStringLength) && ptr[length] != 0)
                {
                    length++;
                }
                
                return ReadStringFromMemory(address, length * 2, Encoding.Unicode);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to read Unicode string at 0x{address:X}: {ex.Message}");
                return string.Empty;
            }
        }

        public byte[] ReadMemoryChunked(nint address, int totalSize, int chunkSize = 4096)
        {
            // For internal memory access, we can read directly without chunking
            return ReadMemory(address, totalSize);
        }

        #endregion

        #region Memory Writing

        public bool WriteMemory(nint address, byte[] buffer)
        {
            if (!IsValid() || buffer.Length == 0) return false;
            
            try
            {
                var dest = (byte*)address;
                
                fixed (byte* src = buffer)
                {
                    Buffer.MemoryCopy(src, dest, buffer.Length, buffer.Length);
                }
                
                return true;
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
                *(T*)address = value;
                return true;
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
                // For internal memory, we can call functions directly
                // This is a simplified approach - in practice, you might need more sophisticated calling conventions
                
                // Create a delegate and invoke it
                // Note: This is a simplified implementation and may need adjustment based on calling convention
                var func = (delegate*<nint, nint, nint, nint, nint>)functionPtr;
                return func(a1, a2, a3, a4);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Function execution failed: {ex.Message}");
                return 0;
            }
        }

        #endregion

        #region Memory Protection and Allocation

        public nint AllocateMemory(int size)
        {
            if (!IsValid() || size <= 0) return 0;
            
            try
            {
                // For internal memory, use managed allocation or native allocation
                var ptr = Marshal.AllocHGlobal(size);
                return ptr;
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
                Marshal.FreeHGlobal(address);
                return true;
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
                // For internal memory access, we might not need to change protection
                // This would depend on the specific use case
                Logger.LogWarning("Memory protection change not implemented for internal memory access");
                return 0;
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
                       $"Memory: {_process.WorkingSet64 / 1024 / 1024} MB [Internal Access]";
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
