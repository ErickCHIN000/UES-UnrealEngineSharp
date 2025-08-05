using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace UES.Memory
{
    /// <summary>
    /// Entity representing a signature pattern for memory scanning
    /// AOT-compatible implementation with proper validation
    /// </summary>
    public class SignatureEntity
    {
        public nint StartAddress { get; set; }
        public int SearchRange { get; set; }
        public byte[] WantedBytes { get; set; }
        public string Mask { get; set; }
        public int AddressOffset { get; set; }

        public SignatureEntity(nint startAddress, int searchRange, byte[] wantedBytes, string mask, int addressOffset = 0)
        {
            StartAddress = startAddress;
            SearchRange = searchRange;
            WantedBytes = wantedBytes ?? throw new ArgumentNullException(nameof(wantedBytes));
            Mask = mask ?? throw new ArgumentNullException(nameof(mask));
            AddressOffset = addressOffset;

            if (wantedBytes.Length != mask.Length)
            {
                throw new ArgumentException("Pattern and mask must have the same length");
            }
        }

        /// <summary>
        /// Scans for this signature pattern in the specified process
        /// </summary>
        /// <param name="process">Target process</param>
        /// <returns>Address of the pattern, or 0 if not found</returns>
        public nint ScanSignature(Process process)
        {
            var sigScan = new SigScan(process, StartAddress, SearchRange);
            return sigScan.FindPattern(WantedBytes, Mask, AddressOffset);
        }
    }

    /// <summary>
    /// Enhanced signature scanning utility for pattern matching in memory
    /// AOT-compatible implementation with improved error handling and performance
    /// </summary>
    public class SigScan
    {
        private readonly object _lockObject = new object();
        
        /// <summary>
        /// The memory dumped from the target process
        /// </summary>
        public byte[]? m_vDumpedRegion { get; private set; }

        /// <summary>
        /// The target process
        /// </summary>
        private Process? m_vProcess;

        /// <summary>
        /// The starting address for memory scanning
        /// </summary>
        private nint m_vAddress;

        /// <summary>
        /// The size of the memory region to scan
        /// </summary>
        private int m_vSize;

        /// <summary>
        /// Whether the memory has been dumped successfully
        /// </summary>
        public bool IsMemoryDumped => m_vDumpedRegion != null && m_vDumpedRegion.Length > 0;

        #region Constructors

        /// <summary>
        /// Default constructor - requires manual setup of properties
        /// </summary>
        public SigScan()
        {
            m_vProcess = null;
            m_vAddress = 0;
            m_vSize = 0;
            m_vDumpedRegion = null;
        }

        /// <summary>
        /// Constructor with parameters for immediate use
        /// </summary>
        /// <param name="process">Target process</param>
        /// <param name="address">Starting address</param>
        /// <param name="size">Size of region to scan</param>
        public SigScan(Process process, nint address, int size)
        {
            m_vProcess = process ?? throw new ArgumentNullException(nameof(process));
            m_vAddress = address;
            m_vSize = size;
            
            if (size <= 0)
                throw new ArgumentException("Size must be positive", nameof(size));
            
            m_vDumpedRegion = null;
        }

        #endregion

        #region Memory Operations

        /// <summary>
        /// Dumps memory from the target process
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public bool DumpMemory()
        {
            lock (_lockObject)
            {
                try
                {
                    // Validation checks
                    if (m_vProcess == null)
                    {
                        Logger.LogError("Cannot dump memory: process is null");
                        return false;
                    }

                    if (m_vProcess.HasExited)
                    {
                        Logger.LogError("Cannot dump memory: process has exited");
                        return false;
                    }

                    if (m_vAddress == 0)
                    {
                        Logger.LogError("Cannot dump memory: address is zero");
                        return false;
                    }

                    if (m_vSize <= 0)
                    {
                        Logger.LogError("Cannot dump memory: invalid size");
                        return false;
                    }

                    if (UnrealEngine.Instance?.MemoryAccess == null)
                    {
                        Logger.LogError("Cannot dump memory: memory access not available");
                        return false;
                    }

                    Logger.LogVerbose($"Dumping memory: 0x{m_vAddress:X} - {m_vSize} bytes");

                    // Dump memory using the current memory access implementation
                    m_vDumpedRegion = UnrealEngine.Instance.MemoryAccess.ReadMemory(m_vAddress, m_vSize);

                    if (m_vDumpedRegion == null || m_vDumpedRegion.Length == 0)
                    {
                        Logger.LogError("Memory dump returned empty data");
                        return false;
                    }

                    Logger.LogVerbose($"Memory dump successful: {m_vDumpedRegion.Length} bytes read");
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "Exception during memory dump");
                    m_vDumpedRegion = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// Resets the dumped memory region to force a redump on next scan
        /// </summary>
        public void ResetRegion()
        {
            lock (_lockObject)
            {
                m_vDumpedRegion = null;
                Logger.LogVerbose("Memory region reset");
            }
        }

        #endregion

        #region Pattern Scanning

        /// <summary>
        /// Finds the first occurrence of a pattern in the dumped memory
        /// </summary>
        /// <param name="pattern">Byte pattern to search for</param>
        /// <param name="mask">Mask string ('x' = match, '?' = wildcard)</param>
        /// <param name="offset">Offset to add to the result address</param>
        /// <returns>Address of the pattern, or 0 if not found</returns>
        public nint FindPattern(byte[] pattern, string mask, int offset = 0)
        {
            if (pattern == null || mask == null)
            {
                Logger.LogError("FindPattern: pattern or mask is null");
                return 0;
            }

            if (pattern.Length != mask.Length)
            {
                Logger.LogError("FindPattern: pattern and mask length mismatch");
                return 0;
            }

            if (pattern.Length == 0)
            {
                Logger.LogError("FindPattern: empty pattern");
                return 0;
            }

            lock (_lockObject)
            {
                try
                {
                    // Ensure memory is dumped
                    if (!IsMemoryDumped && !DumpMemory())
                    {
                        Logger.LogError("FindPattern: failed to dump memory");
                        return 0;
                    }

                    Logger.LogVerbose($"Scanning for pattern: {BitConverter.ToString(pattern)} with mask: {mask}");

                    // Scan for pattern
                    for (int i = 0; i <= m_vDumpedRegion!.Length - pattern.Length; i++)
                    {
                        if (MaskCheck(i, pattern, mask))
                        {
                            var resultAddress = m_vAddress + i + offset;
                            Logger.LogVerbose($"Pattern found at offset {i}, result: 0x{resultAddress:X}");
                            return resultAddress;
                        }
                    }

                    Logger.LogVerbose("Pattern not found");
                    return 0;
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "Exception during pattern scanning");
                    return 0;
                }
            }
        }

        /// <summary>
        /// Finds all occurrences of a pattern in the dumped memory
        /// </summary>
        /// <param name="pattern">Byte pattern to search for</param>
        /// <param name="mask">Mask string ('x' = match, '?' = wildcard)</param>
        /// <param name="offset">Offset to add to result addresses</param>
        /// <returns>List of addresses where pattern was found</returns>
        public List<nint> FindPatterns(byte[] pattern, string mask, int offset = 0)
        {
            var results = new List<nint>();

            if (pattern == null || mask == null)
            {
                Logger.LogError("FindPatterns: pattern or mask is null");
                return results;
            }

            if (pattern.Length != mask.Length)
            {
                Logger.LogError("FindPatterns: pattern and mask length mismatch");
                return results;
            }

            if (pattern.Length == 0)
            {
                Logger.LogError("FindPatterns: empty pattern");
                return results;
            }

            lock (_lockObject)
            {
                try
                {
                    // Ensure memory is dumped
                    if (!IsMemoryDumped && !DumpMemory())
                    {
                        Logger.LogError("FindPatterns: failed to dump memory");
                        return results;
                    }

                    Logger.LogVerbose($"Scanning for all occurrences of pattern: {BitConverter.ToString(pattern)}");

                    // Scan for all pattern occurrences
                    for (int i = 0; i <= m_vDumpedRegion!.Length - pattern.Length; i++)
                    {
                        if (MaskCheck(i, pattern, mask))
                        {
                            var resultAddress = m_vAddress + i + offset;
                            results.Add(resultAddress);
                            Logger.LogVerbose($"Pattern found at offset {i}, result: 0x{resultAddress:X}");
                        }
                    }

                    Logger.LogVerbose($"Found {results.Count} pattern occurrences");
                    return results;
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "Exception during pattern scanning");
                    return results;
                }
            }
        }

        /// <summary>
        /// Checks if the pattern matches at the specified offset using the mask
        /// </summary>
        /// <param name="offset">Offset in the dumped memory</param>
        /// <param name="pattern">Pattern to match</param>
        /// <param name="mask">Mask for comparison</param>
        /// <returns>True if pattern matches, false otherwise</returns>
        private bool MaskCheck(int offset, byte[] pattern, string mask)
        {
            try
            {
                if (m_vDumpedRegion == null || offset + pattern.Length > m_vDumpedRegion.Length)
                    return false;

                for (int i = 0; i < pattern.Length; i++)
                {
                    // Skip wildcard bytes
                    if (mask[i] == '?')
                        continue;

                    // Check for exact match
                    if (mask[i] == 'x' && pattern[i] != m_vDumpedRegion[offset + i])
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Converts a hex string pattern to byte array and mask
        /// </summary>
        /// <param name="pattern">Pattern string like "48 89 ? ? 48 8B"</param>
        /// <returns>Tuple of byte array and mask string</returns>
        public static (byte[] bytes, string mask) ParsePattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                throw new ArgumentException("Pattern cannot be empty", nameof(pattern));

            var parts = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[parts.Length];
            var mask = new char[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "?" || parts[i] == "??")
                {
                    bytes[i] = 0;
                    mask[i] = '?';
                }
                else
                {
                    if (byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                    {
                        mask[i] = 'x';
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid hex value: {parts[i]}", nameof(pattern));
                    }
                }
            }

            return (bytes, new string(mask));
        }

        /// <summary>
        /// Gets information about the current scan configuration
        /// </summary>
        /// <returns>Information string</returns>
        public string GetScanInfo()
        {
            return $"SigScan: Process={m_vProcess?.ProcessName ?? "None"}, " +
                   $"Address=0x{m_vAddress:X}, Size={m_vSize}, " +
                   $"Dumped={IsMemoryDumped}";
        }

        #endregion

        #region Properties

        public Process? Process
        {
            get => m_vProcess;
            set => m_vProcess = value;
        }

        public nint Address
        {
            get => m_vAddress;
            set => m_vAddress = value;
        }

        public int Size
        {
            get => m_vSize;
            set => m_vSize = value;
        }

        #endregion
    }
}
