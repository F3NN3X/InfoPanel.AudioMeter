using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AudioLevelMeterPlugin.Models;
using IniParser;
using IniParser.Model;

namespace AudioLevelMeterPlugin.Services
{
    /// <summary>
    /// Service for managing device name configuration through INI files
    /// </summary>
    public class ConfigurationService
    {
        private readonly string _configFilePath;
        private readonly Dictionary<string, string> _customDeviceNames = new();
        private readonly SpectrumSettings _spectrum;

        public ConfigurationService(string configFilePath, SpectrumSettings? spectrum = null)
        {
            _configFilePath = configFilePath;
            _spectrum = spectrum ?? new SpectrumSettings();
        }

        /// <summary>
        /// Load existing configuration if the INI file exists
        /// </summary>
        public bool LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    Console.WriteLine($"ConfigurationService: Loading existing configuration from {_configFilePath}");
                    var parser = new FileIniDataParser();
                    var config = parser.ReadFile(_configFilePath);
                    LoadConfigurationFromIni(config);
                    return true;
                }
                else
                {
                    Console.WriteLine($"ConfigurationService: No existing configuration found at {_configFilePath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ConfigurationService: Error loading existing configuration: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Create a default INI file with discovered audio devices
        /// </summary>
        public void CreateDefaultConfiguration(List<AudioDeviceInfo> devices)
        {
            try
            {
                var parser = new FileIniDataParser();
                var config = new IniData();
                config.Sections.AddSection("Spectrum");
                config["Spectrum"]["Enabled"] = _spectrum.Enabled.ToString().ToLowerInvariant();
                config["Spectrum"]["Bands"] = _spectrum.Bands.ToString(CultureInfo.InvariantCulture);
                config["Spectrum"]["MinHz"] = _spectrum.MinHz.ToString(CultureInfo.InvariantCulture);
                config["Spectrum"]["MaxHz"] = _spectrum.MaxHz.ToString(CultureInfo.InvariantCulture);
                config["Spectrum"]["FloorDb"] = _spectrum.FloorDb.ToString(CultureInfo.InvariantCulture);
                config.Sections.AddSection("DeviceNames");

                // Add discovered devices with their default names
                foreach (var device in devices)
                {
                    try
                    {
                        config["DeviceNames"][device.DeviceId] = device.FriendlyName;
                        Console.WriteLine($"ConfigurationService: Added device to INI - {device.DeviceId} = '{device.FriendlyName}'");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ConfigurationService: Error adding device to INI: {ex.Message}");
                    }
                }

                parser.WriteFile(_configFilePath, config);
                Console.WriteLine($"ConfigurationService: Created default INI file at {_configFilePath}");
                
                // Write a header comment manually
                string iniContent = File.ReadAllText(_configFilePath);
                string headerComment = "; Audio Device Custom Names Configuration\n" +
                                     "; Edit the device names below to customize how they appear in InfoPanel\n" +
                                     "; Format: DeviceID = Custom Display Name\n\n";
                File.WriteAllText(_configFilePath, headerComment + iniContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ConfigurationService: Error creating default INI file: {ex.Message}");
            }
        }

        /// <summary>
        /// Update the INI file with any newly discovered devices
        /// </summary>
        public void UpdateConfigurationWithNewDevices(List<AudioDeviceInfo> devices)
        {
            if (!File.Exists(_configFilePath))
                return;

            try
            {
                var parser = new FileIniDataParser();
                var config = parser.ReadFile(_configFilePath);

                bool hasNewDevices = false;

                // Add any new devices that aren't in the INI yet
                foreach (var device in devices)
                {
                    try
                    {
                        if (!config["DeviceNames"].ContainsKey(device.DeviceId))
                        {
                            config["DeviceNames"][device.DeviceId] = device.FriendlyName;
                            hasNewDevices = true;
                            Console.WriteLine($"ConfigurationService: Added new device to INI - {device.DeviceId} = '{device.FriendlyName}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ConfigurationService: Error checking device for INI update: {ex.Message}");
                    }
                }

                // Save the updated INI file if we added new devices
                if (hasNewDevices)
                {
                    parser.WriteFile(_configFilePath, config);
                    Console.WriteLine("ConfigurationService: INI file updated with new devices");
                    
                    // Reload configuration to pick up changes
                    LoadConfigurationFromIni(config);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ConfigurationService: Error updating INI file: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the custom display name for a device
        /// </summary>
        public string GetCustomDeviceName(AudioDeviceInfo device)
        {
            if (_customDeviceNames.TryGetValue(device.DeviceId, out string? customName) && !string.IsNullOrWhiteSpace(customName))
            {
                return customName;
            }
            return device.FriendlyName;
        }

        /// <summary>
        /// Apply custom names to a list of device info objects
        /// </summary>
        public void ApplyCustomNames(List<AudioDeviceInfo> devices)
        {
            foreach (var device in devices)
            {
                if (_customDeviceNames.TryGetValue(device.DeviceId, out string? customName) && !string.IsNullOrWhiteSpace(customName))
                {
                    device.CustomName = customName;
                }
            }
        }

        /// <summary>
        /// Get all custom device names
        /// </summary>
        public Dictionary<string, string> GetAllCustomDeviceNames()
        {
            return _customDeviceNames.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// Handle complete device configuration workflow
        /// </summary>
        public void HandleDeviceConfiguration(List<AudioDeviceInfo> devices)
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    Console.WriteLine($"ConfigurationService: INI file not found at {_configFilePath}, creating default with {devices.Count} devices.");
                    CreateDefaultConfiguration(devices);
                }
                else
                {
                    Console.WriteLine($"ConfigurationService: Reading INI file from {_configFilePath}");
                    LoadConfiguration();
                    
                    // Update INI with any newly discovered devices
                    UpdateConfigurationWithNewDevices(devices);
                }
                
                // Apply custom names to devices
                ApplyCustomNames(devices);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ConfigurationService: Error handling device configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Load the [Spectrum] section; missing or invalid keys keep their defaults
        /// </summary>
        private void LoadSpectrumFromIni(IniData config)
        {
            var defaults = new SpectrumSettings();
            _spectrum.Enabled = defaults.Enabled;
            _spectrum.Bands = defaults.Bands;
            _spectrum.MinHz = defaults.MinHz;
            _spectrum.MaxHz = defaults.MaxHz;
            _spectrum.FloorDb = defaults.FloorDb;

            if (!config.Sections.ContainsSection("Spectrum")) return;
            var section = config["Spectrum"];

            if (bool.TryParse(section["Enabled"], out bool enabled)) _spectrum.Enabled = enabled;
            if (int.TryParse(section["Bands"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bands))
                _spectrum.Bands = Math.Clamp(bands, 8, 64);
            if (TryParseFloat(section["MinHz"], out float minHz) && minHz > 0f) _spectrum.MinHz = minHz;
            if (TryParseFloat(section["MaxHz"], out float maxHz) && maxHz > 0f) _spectrum.MaxHz = maxHz;
            if (TryParseFloat(section["FloorDb"], out float floorDb) && floorDb < 0f) _spectrum.FloorDb = floorDb;

            if (_spectrum.MinHz >= _spectrum.MaxHz)
            {
                _spectrum.MinHz = defaults.MinHz;
                _spectrum.MaxHz = defaults.MaxHz;
            }

            Console.WriteLine($"ConfigurationService: Spectrum enabled={_spectrum.Enabled} bands={_spectrum.Bands} {_spectrum.MinHz}-{_spectrum.MaxHz}Hz floor={_spectrum.FloorDb}dB");
        }

        private static bool TryParseFloat(string? value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        /// <summary>
        /// Load device name configuration from INI data
        /// </summary>
        private void LoadConfigurationFromIni(IniData config)
        {
            _customDeviceNames.Clear();
            LoadSpectrumFromIni(config);

            if (config.Sections.ContainsSection("DeviceNames"))
            {
                foreach (var keyData in config["DeviceNames"])
                {
                    if (!string.IsNullOrWhiteSpace(keyData.Value))
                    {
                        _customDeviceNames[keyData.KeyName] = keyData.Value;
                        Console.WriteLine($"ConfigurationService: Custom name loaded - {keyData.KeyName} = '{keyData.Value}'");
                    }
                }
            }
        }
    }
}