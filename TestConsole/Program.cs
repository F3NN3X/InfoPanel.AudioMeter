using System;
using System.Collections.Generic;
using AudioLevelMeterPlugin;

class Program
{
    static void Main()
    {
        Console.WriteLine("Testing Audio Level Meter Plugin with Windows Core Audio API...");
        
        try
        {
            var plugin = new AudioLevelMeter();
            Console.WriteLine($"Plugin created: {plugin.Name}");
            Console.WriteLine($"Update interval: {plugin.UpdateInterval}");
            
            // Test initialization
            Console.WriteLine("Initializing plugin...");
            plugin.Initialize();
            
            // Test loading containers
            Console.WriteLine("Loading containers...");
            var containers = new List<InfoPanel.Plugins.IPluginContainer>();
            plugin.Load(containers);
            
            Console.WriteLine($"Loaded {containers.Count} containers:");
            foreach (var container in containers)
            {
                Console.WriteLine($"  - {container.Id}: {container.Name}");
                Console.WriteLine($"    Entries: {container.Entries.Count}");
            }
            
            // Test a few updates to see if Windows Core Audio API works
            Console.WriteLine("Testing Windows Core Audio API updates...");
            for (int i = 0; i < 10; i++)
            {
                plugin.UpdateAsync(System.Threading.CancellationToken.None).Wait();
                System.Threading.Thread.Sleep(200);
                Console.WriteLine($"Update {i + 1} completed");
                
                // Display current audio level
                foreach (var container in containers)
                {
                    foreach (var entry in container.Entries)
                    {
                        if (entry is InfoPanel.Plugins.PluginSensor sensor && sensor.Id == "audio-level")
                        {
                            Console.WriteLine($"  Audio level: {sensor.Value:F1}%");
                        }
                        if (entry is InfoPanel.Plugins.PluginText text && text.Id == "device-name")
                        {
                            Console.WriteLine($"  Device: {text.Value}");
                        }
                    }
                }
            }
            
            // Cleanup
            plugin.Close();
            Console.WriteLine("Plugin test completed successfully!");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Plugin test failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}