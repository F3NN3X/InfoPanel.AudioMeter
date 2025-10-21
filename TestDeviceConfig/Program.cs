using System;
using InfoPanel.AudioMeter;

class Program
{
    static void Main()
    {
        Console.WriteLine("Testing AudioMeter Plugin Device Configuration...");
        
        try
        {
            var plugin = new AudioLevelMeterPlugin.AudioLevelMeter();
            plugin.Initialize();
            
            Console.WriteLine("Plugin initialized successfully!");
            Console.WriteLine("Check the generated INI file for device customization.");
            
            plugin.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}