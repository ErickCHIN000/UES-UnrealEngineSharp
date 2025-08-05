using System;
using System.Runtime.InteropServices;

namespace UES.Collections
{
    /// <summary>
    /// Generic array wrapper for UE objects providing efficient array access
    /// Supports both object references and value types
    /// </summary>
    /// <typeparam name="T">Type of array elements</typeparam>
    public class Array<T> : UEObject where T : UEObject, new()
    {
        /// <summary>
        /// Creates a new Array wrapper from a UEObject
        /// </summary>
        /// <param name="obj">Source UEObject containing array data</param>
        public Array(UEObject obj) : base()
        {
            if (obj == null) 
            {
                Address = 0;
                return;
            }
            
            Address = obj.Address;
            _classAddr = obj.ClassAddr;
            _substructAddr = obj._substructAddr;
        }

        /// <summary>
        /// Creates a new Array wrapper from an address
        /// </summary>
        /// <param name="addr">Memory address of the array</param>
        public Array(nint addr) : base(addr) { }

        /// <summary>
        /// Creates a new Array wrapper with specific class address
        /// </summary>
        /// <param name="addr">Memory address of the array</param>
        /// <param name="classAddr">Class address for type information</param>
        public Array(nint addr, nint classAddr) : base(addr) 
        { 
            _classAddr = classAddr; 
        }

        /// <summary>
        /// Gets the number of elements in the array
        /// </summary>
        public new int Num
        {
            get
            {
                if (_num != int.MaxValue) return _num;
                
                if (!UnrealEngine.Instance?.MemoryAccess?.IsValid() == true)
                    return 0;
                
                _num = UnrealEngine.Instance.MemoryAccess.ReadMemory<int>(Address + 8);
                
                // Reasonable upper limit to prevent memory issues
                if (_num > 0x20000) _num = 0x20000;
                if (_num < 0) _num = 0;
                
                return _num;
            }
        }

        /// <summary>
        /// Cached array data for efficient repeated access
        /// </summary>
        public new byte[] ArrayCache
        {
            get
            {
                if (_arrayCache.Length != 0) return _arrayCache;
                
                if (Num <= 0 || Value == 0 || !UnrealEngine.Instance?.MemoryAccess?.IsValid() == true)
                    return System.Array.Empty<byte>();
                
                try
                {
                    _arrayCache = UnrealEngine.Instance.MemoryAccess.ReadMemory(Value, Num * 8);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to read array cache: {ex.Message}");
                    _arrayCache = System.Array.Empty<byte>();
                }
                
                return _arrayCache;
            }
        }

        /// <summary>
        /// Accesses array element by index (object reference mode)
        /// </summary>
        /// <param name="index">Index of the element</param>
        /// <returns>UEObject at the specified index</returns>
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= Num || ArrayCache.Length == 0)
                {
                    return new T();
                }

                try
                {
                    var elementAddress = (nint)BitConverter.ToUInt64(ArrayCache, index * 8);
                    var obj = new T();
                    obj.Address = elementAddress;
                    return obj;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to access array element {index}: {ex.Message}");
                    return new T();
                }
            }
        }

        /// <summary>
        /// Accesses array element by index (value type mode)
        /// </summary>
        /// <param name="index">Index of the element</param>
        /// <param name="isValueType">Set to true for value type access</param>
        /// <returns>Object at the specified index</returns>
        public T this[int index, bool isValueType]
        {
            get 
            {
                if (index < 0 || index >= Num)
                {
                    return new T();
                }

                try
                {
                    if (typeof(T).IsAssignableTo(typeof(UEObject)))
                    {
                        // Calculate size based on struct properties or use default
                        var subStructSize = GetElementSize();
                        var elementAddress = Value + index * subStructSize;
                        
                        var obj = new T();
                        obj.Address = elementAddress;
                        if (_substructAddr != nint.MaxValue)
                        {
                            obj._classAddr = _substructAddr;
                        }
                        return obj;
                    }
                    else
                    {
                        var elementSize = Marshal.SizeOf<T>();
                        var elementAddress = Value + index * elementSize;
                        var obj = new T();
                        obj.Address = elementAddress;
                        return obj;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to access value type array element {index}: {ex.Message}");
                    return new T();
                }
            }
        }

        /// <summary>
        /// Gets all elements as an enumerable collection
        /// </summary>
        /// <returns>Enumerable collection of array elements</returns>
        public System.Collections.Generic.IEnumerable<T> GetElements()
        {
            for (int i = 0; i < Num; i++)
            {
                yield return this[i];
            }
        }

        /// <summary>
        /// Gets all elements as an array
        /// </summary>
        /// <returns>Array containing all elements</returns>
        public T[] ToArray()
        {
            var result = new T[Num];
            for (int i = 0; i < Num; i++)
            {
                result[i] = this[i];
            }
            return result;
        }

        /// <summary>
        /// Clears the cached array data
        /// </summary>
        public void ClearCache()
        {
            _arrayCache = System.Array.Empty<byte>();
            _num = int.MaxValue;
        }

        /// <summary>
        /// Attempts to determine the size of array elements
        /// </summary>
        /// <returns>Size of each element in bytes</returns>
        private int GetElementSize()
        {
            // For AOT compatibility, use a simple size calculation
            // Most UEObject elements use pointer size (8 bytes on 64-bit)
            if (typeof(T).IsAssignableTo(typeof(UEObject)))
            {
                return 8; // Pointer size for object references
            }

            // Default to reasonable size for UE structs
            return 0x28;
        }

        /// <summary>
        /// Gets information about this array
        /// </summary>
        /// <returns>Array information string</returns>
        public override string ToString()
        {
            return $"Array<{typeof(T).Name}>[{Num}] @ 0x{Address:X}";
        }
    }

    /// <summary>
    /// Array implementation for value types
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    public class ValueArray<T> where T : unmanaged
    {
        private readonly nint _address;
        private readonly int _count;
        private readonly int _elementSize;

        /// <summary>
        /// Creates a new value array wrapper
        /// </summary>
        /// <param name="address">Memory address of the array data</param>
        /// <param name="count">Number of elements</param>
        public ValueArray(nint address, int count)
        {
            _address = address;
            _count = Math.Max(0, count);
            _elementSize = Marshal.SizeOf<T>();
        }

        /// <summary>
        /// Gets the number of elements
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Gets the size of each element
        /// </summary>
        public int ElementSize => _elementSize;

        /// <summary>
        /// Accesses element by index
        /// </summary>
        /// <param name="index">Element index</param>
        /// <returns>Value at the specified index</returns>
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= _count || !UnrealEngine.Instance?.MemoryAccess?.IsValid() == true)
                    return default;

                try
                {
                    var elementAddress = _address + index * _elementSize;
                    return UnrealEngine.Instance.MemoryAccess.ReadMemory<T>(elementAddress);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to read value array element {index}: {ex.Message}");
                    return default;
                }
            }
            set
            {
                if (index < 0 || index >= _count || !UnrealEngine.Instance?.MemoryAccess?.IsValid() == true)
                    return;

                try
                {
                    var elementAddress = _address + index * _elementSize;
                    UnrealEngine.Instance.MemoryAccess.WriteMemory(elementAddress, value);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to write value array element {index}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets all elements as an array
        /// </summary>
        /// <returns>Array containing all elements</returns>
        public T[] ToArray()
        {
            if (_count <= 0 || !UnrealEngine.Instance?.MemoryAccess?.IsValid() == true)
                return System.Array.Empty<T>();

            try
            {
                var buffer = UnrealEngine.Instance.MemoryAccess.ReadMemory(_address, _count * _elementSize);
                var result = new T[_count];
                
                unsafe
                {
                    fixed (byte* bufferPtr = buffer)
                    fixed (T* resultPtr = result)
                    {
                        Buffer.MemoryCopy(bufferPtr, resultPtr, buffer.Length, buffer.Length);
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to read value array: {ex.Message}");
                return System.Array.Empty<T>();
            }
        }
    }
}