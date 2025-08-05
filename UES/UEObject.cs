using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UES.Memory;

namespace UES
{
    public class UEObject
    {
        static UnrealEngine? Engine => UnrealEngine.Instance;
        static IMemoryAccess? memoryAccess => UnrealEngine.Instance?.MemoryAccess;

        public static int objectOuterOffset = 0x20;
        public static int classOffset = 0x10;
        public static int nameOffset = 0x18;
        public static int structSuperOffset = 0x40;
        public static int childPropertiesOffset = 0x50;
        public static int childrenOffset = 0x48;
        public static int fieldNameOffset = 0x28;
        public static int fieldTypeNameOffset = 0;
        public static int fieldClassOffset = 0x8;
        public static int fieldNextOffset = 0x20;
        public static int funcNextOffset = 0x20;
        public static int fieldOffset = 0x4C;
        public static int propertySize = 0x78;
        public static int vTableFuncNum = 66;
        public static int funcFlagsOffset = 0xB0;
        public static int enumArrayOffset = 0x40;
        public static int enumCountOffset = 0x48;

        static ConcurrentDictionary<nint, string> AddrToName = new ConcurrentDictionary<nint, string>();
        static ConcurrentDictionary<nint, nint> AddrToClass = new ConcurrentDictionary<nint, nint>();
        static ConcurrentDictionary<String, Boolean> ClassIsSubClass = new ConcurrentDictionary<string, bool>();
        static ConcurrentDictionary<nint, string> ClassToName = new ConcurrentDictionary<nint, string>();
        static ConcurrentDictionary<nint, ConcurrentDictionary<string, nint>> ClassFieldToAddr = new ConcurrentDictionary<nint, ConcurrentDictionary<string, nint>>();
        static ConcurrentDictionary<nint, int> FieldAddrToOffset = new ConcurrentDictionary<nint, int>();
        static ConcurrentDictionary<nint, string> FieldAddrToType = new ConcurrentDictionary<nint, string>();
        public static void ClearCache()
        {
            AddrToName.Clear();
            AddrToClass.Clear();
            ClassIsSubClass.Clear();
            //ClassToAddr.Clear();
            ClassFieldToAddr.Clear();
            FieldAddrToOffset.Clear();
            FieldAddrToType.Clear();
        }
        public int GetFieldOffset(nint fieldAddr)
        {
            if (FieldAddrToOffset.ContainsKey(fieldAddr)) return FieldAddrToOffset[fieldAddr];
            if (memoryAccess == null) return 0;
            var offset = memoryAccess.ReadMemory<int>(fieldAddr + fieldOffset);
            FieldAddrToOffset[fieldAddr] = offset;
            return offset;
        }
        String? _className;
        public String ClassName
        {
            get
            {
                if (_className != null) return _className;
                _className = GetFullPath();// GetFullName(ClassAddr);
                return _className;
            }
        }
        public nint _substructAddr = nint.MaxValue;
        public nint _classAddr = nint.MaxValue;
        public nint ClassAddr
        {
            get
            {
                if (_classAddr != nint.MaxValue) return _classAddr;
                if (AddrToClass.ContainsKey(Address))
                {
                    _classAddr = AddrToClass[Address];
                    return _classAddr;
                }
                _classAddr = memoryAccess?.ReadMemory<nint>(Address + classOffset) ?? 0;
                AddrToClass[Address] = _classAddr;
                return _classAddr;
            }
        }
        public UEObject() : this(0)
        {
        }

        public UEObject(nint address = 0)
        {
            Address = address;
        }
        public Boolean IsA(nint entityClassAddr, String targetClassName)
        {
            var key = entityClassAddr + ":" + targetClassName;
            if (ClassIsSubClass.ContainsKey(key)) return ClassIsSubClass[key];
            var tempEntityClassAddr = entityClassAddr;
            while (true)
            {
                var tempEntity = new UEObject(tempEntityClassAddr);
                var className = tempEntity.GetFullPath();
                if (className == targetClassName)
                {
                    ClassIsSubClass[key] = true;
                    return true;
                }
                tempEntityClassAddr = memoryAccess.ReadMemory<nint>(tempEntityClassAddr + structSuperOffset);
                if (tempEntityClassAddr == 0) break;
            }
            ClassIsSubClass[key] = false;
            return false;
        }
        public Boolean IsA(String className)
        {
            return IsA(ClassAddr, className);
        }
        public Boolean IsA<T>(out T converted) where T : UEObject, new()
        {
            var n = typeof(T).Namespace;
            n = n?.Substring(3, n.Length - 6).Replace(".", "/") ?? "";
            n = "Class " + n + "." + typeof(T).Name;
            converted = As<T>();
            return IsA(ClassAddr, n);
        }
        public Boolean IsA<T>() where T : UEObject, new()
        {
            if (Address == 0) return false;
            return IsA<T>(out _);
        }
        public static Boolean NewFName = true;
        public static String GetName(int key)
        {
            if (memoryAccess == null || Engine == null) return "badIndex";
            
            var namePtr = memoryAccess.ReadMemory<nint>(Engine.GNames + ((key >> 16) + 2) * 8);
            if (namePtr == 0) return "badIndex";
            var nameEntry = memoryAccess.ReadMemory<UInt16>(namePtr + (key & 0xffff) * 2);
            var nameLength = (Int32)(nameEntry >> 6);
            if (nameLength <= 0) return "badIndex";

            memoryAccess.MaxStringLength = nameLength;
            string result = memoryAccess.ReadStringFromMemory(namePtr + (key & 0xffff) * 2 + 2, nameLength, Encoding.UTF8);
            memoryAccess.MaxStringLength = 0x100;
            return result;
        }
        public String GetName()
        {
            if (memoryAccess == null) return "Unknown";
            return GetName(memoryAccess.ReadMemory<int>(Address + nameOffset));
        }
        public String GetShortName()
        {
            if (ClassToName.ContainsKey(ClassAddr)) return ClassToName[ClassAddr];
            if (memoryAccess == null) return "Unknown";
            var classNameIndex = memoryAccess.ReadMemory<int>(ClassAddr + nameOffset);
            ClassToName[ClassAddr] = GetName(classNameIndex);
            return ClassToName[ClassAddr];
        }
        public String GetFullPath()
        {
            if (AddrToName.ContainsKey(Address)) return AddrToName[Address];
            var classPtr = memoryAccess.ReadMemory<nint>(Address + classOffset);
            var classNameIndex = memoryAccess.ReadMemory<int>(classPtr + nameOffset);
            var name = GetName(classNameIndex);
            var outerEntityAddr = Address;
            var parentName = "";
            while (true)
            {
                var tempOuterEntityAddr = memoryAccess.ReadMemory<nint>(outerEntityAddr + objectOuterOffset);
                //var tempOuterEntityAddr = Memory.ReadMemory<UInt64>(outerEntityAddr + structSuperOffset);
                if (tempOuterEntityAddr == outerEntityAddr || tempOuterEntityAddr == 0) break;
                outerEntityAddr = tempOuterEntityAddr;
                var outerNameIndex = memoryAccess.ReadMemory<int>(outerEntityAddr + nameOffset);
                var tempName = GetName(outerNameIndex);
                if (tempName == "") break;
                if (tempName == "None") break;
                parentName = tempName + "." + parentName;
            }
            name += " " + parentName;
            var nameIndex = memoryAccess.ReadMemory<int>(Address + nameOffset);
            name += GetName(nameIndex);
            AddrToName[Address] = name;
            return name;
        }
        public String GetHierachy()
        {
            var sb = new StringBuilder();
            var tempEntityClassAddr = ClassAddr;
            while (true)
            {
                var tempEntity = new UEObject(tempEntityClassAddr);
                var className = tempEntity.GetFullPath();
                sb.AppendLine(className);
                tempEntityClassAddr = memoryAccess.ReadMemory<nint>(tempEntityClassAddr + structSuperOffset);
                if (tempEntityClassAddr == 0) break;
            }
            return sb.ToString();
        }
        public String GetFieldType(nint fieldAddr)
        {
            if (FieldAddrToType.ContainsKey(fieldAddr)) return FieldAddrToType[fieldAddr];
            var fieldType = memoryAccess.ReadMemory<nint>(fieldAddr + fieldClassOffset);
            var name = GetName(memoryAccess.ReadMemory<int>(fieldType + (NewFName ? 0 : fieldNameOffset)));
            FieldAddrToType[fieldAddr] = name;
            return name;
        }
        nint GetFieldAddr(nint origClassAddr, nint classAddr, string fieldName)
        {
            if (ClassFieldToAddr.ContainsKey(origClassAddr) && ClassFieldToAddr[origClassAddr].ContainsKey(fieldName)) return ClassFieldToAddr[origClassAddr][fieldName];
            var field = classAddr + childPropertiesOffset - fieldNextOffset;
            while ((field = memoryAccess.ReadMemory<nint>(field + fieldNextOffset)) > 0)
            {
                var fName = GetName(memoryAccess.ReadMemory<int>(field + fieldNameOffset));
                if (fName == fieldName)
                {
                    if (!ClassFieldToAddr.ContainsKey(origClassAddr))
                        ClassFieldToAddr[origClassAddr] = new ConcurrentDictionary<string, nint>();
                    ClassFieldToAddr[origClassAddr][fieldName] = field;
                    return field;
                }
            }
            var parentClass = memoryAccess.ReadMemory<nint>(classAddr + structSuperOffset);
            //if (parentClass == classAddr) throw new Exception("parent is me");
            if (parentClass == 0)
            {
                if (!ClassFieldToAddr.ContainsKey(origClassAddr))
                    ClassFieldToAddr[origClassAddr] = new ConcurrentDictionary<string, nint>();
                ClassFieldToAddr[origClassAddr][fieldName] = 0;
                return 0;
            }
            return GetFieldAddr(origClassAddr, parentClass, fieldName);
        }
        public nint GetFieldAddr(string fieldName)
        {
            return GetFieldAddr(ClassAddr, ClassAddr, fieldName);
        }
        public nint GetFuncAddr(nint origClassAddr, nint classAddr, String fieldName)
        {
            if (!NewFName) return GetFieldAddr(origClassAddr, classAddr, fieldName);
            if (ClassFieldToAddr.ContainsKey(origClassAddr) && ClassFieldToAddr[origClassAddr].ContainsKey(fieldName)) return ClassFieldToAddr[origClassAddr][fieldName];
            var field = classAddr + childrenOffset - funcNextOffset;
            while ((field = memoryAccess.ReadMemory<nint>(field + funcNextOffset)) > 0)
            {
                var fName = GetName(memoryAccess.ReadMemory<int>(field + nameOffset));
                if (fName == fieldName)
                {
                    if (!ClassFieldToAddr.ContainsKey(origClassAddr))
                        ClassFieldToAddr[origClassAddr] = new ConcurrentDictionary<String, nint>();
                    ClassFieldToAddr[origClassAddr][fieldName] = field;
                    return field;
                }
            }
            var parentClass = memoryAccess.ReadMemory<nint>(classAddr + structSuperOffset);
            if (parentClass == classAddr) throw new Exception("parent is me");
            if (parentClass == 0) throw new Exception("bad field");
            return GetFuncAddr(origClassAddr, parentClass, fieldName);
        }
        public int FieldOffset;
        public Byte[] Data = Array.Empty<byte>();
        public nint _value = 0xcafeb00;
        public nint Value
        {
            get
            {
                if (_value != 0xcafeb00) return _value;
                _value = memoryAccess.ReadMemory<nint>(Address);
                return _value;
            }
            set
            {
                _value = 0xcafeb00;
                memoryAccess.WriteMemory(Address, value);
            }
        }

        public T GetValue<T>() where T : unmanaged
        {
            return memoryAccess.ReadMemory<T>(Address);
        }
        
        public void SetValue<T>(T value) where T : unmanaged
        {
            memoryAccess.WriteMemory<T>(Address, value);
        }
        UInt64 boolMask = 0;
        public Boolean Flag
        {
            get
            {
                var val = memoryAccess.ReadMemory<UInt64>(Address);
                return ((val & boolMask) == boolMask);
            }
            set
            {
                var val = memoryAccess.ReadMemory<UInt64>(Address);
                if (value) val |= boolMask;
                else val &= ~boolMask;
                memoryAccess.WriteMemory(Address, val);
                //memoryAccess.WriteProcessMemory(Address, value);
            }

        }
        public nint Address;
        public UEObject this[String key]
        {
            get
            {
                var fieldAddr = GetFieldAddr(key);
                if (fieldAddr == 0) return null;
                var fieldType = GetFieldType(fieldAddr);
                var offset = GetFieldOffset(fieldAddr);
                UEObject obj;
                if (fieldType == "ObjectProperty" || fieldType == "ScriptStruct")
                    obj = new UEObject(memoryAccess.ReadMemory<nint>(Address + offset)) { FieldOffset = offset };
                else if (fieldType == "ArrayProperty")
                {
                    obj = new UEObject(Address + offset);
                    obj._classAddr = memoryAccess.ReadMemory<nint>(fieldAddr + fieldClassOffset);
                    var inner = memoryAccess.ReadMemory<nint>(fieldAddr + propertySize);
                    var innerClass = memoryAccess.ReadMemory<nint>(inner + fieldClassOffset);
                    obj._substructAddr = memoryAccess.ReadMemory<nint>(inner + propertySize);
                    //obj._substructAddr;
                }
                else if (fieldType.Contains("Bool"))
                {
                    obj = new UEObject(Address + offset);
                    obj._classAddr = memoryAccess.ReadMemory<nint>(fieldAddr + classOffset);
                    obj.boolMask = memoryAccess.ReadMemory<Byte>(fieldAddr + propertySize);
                }
                else if (fieldType.Contains("Function"))
                {
                    obj = new UEObject(fieldAddr);
                    //obj.BaseObjAddr = Address;
                }
                else if (fieldType.Contains("StructProperty"))
                {
                    obj = new UEObject(Address + offset);
                    obj._classAddr = memoryAccess.ReadMemory<nint>(fieldAddr + propertySize);
                }
                else if (fieldType.Contains("FloatProperty"))
                {
                    obj = new UEObject(Address + offset);
                    obj._classAddr = 0;
                }
                else
                {
                    obj = new UEObject(Address + offset);
                    obj._classAddr = memoryAccess.ReadMemory<nint>(fieldAddr + propertySize);
                }
                if (obj.Address == 0)
                {
                    obj = new UEObject(0);
                    //var classInfo = Engine.Instance.DumpClass(ClassAddr);
                    //throw new Exception("bad addr");
                }
                return obj;
            }
            set
            {
                var fieldAddr = GetFieldAddr(key);
                var offset = GetFieldOffset(fieldAddr);
                memoryAccess.WriteMemory(Address + offset, value.Address);
            }
        }
        public int _num = int.MaxValue;
        public int Num
        {
            get
            {
                if (_num != int.MaxValue) return _num;
                _num = memoryAccess.ReadMemory<int>(Address + 8);
                if (_num > 0x10000) _num = 0x10000;
                return _num;
            }
        }
        public Byte[] _arrayCache = new Byte[0];
        public Byte[] ArrayCache
        {
            get
            {
                if (_arrayCache.Length != 0) return _arrayCache;
                _arrayCache = memoryAccess.ReadMemory(Value, Num * 8);
                return _arrayCache;
            }
        }
        public UEObject this[int index] { get { return new UEObject((nint)BitConverter.ToUInt64(ArrayCache, index * 8)); } }
        public nint _vTableFunc = 0xcafeb00;
        public nint VTableFunc
        {
            get
            {
                if (_vTableFunc != 0xcafeb00) return _vTableFunc;
                _vTableFunc = memoryAccess.ReadMemory<nint>(Address) + vTableFuncNum * 8;
                _vTableFunc = memoryAccess.ReadMemory<nint>(_vTableFunc);
                return _vTableFunc;
            }
        }
        public T Invoke<T>(String funcName, params Object[] args) where T : unmanaged
        {
            if (!memoryAccess?.IsValid() == true)
            {
                Logger.LogWarning($"Memory access not available for function invocation: {funcName}");
                return default(T);
            }

            try
            {
                var funcAddr = GetFuncAddr(ClassAddr, ClassAddr, funcName);
                if (funcAddr == 0)
                {
                    Logger.LogWarning($"Function address not found for: {funcName}");
                    return default(T);
                }

                // Read original function flags
                var initFlags = memoryAccess.ReadMemory<nint>(funcAddr + funcFlagsOffset);
                var nativeFlag = initFlags;
                nativeFlag |= 0x400; // Set native flag

                // Temporarily modify function flags
                memoryAccess.WriteMemory(funcAddr + funcFlagsOffset, nativeFlag);

                // Convert arguments to nint array for execution
                var nativeArgs = new nint[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] is nint ptr)
                        nativeArgs[i] = ptr;
                    else if (args[i] is int intVal)
                        nativeArgs[i] = intVal;
                    else if (args[i] is uint uintVal)
                        nativeArgs[i] = (nint)uintVal;
                    else if (args[i] is long longVal)
                        nativeArgs[i] = (nint)longVal;
                    else if (args[i] is ulong ulongVal)
                        nativeArgs[i] = (nint)ulongVal;
                    else
                        nativeArgs[i] = 0; // Default for unsupported types
                }

                // Execute the function through the VTable
                var result = memoryAccess.Execute(VTableFunc, Address, funcAddr, 0, 0, nativeArgs);

                // Restore original function flags
                memoryAccess.WriteMemory(funcAddr + funcFlagsOffset, initFlags);

                // Convert result to the requested type
                if (typeof(T) == typeof(nint))
                    return (T)(object)result;
                else if (typeof(T) == typeof(int))
                    return (T)(object)(int)result;
                else if (typeof(T) == typeof(uint))
                    return (T)(object)(uint)result;
                else if (typeof(T) == typeof(long))
                    return (T)(object)(long)result;
                else if (typeof(T) == typeof(ulong))
                    return (T)(object)(ulong)result;
                else if (typeof(T) == typeof(float))
                    return (T)(object)BitConverter.Int32BitsToSingle((int)result);
                else if (typeof(T) == typeof(double))
                    return (T)(object)BitConverter.Int64BitsToDouble((long)result);
                else
                {
                    // For other types, try to read from the result address
                    return memoryAccess.ReadMemory<T>(result);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Function invocation failed for {funcName}: {ex.Message}");
                return default(T);
            }
        }
        public void Invoke(String funcName, params Object[] args)
        {
            Invoke<int>(funcName, args);
        }
        public T As<T>() where T : UEObject, new()
        {
            var obj = new T();
            obj.Address = Address;
            obj._classAddr = _classAddr;
            return obj;
        }
        public string Dump()
        {
            var tempEntity = ClassAddr;
            var fields = new List<object> { };
            while (true)
            {
                var classNameIndex = memoryAccess.ReadMemory<int>(tempEntity + nameOffset);
                var name = GetName(classNameIndex);
                var field = tempEntity + childPropertiesOffset - fieldNextOffset;
                while ((field = memoryAccess.ReadMemory<nint>(field + fieldNextOffset)) > 0)
                {
                    var fName = GetName(memoryAccess.ReadMemory<int>(field + fieldNameOffset));
                    var fType = GetFieldType(field);
                    var fValue = "(" + field.ToString("X") + ")";
                    var offset = GetFieldOffset(field);
                    if (fType == "BoolProperty")
                    {
                        fType = "Boolean";
                        fValue = this[fName].Flag.ToString();
                    }
                    else if (fType == "FloatProperty")
                    {
                        fType = "Single";
                        fValue = BitConverter.ToSingle(BitConverter.GetBytes(this[fName].Value), 0).ToString();
                    }
                    else if (fType == "DoubleProperty")
                    {
                        fType = "Double";
                        fValue = BitConverter.ToDouble(BitConverter.GetBytes(this[fName].Value), 0).ToString();
                    }
                    else if (fType == "IntProperty")
                    {
                        fType = "Int32";
                        fValue = ((int)this[fName].Value).ToString("X");
                    }
                    else if (fType == "ObjectProperty" || fType == "StructProperty")
                    {
                        var structFieldIndex = memoryAccess.ReadMemory<int>(memoryAccess.ReadMemory<nint>(field + UEObject.propertySize) + UEObject.nameOffset);
                        fType = UEObject.GetName(structFieldIndex);
                    }
                    /*fields.Add(new
                    {
                        info = fType + " " + fName + " = " + fValue
                    });*/
                    fields.Add(fType + " " + fName + " = " + fValue + " ( @ " + offset.ToString("X") + " - " + (this.Address + offset).ToString("X") + " )");
                }

                field = tempEntity + UEObject.childrenOffset - UEObject.funcNextOffset;
                while ((field = memoryAccess.ReadMemory<nint>(field + funcNextOffset)) > 0)
                {
                    var fName = UEObject.GetName(memoryAccess.ReadMemory<Int32>(field + nameOffset));
                }
                tempEntity = memoryAccess.ReadMemory<nint>(tempEntity + structSuperOffset);
                if (tempEntity == 0) break;
            }
            // Create a manual JSON-like string to avoid reflection-based serialization
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"name\": \"{EscapeJsonString(ClassName + " : " + GetFullPath())}\",");
            sb.AppendLine($"  \"hierarchy\": \"{EscapeJsonString(GetHierachy())}\",");
            sb.AppendLine("  \"fields\": [");
            
            for (int i = 0; i < fields.Count; i++)
            {
                sb.Append($"    \"{EscapeJsonString(fields[i].ToString() ?? "")}\"");
                if (i < fields.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }
            
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            
            return sb.ToString();
        }

        /// <summary>
        /// Escapes special characters in JSON strings for AOT compatibility
        /// </summary>
        /// <param name="value">String to escape</param>
        /// <returns>Escaped string</returns>
        private static string EscapeJsonString(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
                
            return value.Replace("\\", "\\\\")
                       .Replace("\"", "\\\"")
                       .Replace("\n", "\\n")
                       .Replace("\r", "\\r")
                       .Replace("\t", "\\t");
        }
    }
}
