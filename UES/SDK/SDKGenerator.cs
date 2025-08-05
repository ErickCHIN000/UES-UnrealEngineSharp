using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UES.SDK
{
    /// <summary>
    /// Represents a package in the SDK generation system
    /// </summary>
    public class Package
    {
        public string FullName { get; set; } = string.Empty;
        public string Name => FullName.Substring(FullName.LastIndexOf("/") + 1);
        public List<SDKClass> Classes { get; set; } = new List<SDKClass>();
        public List<Package> Dependencies { get; set; } = new List<Package>();

        /// <summary>
        /// Represents a class in the SDK
        /// </summary>
        public class SDKClass
        {
            public string SdkType { get; set; } = string.Empty;
            public string Namespace { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? Parent { get; set; }
            public List<SDKField> Fields { get; set; } = new List<SDKField>();
            public List<SDKFunction> Functions { get; set; } = new List<SDKFunction>();

            /// <summary>
            /// Represents a field or property in a class
            /// </summary>
            public class SDKField
            {
                public string? Type { get; set; }
                public string Name { get; set; } = string.Empty;
                public string? GetterSetter { get; set; }
                public int EnumVal { get; set; }
            }

            /// <summary>
            /// Represents a function in a class
            /// </summary>
            public class SDKFunction
            {
                public string? ReturnType { get; set; }
                public string Name { get; set; } = string.Empty;
                public List<SDKField> Params { get; set; } = new List<SDKField>();
            }
        }
    }

    /// <summary>
    /// SDK generation utility for creating C# code from Unreal Engine objects
    /// </summary>
    public class SDKGenerator
    {
        private readonly UnrealEngine _engine;

        public SDKGenerator(UnrealEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        /// <summary>
        /// Dumps the entire SDK to the specified location
        /// </summary>
        /// <param name="location">Output directory</param>
        public void DumpSdk(string location = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                location = _engine.MemoryAccess?.GetProcessInfo()?.Split(' ')[0] ?? "UnknownProcess";
            }

            Directory.CreateDirectory(location);
            Logger.LogInfo($"Starting SDK dump to: {location}");

            try
            {
                var objectsByOuter = ScanAllObjects();
                var packages = GeneratePackages(objectsByOuter);
                GenerateDependencies(packages);
                WritePackageFiles(packages, location);
                
                Logger.LogInfo($"SDK dump completed. Generated {packages.Count} packages.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"SDK dump failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Scans all objects in the process and organizes them into packages
        /// </summary>
        /// <returns>Dictionary of packages organized by outer object</returns>
        private Dictionary<nint, List<nint>> ScanAllObjects()
        {
            Logger.LogInfo("Scanning all objects in process...");

            if (_engine.MemoryAccess == null || !_engine.MemoryAccess.IsValid())
            {
                throw new InvalidOperationException("Memory access not available");
            }

            var entityList = _engine.MemoryAccess.ReadMemory<nint>(_engine.MemoryAccess.GetBaseAddress() + _engine.GObjects);
            var count = _engine.MemoryAccess.ReadMemory<uint>(_engine.MemoryAccess.GetBaseAddress() + _engine.GObjects + 0x14);
            entityList = _engine.MemoryAccess.ReadMemory<nint>(entityList);

            var packages = new Dictionary<nint, List<nint>>();

            Logger.LogInfo($"Processing {count} objects...");

            for (var i = 0; i < count; i++)
            {
                try
                {
                    var entityAddr = _engine.MemoryAccess.ReadMemory<nint>((entityList + 8 * (i >> 16)) + 24 * (i % 0x10000));
                    if (entityAddr == 0) continue;

                    var outer = FindOuterMostObject(entityAddr);
                    
                    if (!packages.ContainsKey(outer))
                        packages.Add(outer, new List<nint>());
                    
                    packages[outer].Add(entityAddr);
                }
                catch (Exception ex)
                {
                    Logger.LogVerbose($"Error processing object {i}: {ex.Message}");
                }
            }

            Logger.LogInfo($"Found {packages.Count} packages");
            return packages;
        }

        /// <summary>
        /// Finds the outermost object for a given object address
        /// </summary>
        /// <param name="entityAddr">Object address</param>
        /// <returns>Address of the outermost object</returns>
        private nint FindOuterMostObject(nint entityAddr)
        {
            var outer = entityAddr;
            var visited = new HashSet<nint>();

            while (true)
            {
                if (visited.Contains(outer))
                    break; // Circular reference protection
                
                visited.Add(outer);
                
                var tempOuter = _engine.MemoryAccess!.ReadMemory<nint>(outer + UEObject.objectOuterOffset);
                if (tempOuter == 0 || tempOuter == outer)
                    break;
                
                outer = tempOuter;
            }

            return outer;
        }

        /// <summary>
        /// Generates package objects from the scanned data
        /// </summary>
        /// <param name="objectsByOuter">Objects organized by outer</param>
        /// <returns>List of generated packages</returns>
        private List<Package> GeneratePackages(Dictionary<nint, List<nint>> objectsByOuter)
        {
            var packages = new List<Package>();

            foreach (var kvp in objectsByOuter)
            {
                try
                {
                    var packageObj = new UEObject(kvp.Key);
                    var fullPackageName = packageObj.GetName();

                    if (string.IsNullOrEmpty(fullPackageName))
                        continue;

                    var package = new Package { FullName = fullPackageName };
                    var dumpedClasses = new HashSet<string>();

                    foreach (var objAddr in kvp.Value)
                    {
                        try
                        {
                            var obj = new UEObject(objAddr);
                            var className = obj.ClassName;

                            if (dumpedClasses.Contains(className))
                                continue;

                            dumpedClasses.Add(className);

                            var sdkClass = GenerateSDKClass(obj);
                            if (sdkClass != null)
                            {
                                package.Classes.Add(sdkClass);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogVerbose($"Error processing object in package {fullPackageName}: {ex.Message}");
                        }
                    }

                    if (package.Classes.Count > 0)
                    {
                        packages.Add(package);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogVerbose($"Error processing package: {ex.Message}");
                }
            }

            return packages;
        }

        /// <summary>
        /// Generates an SDK class from a UEObject
        /// </summary>
        /// <param name="obj">Source UEObject</param>
        /// <returns>Generated SDK class or null if not suitable</returns>
        private Package.SDKClass? GenerateSDKClass(UEObject obj)
        {
            var className = obj.GetName();
            var fullClassName = obj.ClassName;

            if (string.IsNullOrEmpty(className) || fullClassName.StartsWith("Package"))
                return null;

            var typeName = DetermineClassType(fullClassName);
            if (typeName == "unknown")
                return null;

            var sdkClass = new Package.SDKClass
            {
                Name = className,
                Namespace = obj.GetFullPath(),
                SdkType = typeName
            };

            // Set parent class
            if (typeName == "enum")
            {
                sdkClass.Parent = "int";
                GenerateEnumFields(obj, sdkClass);
            }
            else
            {
                var parentClass = _engine.MemoryAccess!.ReadMemory<nint>(obj.Address + UEObject.structSuperOffset);
                if (parentClass != 0)
                {
                    var parentObj = new UEObject(parentClass);
                    sdkClass.Parent = parentObj.GetName();
                }
                else
                {
                    sdkClass.Parent = "UEObject";
                }

                GenerateClassFields(obj, sdkClass);
                GenerateClassFunctions(obj, sdkClass);
            }

            return sdkClass;
        }

        /// <summary>
        /// Determines the type of class based on its name
        /// </summary>
        /// <param name="className">Class name</param>
        /// <returns>Type string</returns>
        private string DetermineClassType(string className)
        {
            if (className.StartsWith("Class"))
                return "class";
            if (className.StartsWith("ScriptStruct"))
                return "class";
            if (className.StartsWith("Enum"))
                return "enum";
            
            return "unknown";
        }

        /// <summary>
        /// Generates enum fields for an enum class
        /// </summary>
        /// <param name="obj">Source enum object</param>
        /// <param name="sdkClass">Target SDK class</param>
        private void GenerateEnumFields(UEObject obj, Package.SDKClass sdkClass)
        {
            try
            {
                var enumArray = _engine.MemoryAccess!.ReadMemory<nint>(obj.Address + UEObject.enumArrayOffset);
                var enumCount = _engine.MemoryAccess.ReadMemory<int>(obj.Address + UEObject.enumCountOffset);

                for (var i = 0; i < enumCount && i < 1000; i++) // Limit to prevent infinite loops
                {
                    var enumNameIndex = _engine.MemoryAccess.ReadMemory<int>(enumArray + i * 0x10);
                    var enumName = UEObject.GetName(enumNameIndex);
                    
                    if (enumName.Contains(":"))
                        enumName = enumName.Substring(enumName.LastIndexOf(":") + 1);

                    var enumVal = _engine.MemoryAccess.ReadMemory<int>(enumArray + i * 0x10 + 0x8);

                    sdkClass.Fields.Add(new Package.SDKClass.SDKField
                    {
                        Name = enumName,
                        EnumVal = enumVal
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogVerbose($"Error generating enum fields: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates class fields for a regular class
        /// </summary>
        /// <param name="obj">Source object</param>
        /// <param name="sdkClass">Target SDK class</param>
        private void GenerateClassFields(UEObject obj, Package.SDKClass sdkClass)
        {
            try
            {
                var field = obj.Address + UEObject.childPropertiesOffset - UEObject.fieldNextOffset;
                var processedFields = new HashSet<nint>();

                while ((field = _engine.MemoryAccess!.ReadMemory<nint>(field + UEObject.fieldNextOffset)) > 0)
                {
                    if (processedFields.Contains(field))
                        break; // Circular reference protection
                    
                    processedFields.Add(field);

                    var fName = UEObject.GetName(_engine.MemoryAccess.ReadMemory<int>(field + UEObject.fieldNameOffset));
                    var fType = obj.GetFieldType(field);
                    
                    if (string.IsNullOrEmpty(fName))
                        continue;

                    var getterSetter = GenerateGetterSetter(fName, fType, field);
                    var resolvedType = ResolveFieldType(fType, field);

                    sdkClass.Fields.Add(new Package.SDKClass.SDKField
                    {
                        Type = resolvedType,
                        Name = fName,
                        GetterSetter = getterSetter
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogVerbose($"Error generating class fields: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates class functions
        /// </summary>
        /// <param name="obj">Source object</param>
        /// <param name="sdkClass">Target SDK class</param>
        private void GenerateClassFunctions(UEObject obj, Package.SDKClass sdkClass)
        {
            try
            {
                var field = obj.Address + UEObject.childrenOffset - UEObject.funcNextOffset;
                var processedFields = new HashSet<nint>();

                while ((field = _engine.MemoryAccess!.ReadMemory<nint>(field + UEObject.funcNextOffset)) > 0)
                {
                    if (processedFields.Contains(field))
                        break;
                    
                    processedFields.Add(field);

                    var fName = UEObject.GetName(_engine.MemoryAccess.ReadMemory<int>(field + UEObject.nameOffset));
                    
                    if (string.IsNullOrEmpty(fName))
                        continue;

                    var func = new Package.SDKClass.SDKFunction { Name = fName };
                    
                    // Generate function parameters
                    GenerateFunctionParameters(field, func);
                    
                    sdkClass.Functions.Add(func);
                }
            }
            catch (Exception ex)
            {
                Logger.LogVerbose($"Error generating class functions: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates function parameters
        /// </summary>
        /// <param name="funcField">Function field address</param>
        /// <param name="func">Target function object</param>
        private void GenerateFunctionParameters(nint funcField, Package.SDKClass.SDKFunction func)
        {
            try
            {
                var paramField = funcField + UEObject.childPropertiesOffset - UEObject.fieldNextOffset;
                var processedParams = new HashSet<nint>();

                while ((paramField = _engine.MemoryAccess!.ReadMemory<nint>(paramField + UEObject.fieldNextOffset)) > 0)
                {
                    if (processedParams.Contains(paramField))
                        break;
                    
                    processedParams.Add(paramField);

                    var pName = UEObject.GetName(_engine.MemoryAccess.ReadMemory<int>(paramField + UEObject.fieldNameOffset));
                    var pType = ResolveFieldType("", paramField);

                    if (string.IsNullOrEmpty(pName))
                        continue;

                    func.Params.Add(new Package.SDKClass.SDKField
                    {
                        Name = pName,
                        Type = pType
                    });
                }

                // Determine return type from parameters
                var returnParam = func.Params.FirstOrDefault(p => p.Name == "ReturnValue");
                if (returnParam != null)
                {
                    func.ReturnType = returnParam.Type;
                    func.Params.Remove(returnParam);
                }
                else
                {
                    func.ReturnType = "void";
                }
            }
            catch (Exception ex)
            {
                Logger.LogVerbose($"Error generating function parameters: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves field type to C# type
        /// </summary>
        /// <param name="fieldType">Field type name</param>
        /// <param name="fieldAddr">Field address</param>
        /// <returns>Resolved C# type</returns>
        private string ResolveFieldType(string fieldType, nint fieldAddr)
        {
            return fieldType switch
            {
                "BoolProperty" => "bool",
                "ByteProperty" or "Int8Property" => "byte",
                "Int16Property" => "short",
                "UInt16Property" => "ushort",
                "IntProperty" => "int",
                "UInt32Property" => "uint",
                "Int64Property" => "long",
                "UInt64Property" => "ulong",
                "FloatProperty" => "float",
                "DoubleProperty" => "double",
                "StrProperty" => "string",
                "TextProperty" => "string",
                "NameProperty" => "string",
                "ObjectProperty" => GetObjectTypeName(fieldAddr),
                "StructProperty" => GetStructTypeName(fieldAddr),
                "EnumProperty" => GetEnumTypeName(fieldAddr),
                "ArrayProperty" => GetArrayTypeName(fieldAddr),
                _ => "UEObject"
            };
        }

        /// <summary>
        /// Gets object type name for ObjectProperty
        /// </summary>
        /// <param name="fieldAddr">Field address</param>
        /// <returns>Object type name</returns>
        private string GetObjectTypeName(nint fieldAddr)
        {
            try
            {
                var structFieldAddr = _engine.MemoryAccess!.ReadMemory<nint>(fieldAddr + UEObject.propertySize);
                var structFieldIndex = _engine.MemoryAccess.ReadMemory<int>(structFieldAddr + UEObject.nameOffset);
                return UEObject.GetName(structFieldIndex);
            }
            catch
            {
                return "UEObject";
            }
        }

        /// <summary>
        /// Gets struct type name for StructProperty
        /// </summary>
        /// <param name="fieldAddr">Field address</param>
        /// <returns>Struct type name</returns>
        private string GetStructTypeName(nint fieldAddr)
        {
            try
            {
                var structFieldAddr = _engine.MemoryAccess!.ReadMemory<nint>(fieldAddr + UEObject.propertySize);
                var structFieldIndex = _engine.MemoryAccess.ReadMemory<int>(structFieldAddr + UEObject.nameOffset);
                return UEObject.GetName(structFieldIndex);
            }
            catch
            {
                return "UEObject";
            }
        }

        /// <summary>
        /// Gets enum type name for EnumProperty
        /// </summary>
        /// <param name="fieldAddr">Field address</param>
        /// <returns>Enum type name</returns>
        private string GetEnumTypeName(nint fieldAddr)
        {
            try
            {
                var enumFieldAddr = _engine.MemoryAccess!.ReadMemory<nint>(fieldAddr + UEObject.propertySize + 8);
                var enumFieldIndex = _engine.MemoryAccess.ReadMemory<int>(enumFieldAddr + UEObject.nameOffset);
                return UEObject.GetName(enumFieldIndex);
            }
            catch
            {
                return "int";
            }
        }

        /// <summary>
        /// Gets array type name for ArrayProperty
        /// </summary>
        /// <param name="fieldAddr">Field address</param>
        /// <returns>Array type name</returns>
        private string GetArrayTypeName(nint fieldAddr)
        {
            try
            {
                var inner = _engine.MemoryAccess!.ReadMemory<nint>(fieldAddr + UEObject.propertySize);
                var innerClass = _engine.MemoryAccess.ReadMemory<nint>(inner + UEObject.fieldClassOffset);
                var innerType = UEObject.GetName(_engine.MemoryAccess.ReadMemory<int>(innerClass));
                var resolvedInnerType = ResolveFieldType(innerType, inner);
                return $"Array<{resolvedInnerType}>";
            }
            catch
            {
                return "Array<UEObject>";
            }
        }

        /// <summary>
        /// Generates getter/setter code for a field
        /// </summary>
        /// <param name="fieldName">Field name</param>
        /// <param name="fieldType">Field type</param>
        /// <param name="fieldAddr">Field address</param>
        /// <returns>Getter/setter code</returns>
        private string GenerateGetterSetter(string fieldName, string fieldType, nint fieldAddr)
        {
            return fieldType switch
            {
                "BoolProperty" => $"{{ get {{ return this[nameof({fieldName})].Flag; }} set {{ this[nameof({fieldName})].Flag = value; }} }}",
                var t when t.EndsWith("Property") && (t.StartsWith("Int") || t.StartsWith("UInt") || t.StartsWith("Float") || t.StartsWith("Double") || t.StartsWith("Byte")) 
                    => $"{{ get {{ return this[nameof({fieldName})].GetValue<{ResolveFieldType(fieldType, fieldAddr)}>(); }} set {{ this[nameof({fieldName})].SetValue<{ResolveFieldType(fieldType, fieldAddr)}>(value); }} }}",
                "ObjectProperty" or "StructProperty" 
                    => $"{{ get {{ return this[nameof({fieldName})].As<{ResolveFieldType(fieldType, fieldAddr)}>(); }} set {{ this[\"{fieldName}\"] = value; }} }}",
                _ => $"{{ get {{ return this[nameof({fieldName})]; }} set {{ this[nameof({fieldName})] = value; }} }}"
            };
        }

        /// <summary>
        /// Generates dependencies between packages
        /// </summary>
        /// <param name="packages">List of packages</param>
        private void GenerateDependencies(List<Package> packages)
        {
            Logger.LogInfo("Generating package dependencies...");

            foreach (var package in packages)
            {
                foreach (var cls in package.Classes)
                {
                    // Add dependency for parent class
                    if (!string.IsNullOrEmpty(cls.Parent))
                    {
                        AddDependency(package, packages, cls.Parent);
                    }

                    // Add dependencies for field types
                    foreach (var field in cls.Fields)
                    {
                        if (!string.IsNullOrEmpty(field.Type))
                        {
                            var cleanType = field.Type.Replace("Array<", "").Replace(">", "");
                            AddDependency(package, packages, cleanType);
                        }
                    }

                    // Add dependencies for function parameters and return types
                    foreach (var func in cls.Functions)
                    {
                        if (!string.IsNullOrEmpty(func.ReturnType))
                        {
                            AddDependency(package, packages, func.ReturnType);
                        }

                        foreach (var param in func.Params)
                        {
                            if (!string.IsNullOrEmpty(param.Type))
                            {
                                var cleanType = param.Type.Replace("Array<", "").Replace(">", "");
                                AddDependency(package, packages, cleanType);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Adds a dependency to a package if needed
        /// </summary>
        /// <param name="package">Target package</param>
        /// <param name="allPackages">All available packages</param>
        /// <param name="typeName">Type name to find</param>
        private void AddDependency(Package package, List<Package> allPackages, string typeName)
        {
            var fromPackage = allPackages.FirstOrDefault(p => 
                p.Classes.Any(c => c.Name == typeName));

            if (fromPackage != null && fromPackage != package && !package.Dependencies.Contains(fromPackage))
            {
                package.Dependencies.Add(fromPackage);
            }
        }

        /// <summary>
        /// Writes package files to disk
        /// </summary>
        /// <param name="packages">Packages to write</param>
        /// <param name="location">Output directory</param>
        private void WritePackageFiles(List<Package> packages, string location)
        {
            Logger.LogInfo($"Writing {packages.Count} package files...");

            foreach (var package in packages)
            {
                try
                {
                    if (package.Classes.Count == 0)
                        continue;

                    var fileName = $"{package.Name}.cs";
                    var filePath = Path.Combine(location, fileName);
                    var content = GeneratePackageContent(package);

                    File.WriteAllText(filePath, content);
                    Logger.LogVerbose($"Generated: {fileName}");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error writing package {package.Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Generates the content of a package file
        /// </summary>
        /// <param name="package">Package to generate</param>
        /// <returns>Generated C# code</returns>
        private string GeneratePackageContent(Package package)
        {
            var sb = new StringBuilder();

            // Add using statements
            sb.AppendLine("using UES;");
            sb.AppendLine("using UES.Collections;");
            sb.AppendLine("using UES.Extensions;");
            sb.AppendLine("using UEObject = UES.UEObject;");

            // Add dependency using statements
            foreach (var dependency in package.Dependencies)
            {
                sb.AppendLine($"using SDK{dependency.FullName.Replace("/", ".")};");
            }

            // Add namespace
            sb.AppendLine($"namespace SDK{package.FullName.Replace("/", ".")}");
            sb.AppendLine("{");

            // Add classes
            foreach (var cls in package.Classes)
            {
                GenerateClassContent(sb, cls);
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// Generates content for a single class
        /// </summary>
        /// <param name="sb">StringBuilder to append to</param>
        /// <param name="cls">Class to generate</param>
        private void GenerateClassContent(StringBuilder sb, Package.SDKClass cls)
        {
            var parentClause = string.IsNullOrEmpty(cls.Parent) ? "" : $" : {cls.Parent}";
            
            sb.AppendLine($"    public {cls.SdkType} {cls.Name}{parentClause}");
            sb.AppendLine("    {");

            // Add constructor for non-enum types
            if (cls.SdkType != "enum")
            {
                sb.AppendLine($"        public {cls.Name}(nint addr) : base(addr) {{ }}");
            }

            // Add fields
            foreach (var field in cls.Fields)
            {
                if (cls.SdkType == "enum")
                {
                    sb.AppendLine($"        {field.Name} = {field.EnumVal},");
                }
                else
                {
                    sb.AppendLine($"        public {field.Type} {field.Name} {field.GetterSetter}");
                }
            }

            // Add functions
            foreach (var func in cls.Functions)
            {
                var parameters = string.Join(", ", func.Params.Select(p => $"{p.Type} {p.Name}"));
                var args = func.Params.Select(p => p.Name).ToList();
                args.Insert(0, $"nameof({func.Name})");
                var argList = string.Join(", ", args);
                var returnTypeTemplate = func.ReturnType == "void" ? "" : $"<{func.ReturnType}>";
                var returnStatement = func.ReturnType == "void" ? "" : "return ";

                sb.AppendLine($"        public {func.ReturnType} {func.Name}({parameters}) {{ {returnStatement}Invoke{returnTypeTemplate}({argList}); }}");
            }

            sb.AppendLine("    }");
        }
    }
}