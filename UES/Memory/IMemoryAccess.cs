using System;
using System.Text;

namespace UES.Memory
{
    /// <summary>
    /// Interface defining all memory operations for both internal and external memory access
    /// Ensures consistent API across different memory access implementations
    /// AOT-compatible interface design
    /// </summary>
    public interface IMemoryAccess
    {
        /// <summary>
        /// Maximum size for a single read operation (bytes)
        /// </summary>
        int MaxReadSize { get; set; }

        /// <summary>
        /// Maximum length for string reads from memory
        /// </summary>
        int MaxStringLength { get; set; }

        /// <summary>
        /// Gets the base address of the target process
        /// </summary>
        /// <returns>Base address of the main module</returns>
        nint GetBaseAddress();

        /// <summary>
        /// Checks if the memory access is valid and ready
        /// </summary>
        /// <returns>True if memory access is ready, false otherwise</returns>
        bool IsValid();

        #region Pattern Scanning

        /// <summary>
        /// Finds the first occurrence of a byte pattern in the entire process memory
        /// </summary>
        /// <param name="pattern">Byte pattern with wildcards (e.g., "48 89 5C 24 ? 48 89 6C 24")</param>
        /// <returns>Address of the pattern or 0 if not found</returns>
        nint FindPattern(string pattern);

        /// <summary>
        /// Finds the first occurrence of a byte pattern in a specific memory region
        /// </summary>
        /// <param name="pattern">Byte pattern with wildcards</param>
        /// <param name="start">Start address for search</param>
        /// <param name="length">Length of memory region to search</param>
        /// <returns>Address of the pattern or 0 if not found</returns>
        nint FindPattern(string pattern, nint start, int length);

        /// <summary>
        /// Finds a string reference in memory
        /// </summary>
        /// <param name="str">String to search for</param>
        /// <returns>Address where the string is referenced, or 0 if not found</returns>
        nint FindStringRef(string str);

        #endregion

        #region Memory Reading

        /// <summary>
        /// Reads raw bytes from memory
        /// </summary>
        /// <param name="address">Memory address to read from</param>
        /// <param name="length">Number of bytes to read</param>
        /// <returns>Byte array containing the read data</returns>
        byte[] ReadMemory(nint address, int length);

        /// <summary>
        /// Reads a value of specified type from memory
        /// </summary>
        /// <typeparam name="T">Type to read (must be unmanaged)</typeparam>
        /// <param name="address">Memory address to read from</param>
        /// <returns>Value read from memory</returns>
        T ReadMemory<T>(nint address) where T : unmanaged;

        /// <summary>
        /// Reads a string from memory using specified encoding
        /// </summary>
        /// <param name="address">Memory address to read from</param>
        /// <param name="length">Maximum length to read</param>
        /// <param name="encoding">Text encoding to use</param>
        /// <returns>String read from memory</returns>
        string ReadStringFromMemory(nint address, int length, Encoding encoding);

        /// <summary>
        /// Reads a null-terminated ASCII string from memory
        /// </summary>
        /// <param name="address">Memory address to read from</param>
        /// <param name="maxLength">Maximum length to read</param>
        /// <returns>String read from memory</returns>
        string ReadAsciiString(nint address, int maxLength = 256);

        /// <summary>
        /// Reads a null-terminated Unicode string from memory
        /// </summary>
        /// <param name="address">Memory address to read from</param>
        /// <param name="maxLength">Maximum length to read (in characters)</param>
        /// <returns>String read from memory</returns>
        string ReadUnicodeString(nint address, int maxLength = 256);

        #endregion

        #region Memory Writing

        /// <summary>
        /// Writes raw bytes to memory
        /// </summary>
        /// <param name="address">Memory address to write to</param>
        /// <param name="buffer">Data to write</param>
        /// <returns>True if write was successful, false otherwise</returns>
        bool WriteMemory(nint address, byte[] buffer);

        /// <summary>
        /// Writes a value of specified type to memory
        /// </summary>
        /// <typeparam name="T">Type to write (must be unmanaged)</typeparam>
        /// <param name="address">Memory address to write to</param>
        /// <param name="value">Value to write</param>
        /// <returns>True if write was successful, false otherwise</returns>
        bool WriteMemory<T>(nint address, T value) where T : unmanaged;

        #endregion

        #region Function Execution

        /// <summary>
        /// Executes a function at the specified address with given parameters
        /// </summary>
        /// <param name="functionPtr">Address of the function to execute</param>
        /// <param name="a1">First parameter</param>
        /// <param name="a2">Second parameter</param>
        /// <param name="a3">Third parameter</param>
        /// <param name="a4">Fourth parameter</param>
        /// <param name="args">Additional parameters</param>
        /// <returns>Return value of the function</returns>
        nint Execute(nint functionPtr, nint a1, nint a2, nint a3, nint a4, params nint[] args);

        #endregion

        #region Memory Protection and Allocation

        /// <summary>
        /// Allocates memory in the target process
        /// </summary>
        /// <param name="size">Size of memory to allocate</param>
        /// <returns>Address of allocated memory, or 0 if allocation failed</returns>
        nint AllocateMemory(int size);

        /// <summary>
        /// Frees previously allocated memory
        /// </summary>
        /// <param name="address">Address of memory to free</param>
        /// <returns>True if memory was freed successfully, false otherwise</returns>
        bool FreeMemory(nint address);

        /// <summary>
        /// Changes memory protection for a region
        /// </summary>
        /// <param name="address">Address of memory region</param>
        /// <param name="size">Size of memory region</param>
        /// <param name="newProtection">New protection flags</param>
        /// <returns>Previous protection flags, or 0 if operation failed</returns>
        uint ProtectMemory(nint address, int size, uint newProtection);

        #endregion

        #region Utility Methods

        /// <summary>
        /// Gets information about the target process
        /// </summary>
        /// <returns>Process information string</returns>
        string GetProcessInfo();

        /// <summary>
        /// Reads memory in chunks to handle large reads efficiently
        /// </summary>
        /// <param name="address">Starting address</param>
        /// <param name="totalSize">Total size to read</param>
        /// <param name="chunkSize">Size of each chunk</param>
        /// <returns>Complete data as byte array</returns>
        byte[] ReadMemoryChunked(nint address, int totalSize, int chunkSize = 4096);

        #endregion
    }
}
