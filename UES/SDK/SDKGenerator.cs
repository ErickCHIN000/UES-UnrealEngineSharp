using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
                public string OriginalName { get; set; } = string.Empty;
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

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(UESConfig.SDKGenerationTimeoutSeconds));
            
            try
            {
                var task = Task.Run(() => DumpSdkInternal(location, cts.Token), cts.Token);
                task.Wait(cts.Token);
                
                Logger.LogInfo("SDK dump completed successfully.");
            }
            catch (OperationCanceledException)
            {
                Logger.LogError($"SDK dump timed out after {UESConfig.SDKGenerationTimeoutSeconds} seconds. This may indicate an infinite loop or very large dataset.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"SDK dump failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Internal SDK dump implementation with cancellation support
        /// </summary>
        /// <param name="location">Output directory</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private void DumpSdkInternal(string location, CancellationToken cancellationToken)
        {
            var objectsByOuter = ScanAllObjects(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            
            var packages = GeneratePackages(objectsByOuter, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            
            GenerateDependencies(packages);
            cancellationToken.ThrowIfCancellationRequested();
            
            WritePackageFiles(packages, location);
            
            Logger.LogInfo($"Generated {packages.Count} packages.");
        }

        /// <summary>
        /// Scans all objects in the process and organizes them into packages
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary of packages organized by outer object</returns>
        private Dictionary<nint, List<nint>> ScanAllObjects(CancellationToken cancellationToken)
        {
            Logger.LogInfo("Scanning all objects in process...");

            if (_engine.MemoryAccess == null || !_engine.MemoryAccess.IsValid())
            {
                throw new InvalidOperationException("Memory access not available");
            }

            var entityList = _engine.MemoryAccess.ReadMemory<nint>(_engine.MemoryAccess.GetBaseAddress() + _engine.GObjects);
            var count = _engine.MemoryAccess.ReadMemory<uint>(_engine.MemoryAccess.GetBaseAddress() + _engine.GObjects + 0x14);
            entityList = _engine.MemoryAccess.ReadMemory<nint>(entityList);

            Logger.LogInfo($"Processing {count} objects...");

            if (UESConfig.EnableMultithreadedSDKGeneration)
            {
                return ScanAllObjectsParallel(entityList, count, cancellationToken);
            }
            else
            {
                return ScanAllObjectsSequential(entityList, count, cancellationToken);
            }
        }

        /// <summary>
        /// Scans objects sequentially with cancellation support
        /// </summary>
        private Dictionary<nint, List<nint>> ScanAllObjectsSequential(nint entityList, uint count, CancellationToken cancellationToken)
        {
            var packages = new Dictionary<nint, List<nint>>();

            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var entityAddr = _engine.MemoryAccess!.ReadMemory<nint>((entityList + 8 * (i >> 16)) + 24 * (i % 0x10000));
                    if (entityAddr == 0) continue;

                    var outer = FindOuterMostObject(entityAddr, cancellationToken);
                    
                    if (!packages.ContainsKey(outer))
                        packages.Add(outer, new List<nint>());
                    
                    packages[outer].Add(entityAddr);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogMemoryReadFailure($"Error processing object {i}: {ex.Message}");
                }
            }

            Logger.LogInfo($"Found {packages.Count} packages");
            return packages;
        }

        /// <summary>
        /// Scans objects in parallel with cancellation support
        /// </summary>
        private Dictionary<nint, List<nint>> ScanAllObjectsParallel(nint entityList, uint count, CancellationToken cancellationToken)
        {
            var packages = new ConcurrentDictionary<nint, ConcurrentBag<nint>>();
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            Parallel.For(0, (int)count, parallelOptions, i =>
            {
                try
                {
                    var entityAddr = _engine.MemoryAccess!.ReadMemory<nint>((entityList + 8 * (i >> 16)) + 24 * (i % 0x10000));
                    if (entityAddr == 0) return;

                    var outer = FindOuterMostObject(entityAddr, cancellationToken);
                    
                    packages.AddOrUpdate(outer, 
                        new ConcurrentBag<nint> { entityAddr },
                        (key, existing) => { existing.Add(entityAddr); return existing; });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogMemoryReadFailure($"Error processing object {i}: {ex.Message}");
                }
            });

            // Convert to regular dictionary with lists
            var result = new Dictionary<nint, List<nint>>();
            foreach (var kvp in packages)
            {
                result[kvp.Key] = kvp.Value.ToList();
            }

            Logger.LogInfo($"Found {result.Count} packages (parallel scan)");
            return result;
        }

        /// <summary>
        /// Finds the outermost object for a given object address
        /// </summary>
        /// <param name="entityAddr">Object address</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Address of the outermost object</returns>
        private nint FindOuterMostObject(nint entityAddr, CancellationToken cancellationToken)
        {
            var outer = entityAddr;
            var visited = new HashSet<nint>();
            var maxIterations = 1000; // Prevent infinite loops even without cancellation
            var iterations = 0;

            while (iterations < maxIterations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (visited.Contains(outer))
                    break; // Circular reference protection
                
                visited.Add(outer);
                
                var tempOuter = _engine.MemoryAccess!.ReadMemory<nint>(outer + UEObject.objectOuterOffset);
                if (tempOuter == 0 || tempOuter == outer)
                    break;
                
                outer = tempOuter;
                iterations++;
            }

            if (iterations >= maxIterations)
            {
                Logger.LogWarning($"FindOuterMostObject hit iteration limit for address 0x{entityAddr:X}, possible infinite loop prevented");
            }

            return outer;
        }

        /// <summary>
        /// Generates package objects from the scanned data
        /// </summary>
        /// <param name="objectsByOuter">Objects organized by outer</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of generated packages</returns>
        private List<Package> GeneratePackages(Dictionary<nint, List<nint>> objectsByOuter, CancellationToken cancellationToken)
        {
            if (UESConfig.EnableMultithreadedSDKGeneration)
            {
                return GeneratePackagesParallel(objectsByOuter, cancellationToken);
            }
            else
            {
                return GeneratePackagesSequential(objectsByOuter, cancellationToken);
            }
        }

        /// <summary>
        /// Generates packages sequentially
        /// </summary>
        private List<Package> GeneratePackagesSequential(Dictionary<nint, List<nint>> objectsByOuter, CancellationToken cancellationToken)
        {
            var packages = new List<Package>();

            foreach (var kvp in objectsByOuter)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var package = GeneratePackageFromObjects(kvp.Key, kvp.Value, cancellationToken);
                    if (package != null)
                    {
                        packages.Add(package);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogMemoryReadFailure($"Error processing package: {ex.Message}");
                }
            }

            return packages;
        }

        /// <summary>
        /// Generates packages in parallel
        /// </summary>
        private List<Package> GeneratePackagesParallel(Dictionary<nint, List<nint>> objectsByOuter, CancellationToken cancellationToken)
        {
            var packages = new ConcurrentBag<Package>();
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            Parallel.ForEach(objectsByOuter, parallelOptions, kvp =>
            {
                try
                {
                    var package = GeneratePackageFromObjects(kvp.Key, kvp.Value, cancellationToken);
                    if (package != null)
                    {
                        packages.Add(package);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogMemoryReadFailure($"Error processing package: {ex.Message}");
                }
            });

            return packages.ToList();
        }

        /// <summary>
        /// Generates a single package from objects
        /// </summary>
        private Package? GeneratePackageFromObjects(nint outerKey, List<nint> objects, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var packageObj = new UEObject(outerKey);
            var fullPackageName = packageObj.GetName();

            if (string.IsNullOrEmpty(fullPackageName))
                return null;

            var package = new Package { FullName = fullPackageName };
            var dumpedClasses = new HashSet<string>();

            foreach (var objAddr in objects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var obj = new UEObject(objAddr);
                    var className = obj.ClassName;

                    if (dumpedClasses.Contains(className))
                        continue;

                    dumpedClasses.Add(className);

                    var sdkClass = GenerateSDKClass(obj, cancellationToken);
                    if (sdkClass != null)
                    {
                        package.Classes.Add(sdkClass);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogMemoryReadFailure($"Error processing object in package {fullPackageName}: {ex.Message}");
                }
            }

            return package.Classes.Count > 0 ? package : null;
        }

        /// <summary>
        /// Generates an SDK class from a UEObject
        /// </summary>
        /// <param name="obj">Source UEObject</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Generated SDK class or null if not suitable</returns>
        private Package.SDKClass? GenerateSDKClass(UEObject obj, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
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
                GenerateEnumFields(obj, sdkClass, cancellationToken);
            }
            else
            {
                var parentClass = _engine.MemoryAccess!.ReadMemory<nint>(obj.Address + UEObject.structSuperOffset);
                if (parentClass != 0)
                {
                    var parentObj = new UEObject(parentClass);
                    var parentName = parentObj.GetName();
                    // Handle ambiguous parent names
                    sdkClass.Parent = SanitizeParentClassName(parentName);
                }
                else
                {
                    sdkClass.Parent = "UEObject";
                }

                GenerateClassFields(obj, sdkClass, cancellationToken);
                GenerateClassFunctions(obj, sdkClass, cancellationToken);
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
        /// <param name="cancellationToken">Cancellation token</param>
        private void GenerateEnumFields(UEObject obj, Package.SDKClass sdkClass, CancellationToken cancellationToken)
        {
            try
            {
                var enumArray = _engine.MemoryAccess!.ReadMemory<nint>(obj.Address + UEObject.enumArrayOffset);
                var enumCount = _engine.MemoryAccess.ReadMemory<int>(obj.Address + UEObject.enumCountOffset);

                var maxEnums = Math.Min(enumCount, 1000); // Limit to prevent infinite loops
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                for (var i = 0; i < maxEnums; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var enumNameIndex = _engine.MemoryAccess.ReadMemory<int>(enumArray + i * 0x10);
                    var enumName = UEObject.GetName(enumNameIndex);
                    
                    if (enumName.Contains(":"))
                        enumName = enumName.Substring(enumName.LastIndexOf(":") + 1);

                    var enumVal = _engine.MemoryAccess.ReadMemory<int>(enumArray + i * 0x10 + 0x8);

                    // Handle duplicate enum names by appending the value
                    var originalName = enumName;
                    var suffix = 0;
                    while (usedNames.Contains(enumName))
                    {
                        suffix++;
                        enumName = $"{originalName}_{suffix}";
                    }
                    
                    usedNames.Add(enumName);

                    sdkClass.Fields.Add(new Package.SDKClass.SDKField
                    {
                        Name = enumName,
                        EnumVal = enumVal
                    });
                }
                
                if (enumCount > 1000)
                {
                    Logger.LogWarning($"Enum {sdkClass.Name} has {enumCount} values, truncated to 1000 to prevent excessive processing");
                }
            }
            catch (Exception ex)
            {
                Logger.LogMemoryReadFailure($"Error generating enum fields: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates class fields for a regular class
        /// </summary>
        /// <param name="obj">Source object</param>
        /// <param name="sdkClass">Target SDK class</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private void GenerateClassFields(UEObject obj, Package.SDKClass sdkClass, CancellationToken cancellationToken)
        {
            try
            {
                var field = obj.Address + UEObject.childPropertiesOffset - UEObject.fieldNextOffset;
                var processedFields = new HashSet<nint>();
                var maxIterations = 10000; // Prevent infinite loops
                var iterations = 0;

                while (iterations < maxIterations && (field = _engine.MemoryAccess!.ReadMemory<nint>(field + UEObject.fieldNextOffset)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    if (processedFields.Contains(field))
                        break; // Circular reference protection
                    
                    processedFields.Add(field);

                    var fName = UEObject.GetName(_engine.MemoryAccess.ReadMemory<int>(field + UEObject.fieldNameOffset));
                    var fType = obj.GetFieldType(field);
                    
                    if (string.IsNullOrEmpty(fName))
                        continue;

                    // Sanitize field name to avoid conflicts with class name
                    var sanitizedFieldName = SanitizeFieldNameForClass(fName, sdkClass.Name);
                    var getterSetter = GenerateGetterSetter(fName, fType, field);
                    var resolvedType = ResolveFieldType(fType, field);

                    sdkClass.Fields.Add(new Package.SDKClass.SDKField
                    {
                        Type = resolvedType,
                        Name = sanitizedFieldName,
                        GetterSetter = getterSetter
                    });
                    
                    iterations++;
                }
                
                if (iterations >= maxIterations)
                {
                    Logger.LogWarning($"Field generation for {sdkClass.Name} hit iteration limit, possible infinite loop prevented");
                }
            }
            catch (Exception ex)
            {
                Logger.LogMemoryReadFailure($"Error generating class fields: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates class functions
        /// </summary>
        /// <param name="obj">Source object</param>
        /// <param name="sdkClass">Target SDK class</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private void GenerateClassFunctions(UEObject obj, Package.SDKClass sdkClass, CancellationToken cancellationToken)
        {
            try
            {
                var field = obj.Address + UEObject.childrenOffset - UEObject.funcNextOffset;
                var processedFields = new HashSet<nint>();
                var maxIterations = 10000; // Prevent infinite loops
                var iterations = 0;

                while (iterations < maxIterations && (field = _engine.MemoryAccess!.ReadMemory<nint>(field + UEObject.funcNextOffset)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    if (processedFields.Contains(field))
                        break;
                    
                    processedFields.Add(field);

                    var fName = UEObject.GetName(_engine.MemoryAccess.ReadMemory<int>(field + UEObject.nameOffset));
                    
                    if (string.IsNullOrEmpty(fName))
                        continue;

                    var func = new Package.SDKClass.SDKFunction 
                    { 
                        Name = SanitizeFunctionNameForClass(fName, sdkClass.Name),
                        OriginalName = fName
                    };
                    
                    // Generate function parameters
                    GenerateFunctionParameters(field, func, cancellationToken);
                    
                    sdkClass.Functions.Add(func);
                    iterations++;
                }
                
                if (iterations >= maxIterations)
                {
                    Logger.LogWarning($"Function generation for {sdkClass.Name} hit iteration limit, possible infinite loop prevented");
                }
            }
            catch (Exception ex)
            {
                Logger.LogMemoryReadFailure($"Error generating class functions: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates function parameters
        /// </summary>
        /// <param name="funcField">Function field address</param>
        /// <param name="func">Target function object</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private void GenerateFunctionParameters(nint funcField, Package.SDKClass.SDKFunction func, CancellationToken cancellationToken)
        {
            try
            {
                var paramField = funcField + UEObject.childPropertiesOffset - UEObject.fieldNextOffset;
                var processedParams = new HashSet<nint>();
                var maxIterations = 1000; // Prevent infinite loops
                var iterations = 0;

                while (iterations < maxIterations && (paramField = _engine.MemoryAccess!.ReadMemory<nint>(paramField + UEObject.fieldNextOffset)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
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
                    
                    iterations++;
                }

                if (iterations >= maxIterations)
                {
                    Logger.LogWarning($"Parameter generation for function {func.Name} hit iteration limit, possible infinite loop prevented");
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
                Logger.LogMemoryReadFailure($"Error generating function parameters: {ex.Message}");
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
                "ObjectProperty" => GetSafeObjectTypeName(fieldAddr),
                "StructProperty" => GetSafeStructTypeName(fieldAddr),
                "EnumProperty" => GetSafeEnumTypeName(fieldAddr),
                "ArrayProperty" => GetSafeArrayTypeName(fieldAddr),
                _ => "UEObject"
            };
        }

        /// <summary>
        /// Gets object type name for ObjectProperty (safe version)
        /// </summary>
        /// <param name="fieldAddr">Field address</param>
        /// <returns>Object type name</returns>
        private string GetSafeObjectTypeName(nint fieldAddr)
        {
            try
            {
                var structFieldAddr = _engine.MemoryAccess!.ReadMemory<nint>(fieldAddr + UEObject.propertySize);
                var structFieldIndex = _engine.MemoryAccess.ReadMemory<int>(structFieldAddr + UEObject.nameOffset);
                var typeName = UEObject.GetName(structFieldIndex);
                
                // Handle ambiguous references
                return SanitizeTypeName(typeName);
            }
            catch
            {
                return "UEObject";
            }
        }

        /// <summary>
        /// Gets struct type name for StructProperty (safe version)
        /// </summary>
        /// <param name="fieldAddr">Field address</param>
        /// <returns>Struct type name</returns>
        private string GetSafeStructTypeName(nint fieldAddr)
        {
            try
            {
                var structFieldAddr = _engine.MemoryAccess!.ReadMemory<nint>(fieldAddr + UEObject.propertySize);
                var structFieldIndex = _engine.MemoryAccess.ReadMemory<int>(structFieldAddr + UEObject.nameOffset);
                var typeName = UEObject.GetName(structFieldIndex);
                
                // Handle ambiguous references
                return SanitizeTypeName(typeName);
            }
            catch
            {
                return "UEObject";
            }
        }

        /// <summary>
        /// Gets enum type name for EnumProperty (safe version)
        /// </summary>
        /// <param name="fieldAddr">Field address</param>
        /// <returns>Enum type name</returns>
        private string GetSafeEnumTypeName(nint fieldAddr)
        {
            try
            {
                var enumFieldAddr = _engine.MemoryAccess!.ReadMemory<nint>(fieldAddr + UEObject.propertySize + 8);
                var enumFieldIndex = _engine.MemoryAccess.ReadMemory<int>(enumFieldAddr + UEObject.nameOffset);
                var typeName = UEObject.GetName(enumFieldIndex);
                
                // Handle ambiguous references - use qualified name for Enum
                return SanitizeTypeName(typeName);
            }
            catch
            {
                return "int";
            }
        }

        /// <summary>
        /// Gets array type name for ArrayProperty (safe version)
        /// </summary>
        /// <param name="fieldAddr">Field address</param>
        /// <returns>Array type name</returns>
        private string GetSafeArrayTypeName(nint fieldAddr)
        {
            try
            {
                var inner = _engine.MemoryAccess!.ReadMemory<nint>(fieldAddr + UEObject.propertySize);
                var innerClass = _engine.MemoryAccess.ReadMemory<nint>(inner + UEObject.fieldClassOffset);
                var innerType = UEObject.GetName(_engine.MemoryAccess.ReadMemory<int>(innerClass));
                var resolvedInnerType = ResolveFieldType(innerType, inner);
                
                // Handle primitive types that can't be used with Array<T> constraint
                var safeInnerType = SanitizeTypeName(resolvedInnerType);
                if (IsPrimitiveType(safeInnerType))
                {
                    // Use List<T> for primitive types
                    return $"List<{safeInnerType}>";
                }
                
                // Check if it's an enum type by checking if the inner type is EnumProperty
                if (innerType == "EnumProperty")
                {
                    // Use EnumArray for enum types
                    return $"EnumArray<{safeInnerType}>";
                }
                
                return $"Array<{safeInnerType}>";
            }
            catch
            {
                return "Array<UEObject>";
            }
        }

        /// <summary>
        /// Checks if a type is a primitive type that cannot be used with Array<T> constraint
        /// </summary>
        /// <param name="typeName">Type name to check</param>
        /// <returns>True if it's a primitive type</returns>
        private bool IsPrimitiveType(string typeName)
        {
            return typeName switch
            {
                "string" or "bool" or "byte" or "short" or "ushort" or "int" or "uint" or 
                "long" or "ulong" or "float" or "double" or "char" => true,
                _ => false
            };
        }

        /// <summary>
        /// Sanitizes parent class names to avoid ambiguous references
        /// </summary>
        /// <param name="parentName">Original parent class name</param>
        /// <returns>Sanitized parent class name</returns>
        private string SanitizeParentClassName(string parentName)
        {
            if (string.IsNullOrEmpty(parentName))
                return "UEObject";

            return parentName switch
            {
                "Object" => "UEObject", // Avoid ambiguity with System.Object
                "BlueprintFunctionLibrary" => "UEObject", // Use UEObject as base
                "DeveloperSettings" => "UEObject", // Use UEObject as base for DeveloperSettings due to constructor issues
                _ => parentName
            };
        }

        /// <summary>
        /// Sanitizes type names to avoid ambiguous references and namespace-as-type errors
        /// </summary>
        /// <param name="typeName">Original type name</param>
        /// <returns>Sanitized type name</returns>
        private string SanitizeTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return "UEObject";

            // Check if this looks like a namespace path instead of a type name
            if (IsLikelyNamespace(typeName))
                return "UEObject";

            return typeName switch
            {
                "Object" => "UEObject", // Avoid ambiguity with System.Object
                "Enum" => "SDK.Script.CoreUObject.Enum", // Use qualified name for Enum
                "String" => "string", // Use C# built-in type
                "Boolean" => "bool", // Use C# built-in type
                "Int32" => "int", // Use C# built-in type
                "Single" => "float", // Use C# built-in type
                "Double" => "double", // Use C# built-in type
                "Guid" => "SDK.Script.CoreUObject.Guid", // Avoid ambiguity with System.Guid
                "Transform" => "SDK.Script.CoreUObject.Transform", // Avoid ambiguity with UES.Extensions.Transform
                _ => typeName
            };
        }

        /// <summary>
        /// Checks if a type name is likely a namespace instead of a concrete type
        /// </summary>
        /// <param name="typeName">Type name to check</param>
        /// <returns>True if it appears to be a namespace</returns>
        private bool IsLikelyNamespace(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return false;

            // Check for common namespace patterns that are not valid type names for As<T>()
            // These patterns indicate the type resolution returned a namespace instead of a concrete type
            var namespacePatterns = new[]
            {
                "MovieScene", // Specific case mentioned in the issue
                "LevelSequence", // Specific case mentioned in the issue
                "RigVM", // Specific case mentioned in the issue
                "Engine",
                "Core", 
                "UMG",
                "Slate",
                "EditorStyle",
                "ToolMenus",
                "UnrealEd",
                "LevelEditor",
                "ContentBrowser",
                "SceneOutliner",
                "DetailCustomizations",
                "PropertyEditor",
                "BlueprintGraph",
                "KismetCompiler",
                "GameplayTasks",
                "AIModule",
                "NavigationSystem",
                "Landscape",
                "Foliage",
                "ClothingSystemRuntimeInterface",
                "ClothingSystemRuntimeCommon",
                "AudioPlatformConfiguration",
                "ControlRig", // Related to RigVM
                "Sequencer", // Related to LevelSequence
                "MovieSceneCapture",
                "Niagara",
                "Chaos",
                "GeometryCollectionEngine",
                "FieldSystemEngine"
            };

            // Check if the type name matches known namespace patterns
            foreach (var pattern in namespacePatterns)
            {
                if (typeName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Additional heuristics for namespace detection:
            // 1. Contains multiple path separators (like Script/Engine/Core)
            if (typeName.Contains("/Script/") || typeName.Contains("/Engine/") || typeName.Contains("/Game/"))
                return true;

            // 2. Ends with common namespace suffixes but not type suffixes
            if (typeName.EndsWith("Module", StringComparison.OrdinalIgnoreCase) || 
                typeName.EndsWith("System", StringComparison.OrdinalIgnoreCase) ||
                typeName.EndsWith("Runtime", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// Sanitizes function names to avoid conflicts with class names
        /// </summary>
        /// <param name="functionName">Original function name</param>
        /// <param name="className">Class name to check against</param>
        /// <returns>Sanitized function name</returns>
        private string SanitizeFunctionNameForClass(string functionName, string className)
        {
            var sanitized = SanitizeFunctionName(functionName);
            
            // If function name matches class name, prefix with underscore
            if (sanitized.Equals(className, StringComparison.OrdinalIgnoreCase))
            {
                sanitized = "_" + sanitized;
            }
            
            return sanitized;
        }

        /// <summary>
        /// Sanitizes field names to avoid conflicts with class names
        /// </summary>
        /// <param name="fieldName">Original field name</param>
        /// <param name="className">Class name to check against</param>
        /// <returns>Sanitized field name</returns>
        private string SanitizeFieldNameForClass(string fieldName, string className)
        {
            if (string.IsNullOrEmpty(fieldName) || string.IsNullOrEmpty(className))
                return fieldName;
            
            // If field name exactly matches class name (case-sensitive), add suffix
            if (fieldName.Equals(className, StringComparison.Ordinal))
            {
                return fieldName + "_Property";
            }
            
            return fieldName;
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
            var resolvedType = ResolveFieldType(fieldType, fieldAddr);
            var sanitizedType = SanitizeTypeName(resolvedType);
            
            return fieldType switch
            {
                "BoolProperty" => $"{{ get {{ return this[\"{fieldName}\"].Flag; }} set {{ this[\"{fieldName}\"].Flag = value; }} }}",
                var t when t.EndsWith("Property") && (t.StartsWith("Int") || t.StartsWith("UInt") || t.StartsWith("Float") || t.StartsWith("Double") || t.StartsWith("Byte")) 
                    => $"{{ get {{ return this[\"{fieldName}\"].GetValue<{sanitizedType}>(); }} set {{ this[\"{fieldName}\"].SetValue<{sanitizedType}>(value); }} }}",
                "StrProperty" or "TextProperty" or "NameProperty" 
                    => $"{{ get {{ return this[\"{fieldName}\"].ToString(); }} set {{ /* String properties are read-only */ }} }}",
                "ObjectProperty" or "StructProperty" 
                    => GenerateObjectStructGetterSetter(fieldName, sanitizedType),
                "ArrayProperty" => GenerateArrayGetterSetter(fieldName, resolvedType),
                "EnumProperty" => GenerateEnumGetterSetter(fieldName, sanitizedType),
                _ => $"{{ get {{ return this[\"{fieldName}\"]; }} set {{ this[\"{fieldName}\"] = value; }} }}"
            };
        }

        /// <summary>
        /// Generates getter/setter for object and struct properties with additional safety checks
        /// </summary>
        /// <param name="fieldName">Field name</param>
        /// <param name="sanitizedType">Sanitized type name</param>
        /// <returns>Getter/setter code</returns>
        private string GenerateObjectStructGetterSetter(string fieldName, string sanitizedType)
        {
            // Final safety check: if the sanitized type is still UEObject or appears to be invalid,
            // use a safe fallback that doesn't use As<T>()
            if (sanitizedType == "UEObject" || IsLikelyNamespace(sanitizedType))
            {
                return $"{{ get {{ return this[nameof({fieldName})]; }} set {{ this[\"{fieldName}\"] = value; }} }}";
            }
            
            // Use As<T>() only when we're confident the type is valid
            return $"{{ get {{ return this[nameof({fieldName})].As<{sanitizedType}>(); }} set {{ this[\"{fieldName}\"] = value; }} }}";
        }

        /// <summary>
        /// Generates getter/setter for array properties with proper type handling
        /// </summary>
        /// <param name="fieldName">Field name</param>
        /// <param name="arrayType">Resolved array type</param>
        /// <returns>Getter/setter code</returns>
        private string GenerateArrayGetterSetter(string fieldName, string arrayType)
        {
            if (arrayType.StartsWith("List<"))
            {
                var innerType = arrayType.Substring(5, arrayType.Length - 6);
                
                // Use appropriate method based on inner type
                if (innerType == "string")
                {
                    return $"{{ get {{ return this[\"{fieldName}\"].GetStringList(); }} set {{ /* Arrays are read-only */ }} }}";
                }
                else if (innerType == "UEObject")
                {
                    return $"{{ get {{ return this[\"{fieldName}\"].GetObjectList(); }} set {{ /* Arrays are read-only */ }} }}";
                }
                else if (IsPrimitiveType(innerType))
                {
                    // For primitive types, use GetList method
                    return $"{{ get {{ return this[\"{fieldName}\"].GetList<{innerType}>(); }} set {{ /* Arrays are read-only */ }} }}";
                }
                else
                {
                    // For other types, return object list and let caller cast
                    return $"{{ get {{ return this[\"{fieldName}\"].GetObjectList(); }} set {{ /* Arrays are read-only */ }} }}";
                }
            }
            else if (arrayType.StartsWith("EnumArray<"))
            {
                // For enum types, use EnumArray constructor
                return $"{{ get {{ return new {arrayType}(this[\"{fieldName}\"]); }} set {{ this[\"{fieldName}\"] = value; }} }}";
            }
            else
            {
                // For UEObject-derived types, use Array constructor
                return $"{{ get {{ return new {arrayType}(this[\"{fieldName}\"]); }} set {{ this[\"{fieldName}\"] = value; }} }}";
            }
        }

        /// <summary>
        /// Generates getter/setter for enum properties to avoid type constraint issues
        /// </summary>
        /// <param name="fieldName">Field name</param>
        /// <param name="enumType">Enum type</param>
        /// <returns>Getter/setter code</returns>
        private string GenerateEnumGetterSetter(string fieldName, string enumType)
        {
            // Avoid using As<T>() with enum types that don't inherit from UEObject
            return $"{{ get {{ return ({enumType})this[\"{fieldName}\"].GetValue<int>(); }} set {{ this[\"{fieldName}\"].SetValue<int>((int)value); }} }}";
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
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UES;");
            sb.AppendLine("using UES.Collections;");
            sb.AppendLine("using UES.Extensions;");
            
            // Add aliases to resolve ambiguous references
            sb.AppendLine("using UEObject = UES.UEObject;");
            sb.AppendLine("using Object = UES.UEObject;"); // Alias Object to UEObject
            sb.AppendLine("using SystemGuid = System.Guid;"); // Alias System.Guid
            sb.AppendLine("using UESTransform = UES.Extensions.Transform;"); // Alias UES.Extensions.Transform

            // Add dependency using statements
            foreach (var dependency in package.Dependencies)
            {
                var depName = dependency.FullName.TrimStart('/').Replace("/", ".");
                sb.AppendLine($"using SDK.{depName};");
            }

            // Add namespace
            var nsName = package.FullName.TrimStart('/').Replace("/", ".");
            sb.AppendLine($"namespace SDK.{nsName}");
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
        /// Sanitizes a function name to be a valid C# identifier
        /// </summary>
        /// <param name="functionName">Original function name</param>
        /// <returns>Sanitized function name that is a valid C# identifier</returns>
        private static string SanitizeFunctionName(string functionName)
        {
            if (string.IsNullOrEmpty(functionName))
                return "UnnamedFunction";

            var sanitized = functionName;
            
            // Replace forward slashes with underscores
            sanitized = sanitized.Replace("/", "_");
            
            // Replace other invalid characters with underscores
            sanitized = sanitized.Replace(":", "_")
                                 .Replace("-", "_")
                                 .Replace(" ", "_")
                                 .Replace(".", "_")
                                 .Replace("(", "_")
                                 .Replace(")", "_")
                                 .Replace("[", "_")
                                 .Replace("]", "_")
                                 .Replace("{", "_")
                                 .Replace("}", "_")
                                 .Replace("<", "_")
                                 .Replace(">", "_")
                                 .Replace(",", "_")
                                 .Replace(";", "_")
                                 .Replace("!", "_")
                                 .Replace("@", "_")
                                 .Replace("#", "_")
                                 .Replace("$", "_")
                                 .Replace("%", "_")
                                 .Replace("^", "_")
                                 .Replace("&", "_")
                                 .Replace("*", "_")
                                 .Replace("+", "_")
                                 .Replace("=", "_")
                                 .Replace("|", "_")
                                 .Replace("\\", "_")
                                 .Replace("?", "_")
                                 .Replace("\"", "_")
                                 .Replace("'", "_")
                                 .Replace("`", "_")
                                 .Replace("~", "_");
            
            // Remove consecutive underscores
            while (sanitized.Contains("__"))
            {
                sanitized = sanitized.Replace("__", "_");
            }
            
            // Remove leading/trailing underscores
            sanitized = sanitized.Trim('_');
            
            // If the result is empty or starts with a digit, prefix with underscore
            if (string.IsNullOrEmpty(sanitized) || char.IsDigit(sanitized[0]))
            {
                sanitized = "_" + sanitized;
            }
            
            // Ensure it's not a C# keyword
            if (IsCSharpKeyword(sanitized))
            {
                sanitized = "_" + sanitized;
            }
            
            return sanitized;
        }

        /// <summary>
        /// Checks if a string is a C# keyword
        /// </summary>
        /// <param name="identifier">Identifier to check</param>
        /// <returns>True if it's a C# keyword</returns>
        private static bool IsCSharpKeyword(string identifier)
        {
            var keywords = new HashSet<string>
            {
                "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
                "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
                "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
                "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
                "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
                "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
                "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
                "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
                "using", "virtual", "void", "volatile", "while"
            };
            
            return keywords.Contains(identifier);
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

            // Add constructors for non-enum types
            if (cls.SdkType != "enum")
            {
                // Add primary constructor
                sb.AppendLine($"        public {cls.Name}(nint addr) : base(addr) {{ }}");
                
                // Add parameterless constructor for generic constraints
                sb.AppendLine($"        public {cls.Name}() : base(0) {{ }}");
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
                args.Insert(0, $"\"{func.OriginalName}\"");
                var argList = string.Join(", ", args);
                
                // Determine if we need to use InvokeUEObject for reference types
                var isUEObjectType = !string.IsNullOrEmpty(func.ReturnType) &&
                                   func.ReturnType != "void" && 
                                   !IsPrimitiveType(func.ReturnType) && 
                                   func.ReturnType != "string" &&
                                   !func.ReturnType.StartsWith("List<") &&
                                   !func.ReturnType.StartsWith("Array<") &&
                                   !func.ReturnType.StartsWith("EnumArray<");
                
                if (func.ReturnType == "void")
                {
                    sb.AppendLine($"        public {func.ReturnType} {func.Name}({parameters}) {{ Invoke({argList}); }}");
                }
                else if (isUEObjectType && func.ReturnType != "UEObject")
                {
                    // For UEObject-derived types, use InvokeUEObject and cast
                    sb.AppendLine($"        public {func.ReturnType} {func.Name}({parameters}) {{ return InvokeUEObject({argList}).As<{func.ReturnType}>(); }}");
                }
                else if (func.ReturnType == "UEObject")
                {
                    // For UEObject, use InvokeUEObject directly
                    sb.AppendLine($"        public {func.ReturnType} {func.Name}({parameters}) {{ return InvokeUEObject({argList}); }}");
                }
                else
                {
                    // For primitive types and others, use the generic Invoke<T>
                    var returnTypeTemplate = $"<{func.ReturnType}>";
                    sb.AppendLine($"        public {func.ReturnType} {func.Name}({parameters}) {{ return Invoke{returnTypeTemplate}({argList}); }}");
                }
            }

            sb.AppendLine("    }");
        }
    }

}
