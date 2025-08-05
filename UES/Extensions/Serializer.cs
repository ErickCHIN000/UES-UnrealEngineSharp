using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UES.Extensions
{
    /// <summary>
    /// High-performance serialization utilities for unmanaged types
    /// Provides efficient byte conversion for memory operations
    /// </summary>
    public static class Serializer
    {
        /// <summary>
        /// Converts an unmanaged value to its byte representation
        /// </summary>
        /// <typeparam name="T">The unmanaged type to serialize</typeparam>
        /// <param name="value">The value to convert to bytes</param>
        /// <returns>Byte array representation of the value</returns>
        public static unsafe byte[] Serialize<T>(T value) where T : unmanaged
        {
            byte[] buffer = new byte[sizeof(T)];

            fixed (byte* bufferPtr = buffer)
            {
                Buffer.MemoryCopy(&value, bufferPtr, sizeof(T), sizeof(T));
            }

            return buffer;
        }

        /// <summary>
        /// Converts bytes back to an unmanaged value
        /// </summary>
        /// <typeparam name="T">The unmanaged type to deserialize to</typeparam>
        /// <param name="buffer">The byte array containing the data</param>
        /// <param name="offset">Optional offset within the buffer</param>
        /// <returns>The deserialized value</returns>
        public static unsafe T Deserialize<T>(byte[] buffer, int offset = 0) where T : unmanaged
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            
            if (offset < 0 || offset + sizeof(T) > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            T result = new T();

            fixed (byte* bufferPtr = buffer)
            {
                Buffer.MemoryCopy(bufferPtr + offset, &result, sizeof(T), sizeof(T));
            }

            return result;
        }

        /// <summary>
        /// Deserializes multiple values from a byte array
        /// </summary>
        /// <typeparam name="T">The unmanaged type to deserialize to</typeparam>
        /// <param name="buffer">The byte array containing the data</param>
        /// <param name="count">Number of values to deserialize</param>
        /// <param name="offset">Optional offset within the buffer</param>
        /// <returns>Array of deserialized values</returns>
        public static unsafe T[] DeserializeArray<T>(byte[] buffer, int count, int offset = 0) where T : unmanaged
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            
            if (offset < 0 || offset + (sizeof(T) * count) > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            T[] results = new T[count];

            fixed (byte* bufferPtr = buffer)
            fixed (T* resultsPtr = results)
            {
                Buffer.MemoryCopy(bufferPtr + offset, resultsPtr, sizeof(T) * count, sizeof(T) * count);
            }

            return results;
        }

        /// <summary>
        /// Serializes an array of unmanaged values to bytes
        /// </summary>
        /// <typeparam name="T">The unmanaged type to serialize</typeparam>
        /// <param name="values">The array of values to serialize</param>
        /// <returns>Byte array representation of the values</returns>
        public static unsafe byte[] SerializeArray<T>(T[] values) where T : unmanaged
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            byte[] buffer = new byte[sizeof(T) * values.Length];

            fixed (byte* bufferPtr = buffer)
            fixed (T* valuesPtr = values)
            {
                Buffer.MemoryCopy(valuesPtr, bufferPtr, sizeof(T) * values.Length, sizeof(T) * values.Length);
            }

            return buffer;
        }

        /// <summary>
        /// Writes an unmanaged value directly to a byte span
        /// </summary>
        /// <typeparam name="T">The unmanaged type to write</typeparam>
        /// <param name="value">The value to write</param>
        /// <param name="destination">The destination span</param>
        /// <param name="offset">Optional offset within the destination</param>
        public static unsafe void WriteToSpan<T>(T value, Span<byte> destination, int offset = 0) where T : unmanaged
        {
            if (offset < 0 || offset + sizeof(T) > destination.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            fixed (byte* destPtr = destination)
            {
                Buffer.MemoryCopy(&value, destPtr + offset, sizeof(T), sizeof(T));
            }
        }

        /// <summary>
        /// Reads an unmanaged value directly from a byte span
        /// </summary>
        /// <typeparam name="T">The unmanaged type to read</typeparam>
        /// <param name="source">The source span</param>
        /// <param name="offset">Optional offset within the source</param>
        /// <returns>The read value</returns>
        public static unsafe T ReadFromSpan<T>(ReadOnlySpan<byte> source, int offset = 0) where T : unmanaged
        {
            if (offset < 0 || offset + sizeof(T) > source.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            T result = new T();

            fixed (byte* sourcePtr = source)
            {
                Buffer.MemoryCopy(sourcePtr + offset, &result, sizeof(T), sizeof(T));
            }

            return result;
        }

        /// <summary>
        /// Gets the size in bytes of an unmanaged type
        /// </summary>
        /// <typeparam name="T">The unmanaged type</typeparam>
        /// <returns>Size in bytes</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int SizeOf<T>() where T : unmanaged
        {
            return sizeof(T);
        }

        /// <summary>
        /// Converts a pointer to an unmanaged value
        /// </summary>
        /// <typeparam name="T">The unmanaged type</typeparam>
        /// <param name="ptr">Pointer to the data</param>
        /// <returns>The value at the pointer location</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe T ReadFromPointer<T>(void* ptr) where T : unmanaged
        {
            return *(T*)ptr;
        }

        /// <summary>
        /// Writes an unmanaged value to a pointer location
        /// </summary>
        /// <typeparam name="T">The unmanaged type</typeparam>
        /// <param name="ptr">Pointer to the destination</param>
        /// <param name="value">Value to write</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteToPointer<T>(void* ptr, T value) where T : unmanaged
        {
            *(T*)ptr = value;
        }
    }
}