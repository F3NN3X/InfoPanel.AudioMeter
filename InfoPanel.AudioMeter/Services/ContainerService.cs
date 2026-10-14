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
        private readonly Dictionary<PluginContainer, ContainerEntries> _entries = new();
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
                if (!_entries.TryGetValue(container, out var entries))
                    return;

                // Update professional metrics
                if (entries.Peak != null) entries.Peak.Value = (float)Math.Round(levels.PeakLevel, 1);
                if (entries.Rms != null) entries.Rms.Value = (float)Math.Round(levels.MagnitudeLevel, 1);
                if (entries.PeakHold != null) entries.PeakHold.Value = (float)Math.Round(levels.PeakHold, 1);
                
                // Update per-channel levels
                if (entries.Left != null) entries.Left.Value = (float)Math.Round(levels.LeftChannel, 1);
                if (entries.Right != null) entries.Right.Value = (float)Math.Round(levels.RightChannel, 1);
                
                // Update backward compatibility sensor
                if (entries.Level != null) entries.Level.Value = (float)Math.Round(levels.PercentageLevel, 1);
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
                var spectrumEntry = _entries.TryGetValue(container, out var entries) ? entries.Spectrum : null;
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
                var deviceNameEntry = _entries.TryGetValue(container, out var entries) ? entries.DeviceName : null;
                if (deviceNameEntry != null)
                {
                    string oldName = deviceNameEntry.Value?.ToString() ?? "";
                    if (oldName != deviceName)
                    {
                        deviceNameEntry.Value = deviceName;
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
            for (int i = 0; i < _containers.Count; i++)
            {
                if (_containers[i].Id == containerId) return _containers[i];
            }
            return null;
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
            _entries.Clear();
        }

        /// <summary>
        /// Add standard audio sensors to a container
        /// </summary>
        private void AddAudioSensors(PluginContainer container, string deviceName)
        {
            // Device information
            var deviceNameEntry = new PluginText("device-name", "Device", deviceName);
            container.Entries.Add(deviceNameEntry);
            
            // Advanced audio metrics (professional)
            var peakSensor = new PluginSensor("peak-level", "Peak", _configuration.AudioFloorDb, "dB");
            container.Entries.Add(peakSensor);
            var rmsSensor = new PluginSensor("rms-level", "RMS", _configuration.AudioFloorDb, "dB");
            container.Entries.Add(rmsSensor);
            var peakHoldSensor = new PluginSensor("peak-hold", "Peak Hold", _configuration.AudioFloorDb, "dB");
            container.Entries.Add(peakHoldSensor);
            
            // Per-channel levels
            var leftSensor = new PluginSensor("left-channel", "Left", 0f, "%");
            container.Entries.Add(leftSensor);
            var rightSensor = new PluginSensor("right-channel", "Right", 0f, "%");
            container.Entries.Add(rightSensor);
            
            // Backward compatibility
            var levelSensor = new PluginSensor("audio-level", "Level", 0f, "%");
            container.Entries.Add(levelSensor);

            // Spectrum bands as comma-separated 0..100 integers, lowest band first (empty when unavailable)
            var spectrumEntry = new PluginText("spectrum", "Spectrum", "");
            container.Entries.Add(spectrumEntry);

            _entries[container] = new ContainerEntries(deviceNameEntry, peakSensor, rmsSensor, peakHoldSensor, leftSensor, rightSensor, levelSensor, spectrumEntry);
        }

        /// <summary>
        /// Entries of a container, looked up once at creation instead of by id on every update
        /// </summary>
        private sealed record ContainerEntries(
            PluginText DeviceName, PluginSensor Peak, PluginSensor Rms, PluginSensor PeakHold,
            PluginSensor Left, PluginSensor Right, PluginSensor Level, PluginText Spectrum);
    }
}