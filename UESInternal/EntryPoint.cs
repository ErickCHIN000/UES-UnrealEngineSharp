using System.Runtime.InteropServices;
using UES;

namespace UESInternal
{
    public static class EntryPoint
    {
        private static bool _allocateConsole = true;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) }, EntryPoint = "DllMain")]
        public static int DllMain(nint hinstDLL, uint fdwReason, nint lpvReserved)
        {
            if (fdwReason == 1) // DLL_PROCESS_ATTACH
            {
                var workerDel = new Action(MainThread);
                IntPtr fnPtr = Marshal.GetFunctionPointerForDelegate(workerDel);
                _ = CreateThread(
                    IntPtr.Zero,
                    UIntPtr.Zero,
                    fnPtr,
                    IntPtr.Zero,
                    0,
                    out _);
            }
            
            if (_allocateConsole)
            {
                AllocConsole();
            }
            
            return 1;
        }

        private static void MainThread()
        {
            try
            {
                Console.WriteLine("=== UES Internal Example ===");
                Console.WriteLine("This example demonstrates internal memory access from within the target process.");
                Console.WriteLine();

                // Configure UES for internal memory access
                UESConfig.ConfigureForInternal();
                
                // Enable verbose logging for demonstration
                UESConfig.EnableVerboseLogging = true;
                UESConfig.EnableConsoleLogging = true;
                
                Console.WriteLine("Configuration:");
                Console.WriteLine(UESConfig.GetConfigurationSummary());
                Console.WriteLine();

                // Initialize UES with the configured settings
                var unrealEngine = new UnrealEngine();

                if (unrealEngine.IsInitialized)
                {
                    Console.WriteLine("✅ UES initialized successfully!");
                    Console.WriteLine();
                    Console.WriteLine("Status:");
                    Console.WriteLine(unrealEngine.GetStatus());
                    Console.WriteLine();
                    
                    // Demonstrate some basic functionality
                    if (unrealEngine.GNames != 0)
                    {
                        Console.WriteLine("Testing GNames functionality:");
                        try
                        {
                            var testName = UEObject.GetName(3);
                            Console.WriteLine($"  GetName(3) = '{testName}'");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  GetName test failed: {ex.Message}");
                        }
                    }
                    
                    if (unrealEngine.GWorld != 0)
                    {
                        Console.WriteLine("Testing GWorld access:");
                        try
                        {
                            var world = new UEObject(unrealEngine.MemoryAccess!.ReadMemory<nint>(unrealEngine.GWorld));
                            if (world.Address != 0)
                            {
                                Console.WriteLine($"  World object at: 0x{world.Address:X}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  GWorld test failed: {ex.Message}");
                        }
                    }
                    
                    Console.WriteLine();
                    Console.WriteLine("UES is now running internally. Press Ctrl+C to exit.");
                }
                else
                {
                    Console.WriteLine("❌ Failed to initialize UES");
                }

                // ask user if they want to dump gnames
                Console.WriteLine("Dump GNames? (y/n)");
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Y)
                {
                    try
                    {
                        //var outputDir = Path.Combine(AppContext.BaseDirectory, "GNames");
                        var outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "UES", "GNames");
                        unrealEngine.DumpGNames(outputDir);
                        Console.WriteLine($"✅ GNames dumped successfully to: {outputDir}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ GNames dump failed: {ex.Message}");
                    }
                }


                // ask the user if they want to dump the sdk
                Console.WriteLine("Dump SDK? (y/n)");
                key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Y)
                {
                    try
                    {
                        //var outputDir = Path.Combine(AppContext.BaseDirectory, "SDK");
                        var outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "UES", "SDK");
                        unrealEngine.GenerateSDK(outputDir);
                        Console.WriteLine($"✅ SDK dumped successfully to: {outputDir}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ SDK dump failed: {ex.Message}");
                    }
                }

                // Keep the thread alive
                while (true)
                {
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Exception in main thread: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        [DllImport("kernel32.dll")]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern nint CreateThread(
            nint lpThreadAttributes,
            nuint dwStackSize,
            nint lpStartAddress,
            nint lpParameter,
            uint dwCreationFlags,
            out uint lpThreadId);
    }
}
