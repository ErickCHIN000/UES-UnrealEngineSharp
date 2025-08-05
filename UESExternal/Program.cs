using SDK.Script.Astro2;
using SDK.Script.Engine;
using UES;
using UES.Memory;
using Console = System.Console;

namespace UESExternal
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== UES External Example ===");
            Console.WriteLine("This example demonstrates external memory access to an Unreal Engine process.");
            Console.WriteLine();

            try
            {
                
                UESConfig.ConfigureForExternal("WitchFire-Win64-Shipping");

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
                }
                else
                {
                    Console.WriteLine("❌ Failed to initialize UES");
                    Console.WriteLine("Make sure the target process is running and accessible.");
                    return;
                }

                /*// ask user if they want to dump gnames
                Console.WriteLine("Dump GNames? (y/n)");
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Y)
                {
                    try
                    {
                        var outputDir = Path.Combine(AppContext.BaseDirectory, "GNames");
                        unrealEngine.DumpGNames(outputDir);
                        Console.WriteLine($"✅ GNames dumped successfully to: {outputDir}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ GNames dump failed: {ex.Message}");
                    }
                }

                Console.WriteLine("Dump SDK? (y/n)");
                key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Y)
                {
                    try
                    {
                        var outputDir = Path.Combine(AppContext.BaseDirectory, "SDK");
                        unrealEngine.GenerateSDK(outputDir);
                        Console.WriteLine($"✅ SDK dumped successfully to: {outputDir}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ SDK dump failed: {ex.Message}");
                    }
                }*/

                // Run the main loop
                Console.WriteLine("Running main loop...");
                MainLoop();


                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Exception: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        static bool FovFlag = false;

        static int MainLoop()
        {
            while (true)
            {
                if (UnrealEngine.Instance.GWorld == 0) return 1;

                var world = new World(UnrealEngine.Instance.MemoryAccess!.ReadMemory<nint>(UnrealEngine.Instance.GWorld));
                if (world.Address == 0) return 2;

                var Instance = world.OwningGameInstance;
                if (Instance.Address == 0) return 3;

                var LocalPlayer = Instance.LocalPlayers[0];
                var PlayerController = LocalPlayer.PlayerController.As<DimensionPlayerControllerBase>();
                if (PlayerController.Address == 0) return 4;


                var currentFov = PlayerController.HipfireFOV;

                

                PlayerController.Invoke("UpdateFOV", 20f);



                System.Threading.Thread.Sleep(100);
            }


            return 0;
        }
    }
}
