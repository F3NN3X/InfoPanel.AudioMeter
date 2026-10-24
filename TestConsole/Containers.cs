using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AudioLevelMeterPlugin;
using InfoPanel.Plugins;

/// <summary>
/// `--containers [--inputs]`: loads the plugin, runs it for ~3 s and prints every container with its data-flow,
/// device name and the highest peak seen without any capture stream open. `--inputs` writes IncludeInputs=true
/// to the plugin's ini next to the plugin dll (the test output folder, not InfoPanel's) before loading.
/// </summary>
static class Containers
{
    public static int Run(string[] args)
    {
        string iniPath = $"{typeof(AudioLevelMeter).Assembly.Location}.ini";
        if (args.Contains("--inputs"))
        {
            File.WriteAllText(iniPath, "[Devices]\nIncludeInputs=true\n");
        }
        else if (File.Exists(iniPath))
        {
            File.Delete(iniPath);
        }

        var plugin = new AudioLevelMeter();
        plugin.Initialize();
        var containers = new List<IPluginContainer>();
        plugin.Load(containers);

        var maxPeak = containers.ToDictionary(c => c.Id, _ => float.MinValue);
        for (int i = 0; i < 60; i++)
        {
            plugin.Update();
            foreach (var c in containers)
            {
                foreach (var e in c.Entries)
                {
                    if (e is PluginSensor s && s.Id == "peak-level") maxPeak[c.Id] = Math.Max(maxPeak[c.Id], s.Value);
                }
            }
            System.Threading.Thread.Sleep(50);
        }

        Console.WriteLine("\n=== containers ===");
        foreach (var c in containers)
        {
            string Text(string id) => c.Entries.OfType<PluginText>().FirstOrDefault(t => t.Id == id)?.Value ?? "(none)";
            Console.WriteLine($"{c.Id} | data-flow={Text("data-flow")} | device={Text("device-name")} | max peak {maxPeak[c.Id]:F1} dB");
        }

        plugin.Close();
        return 0;
    }
}
