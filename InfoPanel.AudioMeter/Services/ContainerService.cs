using System;
using System.Collections.Generic;
using System.Linq;
using AudioLevelMeterPlugin.Models;
using InfoPanel.Plugins;

namespace AudioLevelMeterPlugin.Services
{
    /// <summary>
    /// Service for managing InfoPanel plugin containers and sensors
    /// </summary>
    public class ContainerService
    {
        private readonly List<PluginContainer> _containers = new();
        private readonly AudioMeterConfiguration _configuration;

        public ContainerService(AudioMeterConfiguration? configuration = null)
        {
            _configuration = configuration ?? new AudioMeterConfiguration();
        }

        /// <summary>
        /// Create the default audio container
        /// </summary>
        public PluginContainer CreateDefaultContainer()
        {
            var container = new PluginContainer("default-audio", "Audio Level Meter");
            AddAudioSensors(container, "Initializing...");
            _containers.Add(container);
            return container;
        }

        /// <summary>
        /// Create a container for a specific audio device
        /// </summary>
        public PluginContainer CreateDeviceContainer(AudioDeviceInfo device)
        {
            var container = new PluginContainer(device.ContainerId, $"🎵 {device.DisplayName}");
            AddAudioSensors(container, device.DisplayName);
            _containers.Add(container);
            return container;
        }

        /// <summary>
        /// Create a fallback container for devices that failed to initialize properly
        /// </summary>
        public PluginContainer CreateFallbackContainer(int deviceIndex)
        {
            string fallbackId = $"audio-device-{deviceIndex}";
            string fallbackName = $"Audio Device {deviceIndex + 1}";
            
            var container = new PluginContainer(fallbackId, $"🎵 {fallbackName}");
            AddAudioSensors(container, fallbackName);
            _containers.Add(container);
            return container;
        }

        /// <summary>
        /// Create an error container when initialization fails
        /// </summary>
        public PluginContainer CreateErrorContainer()
        {
            var container = new PluginContainer("audio-error", "Audio (Error)");
            AddAudioSensors(container, "Init Error");
            _containers.Add(container);
            return container;
        }

        /// <summary>
        /// Update all sensor values in a container using advanced audio levels
        /// </summary>
        public void UpdateContainerSensors(PluginContainer container, AudioLevels levels)
        {
            try
            {
                // Debug logging
                if (levels.PercentageLevel > 0f || levels.PeakLevel > -90f)
                {
                    Console.WriteLine($"ContainerService: Updating {container.Id} - Peak: {levels.PeakLevel:F1}dB, RMS: {levels.MagnitudeLevel:F1}dB, Percentage: {levels.PercentageLevel:F1}%, Left: {levels.LeftChannel:F1}%, Right: {levels.RightChannel:F1}%");
                }
                
                // Update professional metrics
                var peakSensor = container.Entries.OfType<PluginSensor>().FirstOrDefault(e => e.Id == "peak-level");
                if (peakSensor != null) peakSensor.Value = (float)Math.Round(levels.PeakLevel, 1);
                
                var rmsSensor = container.Entries.OfType<PluginSensor>().FirstOrDefault(e => e.Id == "rms-level");
                if (rmsSensor != null) rmsSensor.Value = (float)Math.Round(levels.MagnitudeLevel, 1);
                
                var peakHoldSensor = container.Entries.OfType<PluginSensor>().FirstOrDefault(e => e.Id == "peak-hold");
                if (peakHoldSensor != null) peakHoldSensor.Value = (float)Math.Round(levels.PeakHold, 1);
                
                // Update per-channel levels
                var leftSensor = container.Entries.OfType<PluginSensor>().FirstOrDefault(e => e.Id == "left-channel");
                if (leftSensor != null) leftSensor.Value = (float)Math.Round(levels.LeftChannel, 1);
                
                var rightSensor = container.Entries.OfType<PluginSensor>().FirstOrDefault(e => e.Id == "right-channel");
                if (rightSensor != null) rightSensor.Value = (float)Math.Round(levels.RightChannel, 1);
                
                // Update backward compatibility sensor
                var audioLevelSensor = container.Entries.OfType<PluginSensor>().FirstOrDefault(e => e.Id == "audio-level");
                if (audioLevelSensor != null) audioLevelSensor.Value = (float)Math.Round(levels.PercentageLevel, 1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ContainerService: Error updating container sensors: {ex.Message}");
            }
        }

        /// <summary>
        /// Set the spectrum bands string of a container
        /// </summary>
        public void UpdateContainerSpectrum(PluginContainer container, string spectrum)
        {
            try
            {
                var spectrumEntry = container.Entries.OfType<PluginText>().FirstOrDefault(e => e.Id == "spectrum");
                if (spectrumEntry != null && spectrumEntry.Value != spectrum)
                {
                    spectrumEntry.Value = spectrum;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ContainerService: Error updating container spectrum: {ex.Message}");
            }
        }

        /// <summary>
        /// Update the device name in a container
        /// </summary>
        public void UpdateContainerDeviceName(PluginContainer container, string deviceName)
        {
            try
            {
                var deviceNameEntry = container.Entries.OfType<PluginText>().FirstOrDefault(e => e.Id == "device-name");
                if (deviceNameEntry != null)
                {
                    string oldName = deviceNameEntry.Value?.ToString() ?? "";
                    deviceNameEntry.Value = deviceName;
                    if (oldName != deviceName)
                    {
                        Console.WriteLine($"ContainerService: Updated device name in container '{container.Id}' - '{oldName}' → '{deviceName}'");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ContainerService: Error updating container device name: {ex.Message}");
            }
        }

        /// <summary>
        /// Update container names with custom device names
        /// </summary>
        public void UpdateContainerNamesWithCustomNames(List<AudioDeviceInfo> devices)
        {
            try
            {
                foreach (var device in devices)
                {
                    var container = _containers.FirstOrDefault(c => c.Id == device.ContainerId);
                    if (container != null)
                    {
                        UpdateContainerDeviceName(container, device.DisplayName);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ContainerService: Error updating container names: {ex.Message}");
            }
        }

        /// <summary>
        /// Find a container by ID
        /// </summary>
        public PluginContainer? FindContainer(string containerId)
        {
            return _containers.FirstOrDefault(c => c.Id == containerId);
        }

        /// <summary>
        /// Get all containers
        /// </summary>
        public List<PluginContainer> GetAllContainers()
        {
            return _containers;
        }

        /// <summary>
        /// Clear all containers
        /// </summary>
        public void ClearContainers()
        {
            _containers.Clear();
        }

        /// <summary>
        /// Add standard audio sensors to a container
        /// </summary>
        private void AddAudioSensors(PluginContainer container, string deviceName)
        {
            // Device information
            container.Entries.Add(new PluginText("device-name", "Device", deviceName));
            
            // Advanced audio metrics (professional)
            container.Entries.Add(new PluginSensor("peak-level", "Peak", _configuration.AudioFloorDb, "dB"));
            container.Entries.Add(new PluginSensor("rms-level", "RMS", _configuration.AudioFloorDb, "dB"));
            container.Entries.Add(new PluginSensor("peak-hold", "Peak Hold", _configuration.AudioFloorDb, "dB"));
            
            // Per-channel levels
            container.Entries.Add(new PluginSensor("left-channel", "Left", 0f, "%"));
            container.Entries.Add(new PluginSensor("right-channel", "Right", 0f, "%"));
            
            // Backward compatibility
            container.Entries.Add(new PluginSensor("audio-level", "Level", 0f, "%"));

            // Spectrum bands as comma-separated 0..100 integers, lowest band first (empty when unavailable)
            container.Entries.Add(new PluginText("spectrum", "Spectrum", ""));
        }
    }
}