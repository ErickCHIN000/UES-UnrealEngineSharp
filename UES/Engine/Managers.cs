using System;
using System.Collections.Generic;

namespace UES.Engine
{
    /// <summary>
    /// Manager for finding and managing the GNames global table
    /// GNames contains all string/name references in the Unreal Engine
    /// </summary>
    internal class GNamesManager
    {
        public UnrealEngine UE { get; private set; }
        public nint GNamesPattern { get; private set; } = 0;

        private readonly List<string> _gnamesPatterns = new List<string>
        {
            "74 09 48 8D 15 ? ? ? ? EB 16",
            "74 09 48 8D ? ? 48 8D ? ? E8",
            "74 09 48 8D ? ? ? 48 8D ? ? E8",
            "74 09 48 8D ? ? 49 8B ? E8",
            "74 09 48 8D ? ? ? 49 8B ? E8",
            "74 09 48 8D ? ? 48 8B ? E8",
            "74 09 48 8D ? ? ? 48 8B ? E8"
        };

        public GNamesManager(UnrealEngine unrealEngine)
        {
            UE = unrealEngine ?? throw new ArgumentNullException(nameof(unrealEngine));
        }

        public bool FindGNames()
        {
            Logger.LogInfo("Locating GNames global table...");
            
            if (UE.MemoryAccess == null || !UE.MemoryAccess.IsValid())
            {
                Logger.LogError("Cannot search for GNames: memory access not available");
                return false;
            }

            foreach (var pattern in _gnamesPatterns)
            {
                try
                {
                    Logger.LogVerbose($"Trying GNames pattern: {pattern}");
                    var patternAddr = UE.MemoryAccess.FindPattern(pattern);
                    
                    if (patternAddr == 0)
                    {
                        Logger.LogVerbose($"Pattern not found: {pattern}");
                        continue;
                    }

                    Logger.LogVerbose($"Pattern found at 0x{patternAddr:X}");

                    // Read offset and calculate GNames address
                    int offset = UE.MemoryAccess.ReadMemory<int>(patternAddr + 5);
                    var gnamesAddr = patternAddr + offset + 9;

                    Logger.LogVerbose($"Calculated GNames address: 0x{gnamesAddr:X}");

                    // Validate GNames by testing GetName(3) which should be "ByteProperty"
                    if (ValidateGNames(gnamesAddr))
                    {
                        UE.GNames = gnamesAddr;
                        GNamesPattern = patternAddr;

                        Logger.LogDualColor("GNames found at", ConsoleColor.Green, $"0x{gnamesAddr:X}", ConsoleColor.Cyan);
                        Logger.LogDualColor("GNames pattern found at", ConsoleColor.Green, $"0x{patternAddr:X}", ConsoleColor.Cyan);
                        return true;
                    }
                    else
                    {
                        Logger.LogVerbose($"GNames validation failed for address 0x{gnamesAddr:X}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogVerbose($"Exception while testing pattern '{pattern}': {ex.Message}");
                }
            }
            
            Logger.LogError("Failed to find valid GNames pattern");
            return false;
        }

        private bool ValidateGNames(nint candidateAddress)
        {
            try
            {
                // Temporarily set GNames to test GetName function
                var originalGNames = UE.GNames;
                UE.GNames = candidateAddress;
                
                // Test GetName(3) - should return "ByteProperty" in most UE versions
                var testName = UEObject.GetName(3);
                
                // Restore original value
                UE.GNames = originalGNames;
                
                if (testName == "ByteProperty")
                {
                    Logger.LogVerbose($"GNames validation passed: GetName(3) = '{testName}'");
                    return true;
                }
                else
                {
                    Logger.LogVerbose($"GNames validation failed: GetName(3) = '{testName}' (expected 'ByteProperty')");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogVerbose($"GNames validation exception: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Manager for finding and managing the GWorld global pointer
    /// GWorld contains the current world/level context
    /// </summary>
    internal class GWorldManager
    {
        public UnrealEngine UE { get; private set; }
        public nint GWorldPattern { get; private set; }

        public GWorldManager(UnrealEngine unrealEngine)
        {
            UE = unrealEngine ?? throw new ArgumentNullException(nameof(unrealEngine));
        }

        public bool FindGWorld()
        {
            Logger.LogInfo("Locating GWorld global pointer...");

            if (UE.MemoryAccess == null || !UE.MemoryAccess.IsValid())
            {
                Logger.LogError("Cannot search for GWorld: memory access not available");
                return false;
            }

            try
            {
                // Method 1: Find through SeamlessTravel string reference
                if (TryFindGWorldViaStringReference())
                {
                    return true;
                }

                // Method 2: Alternative pattern
                if (TryFindGWorldViaAlternativePattern())
                {
                    return true;
                }

                Logger.LogError("Failed to find GWorld using all available methods");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Exception while searching for GWorld");
                return false;
            }
        }

        private bool TryFindGWorldViaStringReference()
        {
            try
            {
                Logger.LogVerbose("Trying GWorld method 1: SeamlessTravel string reference");
                
                nint stringAddr = UE.MemoryAccess.FindStringRef("    SeamlessTravel FlushLevelStreaming");
                if (stringAddr == 0)
                {
                    Logger.LogVerbose("SeamlessTravel string reference not found");
                    return false;
                }

                Logger.LogVerbose($"SeamlessTravel string reference found at 0x{stringAddr:X}");

                GWorldPattern = UE.MemoryAccess.FindPattern("48 89 05", stringAddr - 0x500, 0x500);
                if (GWorldPattern == 0)
                {
                    Logger.LogVerbose("GWorld pattern not found near string reference");
                    return false;
                }

                int offset = UE.MemoryAccess.ReadMemory<int>(GWorldPattern + 3);
                if (offset == 0)
                {
                    Logger.LogVerbose("GWorld offset is zero");
                    return false;
                }

                UE.GWorld = GWorldPattern + offset + 7;
                if (UE.GWorld == 0)
                {
                    Logger.LogVerbose("Calculated GWorld address is zero");
                    return false;
                }

                Logger.LogDualColor("GWorld found at", ConsoleColor.Green, $"0x{UE.GWorld:X}", ConsoleColor.Cyan);
                Logger.LogDualColor("GWorld pattern found at", ConsoleColor.Green, $"0x{GWorldPattern:X}", ConsoleColor.Cyan);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogVerbose($"GWorld method 1 failed: {ex.Message}");
                return false;
            }
        }

        private bool TryFindGWorldViaAlternativePattern()
        {
            try
            {
                Logger.LogVerbose("Trying GWorld method 2: Alternative pattern");
                
                GWorldPattern = UE.MemoryAccess.FindPattern("0F 2E ?? 74 ?? 48 8B 1D ?? ?? ?? ?? 48 85 DB 74");
                if (GWorldPattern == 0)
                {
                    Logger.LogVerbose("Alternative GWorld pattern not found");
                    return false;
                }

                int offset = UE.MemoryAccess.ReadMemory<int>(GWorldPattern + 8);
                if (offset == 0)
                {
                    Logger.LogVerbose("Alternative GWorld offset is zero");
                    return false;
                }

                UE.GWorld = GWorldPattern + offset + 12;
                if (UE.GWorld == 0)
                {
                    Logger.LogVerbose("Alternative calculated GWorld address is zero");
                    return false;
                }

                Logger.LogDualColor("GWorld found at", ConsoleColor.Green, $"0x{UE.GWorld:X}", ConsoleColor.Cyan);
                Logger.LogDualColor("GWorld pattern found at", ConsoleColor.Green, $"0x{GWorldPattern:X}", ConsoleColor.Cyan);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogVerbose($"GWorld method 2 failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Manager for finding and managing the GObjects global table
    /// GObjects contains all UObject instances in the engine
    /// </summary>
    internal class GObjectsManager
    {
        public UnrealEngine UE { get; private set; }
        public nint GObjectsPattern { get; private set; }

        public GObjectsManager(UnrealEngine unrealEngine)
        {
            UE = unrealEngine ?? throw new ArgumentNullException(nameof(unrealEngine));
        }

        public bool FindGObjects()
        {
            Logger.LogInfo("Locating GObjects global table...");

            if (UE.MemoryAccess == null || !UE.MemoryAccess.IsValid())
            {
                Logger.LogError("Cannot search for GObjects: memory access not available");
                return false;
            }

            try
            {
                Logger.LogVerbose("Searching for GObjects pattern...");
                
                GObjectsPattern = UE.MemoryAccess.FindPattern("48 8B 05 ? ? ? ? 48 8B 0C C8 ? 8D 04 D1 EB ?");
                if (GObjectsPattern == 0)
                {
                    Logger.LogError("Failed to find GObjects pattern");
                    return false;
                }

                Logger.LogVerbose($"GObjects pattern found at 0x{GObjectsPattern:X}");

                int offset = UE.MemoryAccess.ReadMemory<int>(GObjectsPattern + 3);
                UE.GObjects = GObjectsPattern + offset + 7 - UE.MemoryAccess.GetBaseAddress();
                
                if (UE.GObjects == 0)
                {
                    Logger.LogError("Calculated GObjects address is zero");
                    return false;
                }

                Logger.LogDualColor("GObjects found at", ConsoleColor.Green, $"0x{UE.GObjects:X}", ConsoleColor.Cyan);
                Logger.LogDualColor("GObjects pattern found at", ConsoleColor.Green, $"0x{GObjectsPattern:X}", ConsoleColor.Cyan);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Exception while searching for GObjects");
                return false;
            }
        }
    }

    /// <summary>
    /// Manager for finding and managing the GEngine global pointer
    /// GEngine contains the main engine instance
    /// </summary>
    internal class GEngineManager
    {
        public UnrealEngine UE { get; private set; }
        public nint GEnginePattern { get; private set; }

        public GEngineManager(UnrealEngine unrealEngine)
        {
            UE = unrealEngine ?? throw new ArgumentNullException(nameof(unrealEngine));
        }

        public bool FindGEngine()
        {
            Logger.LogInfo("Locating GEngine global pointer...");

            if (UE.MemoryAccess == null || !UE.MemoryAccess.IsValid())
            {
                Logger.LogError("Cannot search for GEngine: memory access not available");
                return false;
            }

            try
            {
                Logger.LogVerbose("Searching for GEngine pattern...");
                
                GEnginePattern = UE.MemoryAccess.FindPattern("48 8B 0D ?? ?? ?? ?? 48 85 C9 74 1E 48 8B 01 FF 90");
                if (GEnginePattern == 0)
                {
                    Logger.LogError("Failed to find GEngine pattern");
                    return false;
                }

                Logger.LogVerbose($"GEngine pattern found at 0x{GEnginePattern:X}");

                int offset = UE.MemoryAccess.ReadMemory<int>(GEnginePattern + 3);
                UE.GEngine = UE.MemoryAccess.ReadMemory<nint>(GEnginePattern + offset + 7);
                
                if (UE.GEngine == 0)
                {
                    Logger.LogError("GEngine address is zero after calculation");
                    return false;
                }

                Logger.LogDualColor("GEngine found at", ConsoleColor.Green, $"0x{UE.GEngine:X}", ConsoleColor.Cyan);
                Logger.LogDualColor("GEngine pattern found at", ConsoleColor.Green, $"0x{GEnginePattern:X}", ConsoleColor.Cyan);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Exception while searching for GEngine");
                return false;
            }
        }
    }

    /// <summary>
    /// Manager for finding and managing the static constructor function
    /// Used for creating new UObject instances
    /// </summary>
    internal class GStaticCtorManager
    {
        public UnrealEngine UE { get; private set; }
        public nint GStaticCtorPattern { get; private set; }

        public GStaticCtorManager(UnrealEngine unrealEngine)
        {
            UE = unrealEngine ?? throw new ArgumentNullException(nameof(unrealEngine));
        }

        public bool FindGStaticCtor()
        {
            Logger.LogInfo("Locating static constructor function...");

            if (UE.MemoryAccess == null || !UE.MemoryAccess.IsValid())
            {
                Logger.LogError("Cannot search for GStaticCtor: memory access not available");
                return false;
            }

            try
            {
                Logger.LogVerbose("Searching for GStaticCtor pattern...");
                
                GStaticCtorPattern = UE.MemoryAccess.FindPattern("4C 89 44 24 18 55 53 56 57 41 54 41 55 41 56 41 57 48 8D AC 24 ? ? ? ? 48 81 EC ? ? ? ? 48 8B 05 ? ? ? ? 48 33 C4");
                if (GStaticCtorPattern == 0)
                {
                    Logger.LogWarning("Static constructor pattern not found - console functionality may not be available");
                    return false;
                }

                UE.GStaticCtor = GStaticCtorPattern;

                Logger.LogDualColor("GStaticCtor found at", ConsoleColor.Green, $"0x{UE.GStaticCtor:X}", ConsoleColor.Cyan);
                Logger.LogDualColor("GStaticCtor pattern found at", ConsoleColor.Green, $"0x{GStaticCtorPattern:X}", ConsoleColor.Cyan);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Exception while searching for GStaticCtor");
                return false;
            }
        }
    }
}
