using System;
using System.Collections.Generic;
using AudioLevelMeterPlugin;

class Program
{
    static void Main()
    {
        Console.WriteLine("Testing Audio Level Meter Plugin...");
        
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
            
            // Test a few updates
            Console.WriteLine("Testing updates...");
            for (int i = 0; i < 3; i++)
            {
                plugin.UpdateAsync(System.Threading.CancellationToken.None).Wait();
                System.Threading.Thread.Sleep(100);
                Console.WriteLine($"Update {i + 1} completed");
            }
            
            // Cleanup
            plugin.Close();
            Console.WriteLine("Plugin test completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Plugin test failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}