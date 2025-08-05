using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UES.Engine;
using UES.Memory;

namespace UES
{
    /// <summary>
    /// Main entry point for interacting with the Unreal Engine process
    /// Holds a reference to the active IMemoryAccess implementation
    /// Handles initialization, pattern scanning, and object management
    /// AOT-compatible implementation with proper error handling
    /// </summary>
    public class UnrealEngine
    {
        /// <summary>
        /// Global instance of the UnrealEngine for static access
        /// </summary>
        public static UnrealEngine? Instance { get; private set; }

        /// <summary>
        /// The active memory access implementation (internal or external)
        /// </summary>
        public IMemoryAccess? MemoryAccess { get; private set; }

        /// <summary>
        /// Address of the GNames global table
        /// </summary>
        public nint GNames { get; set; } = 0;

        /// <summary>
        /// Address of the GWorld global pointer
        /// </summary>
        public nint GWorld { get; set; } = 0;

        /// <summary>
        /// Address of the GObjects global table
        /// </summary>
        public nint GObjects { get; set; } = 0;

        /// <summary>
        /// Address of the GEngine global pointer
        /// </summary>
        public nint GEngine { get; set; } = 0;

        /// <summary>
        /// Address of the static constructor function
        /// </summary>
        public nint GStaticCtor { get; set; } = 0;

        /// <summary>
        /// Whether the engine has been successfully initialized
        /// </summary>
        public bool IsInitialized { get; private set; } = false;

        /// <summary>
        /// Manager instances for finding engine components
        /// </summary>
        private GNamesManager? _gnamesManager;
        private GWorldManager? _gworldManager;
        private GObjectsManager? _gobjectsManager;
        private GEngineManager? _gengineManager;
        private GStaticCtorManager? _gstaticctorManager;

        /// <summary>
        /// Creates a new UnrealEngine instance and initializes it
        /// </summary>
        public UnrealEngine()
        {
            Instance = this;
            Logger.LogConfiguration();
            Initialize();
        }

        /// <summary>
        /// Initializes the UnrealEngine with memory access and pattern scanning
        /// </summary>
        public void Initialize()
        {
            Logger.LogInfo("=== Initializing UES - Unreal Engine Sharp ===");

            try
            {
                // Validate configuration
                if (!ValidateConfiguration())
                {
                    Logger.LogError("UES configuration validation failed.");
                    return;
                }

                // Initialize memory access
                if (!TryInitializeMemory())
                {
                    Logger.LogError("Failed to initialize memory access.");
                    return;
                }

                Logger.LogInfo("Memory access initialized successfully.");
                Logger.LogInfo($"Memory access type: {MemoryAccess!.GetType().Name}");
                Logger.LogVerbose($"Process info: {MemoryAccess.GetProcessInfo()}");

                // Initialize managers
                InitializeManagers();

                // Update Unreal Engine addresses
                if (!UpdateAddresses())
                {
                    Logger.LogError("Failed to initialize Unreal Engine memory addresses.");
                    return;
                }

                Logger.LogInfo("Unreal Engine addresses updated successfully.");

                // Enable console if possible
                TryEnableConsole();

                IsInitialized = true;
                Logger.LogInfo("=== UES initialization completed successfully ===");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to initialize UnrealEngine");
                IsInitialized = false;
            }
        }

        /// <summary>
        /// Validates the current UES configuration
        /// </summary>
        /// <returns>True if configuration is valid, false otherwise</returns>
        private bool ValidateConfiguration()
        {
            if (!UESConfig.IsValid)
            {
                Logger.LogError("UES configuration is invalid. Please check your settings.");
                Logger.LogError(UESConfig.GetConfigurationSummary());
                return false;
            }

            Logger.LogVerbose("Configuration validation passed.");
            return true;
        }

        /// <summary>
        /// Attempts to initialize the appropriate memory access implementation
        /// </summary>
        /// <returns>True if memory access was initialized, false otherwise</returns>
        private bool TryInitializeMemory()
        {
            try
            {
                if (UESConfig.UseInternalMemory)
                {
                    Logger.LogInfo("Initializing internal memory access...");
                    MemoryAccess = new InternalMemory();
                    return MemoryAccess.IsValid();
                }

                if (UESConfig.UseExternalMemory)
                {
                    Logger.LogInfo("Initializing external memory access...");
                    
                    // Try with process name first
                    if (!string.IsNullOrEmpty(UESConfig.ExternalProcessName))
                    {
                        try
                        {
                            MemoryAccess = new ExternalMemory(UESConfig.ExternalProcessName);
                            if (MemoryAccess.IsValid())
                                return true;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Failed to initialize external memory with process name '{UESConfig.ExternalProcessName}': {ex.Message}");
                        }
                    }

                    // Try with process object
                    if (UESConfig.ExternalProcess != null)
                    {
                        try
                        {
                            MemoryAccess = new ExternalMemory(UESConfig.ExternalProcess);
                            if (MemoryAccess.IsValid())
                                return true;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Failed to initialize external memory with process object: {ex.Message}");
                        }
                    }

                    Logger.LogError("No valid external process specified or all attempts failed.");
                    return false;
                }

                Logger.LogError("No memory mode configured.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Exception during memory access initialization");
                return false;
            }
        }

        /// <summary>
        /// Initializes the manager instances
        /// </summary>
        private void InitializeManagers()
        {
            Logger.LogVerbose("Initializing engine managers...");
            
            _gnamesManager = new GNamesManager(this);
            _gworldManager = new GWorldManager(this);
            _gobjectsManager = new GObjectsManager(this);
            _gengineManager = new GEngineManager(this);
            _gstaticctorManager = new GStaticCtorManager(this);
            
            Logger.LogVerbose("Engine managers initialized.");
        }

        /// <summary>
        /// Updates all Unreal Engine memory addresses using pattern scanning
        /// </summary>
        /// <returns>True if addresses were updated successfully, false otherwise</returns>
        public bool UpdateAddresses()
        {
            if (MemoryAccess == null || !MemoryAccess.IsValid())
            {
                Logger.LogError("Cannot update addresses: memory access not available.");
                return false;
            }

            try
            {
                Logger.LogInfo("Scanning for Unreal Engine patterns...");

                // Find GNames (required for object name resolution)
                _gnamesManager?.FindGNames();
                if (GNames == 0)
                {
                    Logger.LogError("Failed to find GNames - cannot continue.");
                    return false;
                }

                // Find GWorld
                _gworldManager?.FindGWorld();

                // Find GObjects  
                _gobjectsManager?.FindGObjects();

                // Find GEngine
                _gengineManager?.FindGEngine();

                // Find GStaticCtor
                _gstaticctorManager?.FindGStaticCtor();

                // Validate critical addresses
                var criticalAddresses = new[]
                {
                    ("GNames", GNames),
                    ("GWorld", GWorld),
                    ("GObjects", GObjects)
                };

                foreach (var (name, address) in criticalAddresses)
                {
                    if (address == 0)
                    {
                        Logger.LogWarning($"Failed to find {name} - some functionality may not work.");
                    }
                }

                Logger.LogInfo("Address scanning completed.");
                LogAddressSummary();

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Exception during address scanning");
                return false;
            }
        }

        /// <summary>
        /// Logs a summary of found addresses
        /// </summary>
        private void LogAddressSummary()
        {
            Logger.LogInfo("=== Address Summary ===");
            Logger.LogDualColor("GNames", ConsoleColor.Green, $"0x{GNames:X}", GNames != 0 ? ConsoleColor.Cyan : ConsoleColor.Red);
            Logger.LogDualColor("GWorld", ConsoleColor.Green, $"0x{GWorld:X}", GWorld != 0 ? ConsoleColor.Cyan : ConsoleColor.Red);
            Logger.LogDualColor("GObjects", ConsoleColor.Green, $"0x{GObjects:X}", GObjects != 0 ? ConsoleColor.Cyan : ConsoleColor.Red);
            Logger.LogDualColor("GEngine", ConsoleColor.Green, $"0x{GEngine:X}", GEngine != 0 ? ConsoleColor.Cyan : ConsoleColor.Red);
            Logger.LogDualColor("GStaticCtor", ConsoleColor.Green, $"0x{GStaticCtor:X}", GStaticCtor != 0 ? ConsoleColor.Cyan : ConsoleColor.Red);
            Logger.LogInfo("=====================");
        }

        /// <summary>
        /// Attempts to enable the Unreal Engine console
        /// </summary>
        public void TryEnableConsole()
        {
            try
            {
                if (GEngine == 0 || GStaticCtor == 0 || MemoryAccess == null)
                {
                    Logger.LogWarning("Cannot enable console: required addresses not found.");
                    return;
                }

                Logger.LogInfo("Attempting to enable Unreal Engine console...");

                var engine = new UEObject(GEngine);
                var consoleClassValue = engine["ConsoleClass"].Value;
                var gameViewportAddress = engine["GameViewport"].Address;

                if (consoleClassValue == 0 || gameViewportAddress == 0)
                {
                    Logger.LogWarning("Cannot enable console: required engine objects not found.");
                    return;
                }

                var consoleAddress = MemoryAccess.Execute(GStaticCtor, consoleClassValue, gameViewportAddress, 0, 0);
                if (consoleAddress != 0)
                {
                    var console = new UEObject(consoleAddress);
                    engine["GameViewport"]["ViewportConsole"] = console;
                    Logger.LogInfo("Unreal Engine console enabled successfully.");
                }
                else
                {
                    Logger.LogWarning("Failed to create console object.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Exception while enabling console");
            }
        }

        /// <summary>
        /// Gets status information about the engine initialization
        /// </summary>
        /// <returns>Status information string</returns>
        public string GetStatus()
        {
            var status = $"UnrealEngine Status:\n";
            status += $"  Initialized: {IsInitialized}\n";
            status += $"  Memory Access: {(MemoryAccess?.IsValid() == true ? "Valid" : "Invalid")}\n";
            status += $"  Memory Type: {MemoryAccess?.GetType().Name ?? "None"}\n";
            status += $"  GNames: 0x{GNames:X} {(GNames != 0 ? "✓" : "✗")}\n";
            status += $"  GWorld: 0x{GWorld:X} {(GWorld != 0 ? "✓" : "✗")}\n";
            status += $"  GObjects: 0x{GObjects:X} {(GObjects != 0 ? "✓" : "✗")}\n";
            status += $"  GEngine: 0x{GEngine:X} {(GEngine != 0 ? "✓" : "✗")}\n";
            status += $"  GStaticCtor: 0x{GStaticCtor:X} {(GStaticCtor != 0 ? "✓" : "✗")}";
            
            return status;
        }

        /// <summary>
        /// Reinitializes the engine (useful for process restarts)
        /// </summary>
        public void Reinitialize()
        {
            Logger.LogInfo("Reinitializing UnrealEngine...");
            
            IsInitialized = false;
            GNames = GWorld = GObjects = GEngine = GStaticCtor = 0;
            
            Initialize();
        }

        /// <summary>
        /// Dumps GNames to a text file for analysis
        /// </summary>
        /// <param name="outputPath">Optional output path</param>
        public void DumpGNames(string? outputPath = null)
        {
            if (!IsInitialized || MemoryAccess == null)
            {
                Logger.LogError("Engine not initialized - cannot dump GNames");
                return;
            }

            try
            {
                var outputDir = outputPath ?? MemoryAccess.GetProcessInfo().Split(' ')[0];
                Directory.CreateDirectory(outputDir);
                
                var sb = new StringBuilder();
                var i = 0;
                var processedIndices = new HashSet<int>();

                Logger.LogInfo("Dumping GNames...");

                while (i < 0x100000) // Reasonable upper limit
                {
                    try
                    {
                        if (processedIndices.Contains(i))
                        {
                            i++;
                            continue;
                        }

                        var name = UEObject.GetName(i);
                        if (name == "badIndex")
                        {
                            if ((i & 0xffff) > 0xff00)
                            {
                                i += 0x10000 - (i % 0x10000);
                                continue;
                            }
                            break;
                        }

                        processedIndices.Add(i);
                        sb.AppendLine($"[{i} | 0x{i:X}] {name}");
                        
                        // Skip ahead based on name length to avoid duplicates
                        i += Math.Max(1, name.Length / 2 + name.Length % 2 + 1);
                    }
                    catch
                    {
                        i++;
                    }
                }

                var filePath = Path.Combine(outputDir, "GNamesDump.txt");
                File.WriteAllText(filePath, sb.ToString());
                
                Logger.LogInfo($"GNames dump completed: {filePath}");
                Logger.LogInfo($"Total names found: {processedIndices.Count}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to dump GNames: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates SDK files from the current process
        /// </summary>
        /// <param name="outputPath">Output directory for SDK files</param>
        public void GenerateSDK(string? outputPath = null)
        {
            if (!IsInitialized)
            {
                Logger.LogError("Engine not initialized - cannot generate SDK");
                return;
            }

            try
            {
                var generator = new SDK.SDKGenerator(this);
                generator.DumpSdk(outputPath);
            }
            catch (Exception ex)
            {
                Logger.LogError($"SDK generation failed: {ex.Message}");
            }
        }
    }
}
