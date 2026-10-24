using InfoPanel.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AudioLevelMeterPlugin.Models;
using AudioLevelMeterPlugin.Services;

namespace AudioLevelMeterPlugin
{
    public class AudioLevelMeter : BasePlugin
    {
        private readonly object _lockObject = new object();
        private readonly AudioMeterConfiguration _configuration;
        private readonly Dictionary<string, AdvancedAudioCalculator> _audioCalculators = new();
        private readonly Dictionary<string, float> _decayValues = new();
        
        // Services
        private CoreAudioService? _coreAudioService;
        private ConfigurationService? _configurationService;
        private ContainerService? _containerService;
        private LoopbackCaptureService? _loopbackService;
        
        // State
        private List<AudioDeviceInfo> _audioDevices = new();
        private bool _initializationFailed = false;

        public AudioLevelMeter() : base("audio-level-meter", "Audio Level Meter", "Professional audio monitoring with OBS-style level processing")
        {
            _configuration = new AudioMeterConfiguration();
        }

        public override TimeSpan UpdateInterval => _configuration.UpdateInterval;

        public override void Initialize()
        {
            try
            {
                Console.WriteLine("AudioLevelMeter initializing...");
                
                // Initialize services
                _coreAudioService = new CoreAudioService();
                if (!_coreAudioService.Initialize())
                {
                    throw new InvalidOperationException("Failed to initialize Core Audio API");
                }

                // Set up configuration service
                Assembly assembly = Assembly.GetExecutingAssembly();
                string configFilePath = $"{assembly.ManifestModule.FullyQualifiedName}.ini";
                _configurationService = new ConfigurationService(configFilePath, _configuration.Spectrum, _configuration.Meter, _configuration.Devices);
                _configurationService.LoadConfiguration();

                // Initialize container service
                _containerService = new ContainerService(_configuration);

                // Create default containers
                var defaultContainer = _containerService.CreateDefaultContainer();
                _decayValues["default-audio"] = 0f;
                _audioCalculators["default-audio"] = new AdvancedAudioCalculator(_configuration);
                if (_configuration.Devices.IncludeInputs)
                {
                    _containerService.CreateDefaultInputContainer();
                    _decayValues["default-input"] = 0f;
                    _audioCalculators["default-input"] = new AdvancedAudioCalculator(_configuration);
                }

                // Enumerate all audio devices
                _audioDevices = _coreAudioService.GetAudioDevices(_configuration.Devices.IncludeInputs);
                Console.WriteLine($"Found {_audioDevices.Count} audio devices");

                // Create containers for each device
                CreateDeviceContainers();

                // Handle device configuration
                _configurationService.HandleDeviceConfiguration(_audioDevices);
                _containerService.UpdateContainerNamesWithCustomNames(_audioDevices);

                // Start spectrum capture (never fatal for the level meters)
                StartSpectrumCapture();

                Console.WriteLine($"AudioLevelMeter initialization complete - created {_containerService.GetAllContainers().Count} containers");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AudioLevelMeter Initialize error: {ex.Message}");
                _initializationFailed = true;

                // Ensure we have at least one container
                _containerService ??= new ContainerService(_configuration);
                if (_containerService.GetAllContainers().Count == 0)
                {
                    var errorContainer = _containerService.CreateErrorContainer();
                    _decayValues["audio-error"] = 0f;
                    _audioCalculators["audio-error"] = new AdvancedAudioCalculator(_configuration);
                }
            }
        }

        public override void Load(List<IPluginContainer> containers)
        {
            if (_containerService != null)
            {
                containers.AddRange(_containerService.GetAllContainers().Cast<IPluginContainer>());
            }
        }

        public override void Update()
        {
            if (_initializationFailed || _coreAudioService == null || _containerService == null)
                return;

            lock (_lockObject)
            {
                try
                {
                    // Update default device audio levels
                    UpdateDefaultDeviceLevel("default-audio", _coreAudioService.GetDefaultAudioDevice, _coreAudioService.GetDefaultAudioMeter);
                    if (_configuration.Devices.IncludeInputs)
                        UpdateDefaultDeviceLevel("default-input", _coreAudioService.GetDefaultInputDevice, _coreAudioService.GetDefaultInputMeter);

                    // Update all device audio levels
                    UpdateAllDeviceLevels();

                    // Publish the latest spectrum bands (computed on the capture thread)
                    UpdateSpectra();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Update error: {ex.Message}");
                }
            }
        }

        public override Task UpdateAsync(CancellationToken cancellationToken = default)
        {
            Update();
            return Task.CompletedTask;
        }

        public override void Close()
        {
            try
            {
                Console.WriteLine("AudioLevelMeter closing...");
                
                // Wait for an in-flight Update so no COM object is used after it is released
                lock (_lockObject)
                {
                    _loopbackService?.Dispose();
                    _loopbackService = null;
                    _coreAudioService?.Dispose();
                    _coreAudioService = null;
                    _decayValues.Clear();
                    _audioCalculators.Clear();
                    _containerService?.ClearContainers();
                }
                
                Console.WriteLine("AudioLevelMeter cleanup complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        public override string ConfigFilePath => "";

        /// <summary>
        /// Create containers for all discovered devices
        /// </summary>
        private void CreateDeviceContainers()
        {
            if (_containerService == null) return;

            for (int i = 0; i < _audioDevices.Count; i++)
            {
                try
                {
                    var device = _audioDevices[i];
                    Console.WriteLine($"Processing device {i}: {device.FriendlyName} (ID: {device.DeviceId})");

                    var deviceContainer = _containerService.CreateDeviceContainer(device);
                    _decayValues[device.ContainerId] = 0f;
                    _audioCalculators[device.ContainerId] = new AdvancedAudioCalculator(_configuration);

                    Console.WriteLine($"✅ Pre-created container for: {device.FriendlyName}");
                }
                catch (Exception deviceEx)
                {
                    Console.WriteLine($"⚠️ Failed to process device {i}: {deviceEx.Message}");

                    // Create fallback container
                    try
                    {
                        var fallbackContainer = _containerService.CreateFallbackContainer(i);
                        string fallbackId = fallbackContainer.Id;
                        _decayValues[fallbackId] = 0f;
                        _audioCalculators[fallbackId] = new AdvancedAudioCalculator(_configuration);

                        Console.WriteLine($"✅ Created fallback container: Audio Device {i + 1}");
                    }
                    catch (Exception fallbackEx)
                    {
                        Console.WriteLine($"⚠️ Failed to create fallback container {i}: {fallbackEx.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Start capture for every device container and the default devices: loopback for outputs and, when
        /// inputs are on, a shared-mode capture stream for inputs (an input meter only reads while one is open)
        /// </summary>
        private void StartSpectrumCapture()
        {
            try
            {
                if (!_configuration.Spectrum.Enabled)
                {
                    if (_configuration.Devices.IncludeInputs) Console.WriteLine("Input meters need [Spectrum] Enabled=true: the capture stream that feeds them is the spectrum capture");
                    return;
                }

                _loopbackService = new LoopbackCaptureService(_configuration.Spectrum);
                _loopbackService.AddTarget("default-audio", null);
                if (_configuration.Devices.IncludeInputs) _loopbackService.AddTarget("default-input", null, isInput: true);
                foreach (var device in _audioDevices)
                {
                    _loopbackService.AddTarget(device.ContainerId, device.DeviceId, device.IsInput);
                }
                _loopbackService.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Spectrum capture start error: {ex.Message}");
            }
        }

        /// <summary>
        /// Copy the latest spectrum string into every container's spectrum entry
        /// </summary>
        private void UpdateSpectra()
        {
            if (_containerService == null) return;

            foreach (var container in _containerService.GetAllContainers())
            {
                string spectrum = _loopbackService?.GetSpectrum(container.Id) ?? string.Empty;
                _containerService.UpdateContainerSpectrum(container, spectrum);
            }
        }

        /// <summary>
        /// Update audio levels for a default device container (default-audio or default-input)
        /// </summary>
        private void UpdateDefaultDeviceLevel(string containerId, Func<AudioDeviceInfo?> getDevice, Func<IAudioMeterInformation?> getMeter)
        {
            if (_coreAudioService == null || _containerService == null) return;

            try
            {
                var defaultDevice = getDevice();
                if (defaultDevice == null) return;

                var container = _containerService.FindContainer(containerId);
                if (container == null) return;

                // Update device name
                _containerService.UpdateContainerDeviceName(container, defaultDevice.DisplayName);

                // Get audio levels (the meter is cached and owned by the service)
                var audioMeter = getMeter();
                if (audioMeter != null)
                {
                    var channelPeaks = _coreAudioService.GetChannelPeakValues(audioMeter);
                    var calculator = _audioCalculators[containerId];
                    var levels = calculator.CalculateAdvancedLevels(channelPeaks);
                    
                    _containerService.UpdateContainerSensors(container, levels);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Default device update error: {ex.Message}");
            }
        }

        /// <summary>
        /// Update audio levels for all devices
        /// </summary>
        private void UpdateAllDeviceLevels()
        {
            if (_coreAudioService == null || _containerService == null) return;

            try
            {
                var audioMeters = _coreAudioService.GetAllAudioMeters();

                foreach (var kvp in audioMeters)
                {
                    try
                    {
                        string containerId = kvp.Key;
                        if (containerId == "default-audio" || containerId == "default-input") continue; // Already handled above

                        var audioMeter = kvp.Value;
                        var container = _containerService.FindContainer(containerId);
                        if (container == null) continue;

                        // Get channel peak values and calculate advanced levels
                        var channelPeaks = _coreAudioService.GetChannelPeakValues(audioMeter);
                        var calculator = _audioCalculators[containerId];
                        var levels = calculator.CalculateAdvancedLevels(channelPeaks);

                        // Update all sensors
                        _containerService.UpdateContainerSensors(container, levels);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Device audio level update error: {ex.Message}");
                    }
                }

                // Initialize audio meters for devices that don't have them yet
                foreach (var device in _audioDevices)
                {
                    _coreAudioService.GetAudioMeter(device.ContainerId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"All devices update error: {ex.Message}");
            }
        }

        /// <summary>
        /// Scales raw audio peak value (0.0-1.0) to a more responsive VU meter percentage (0-100)
        /// Legacy method kept for reference - now handled in AdvancedAudioCalculator
        /// </summary>
        [Obsolete("Use AdvancedAudioCalculator.ScaleToPercentage instead")]
        private float ScaleAudioLevel(float peakValue)
        {
            if (peakValue <= 0f) return 0f;
            
            // Ultra-responsive scaling designed to make loud music hit 95-100% consistently
            float scaledValue;
            
            if (peakValue <= 0.01f)
            {
                scaledValue = peakValue * 300f;
            }
            else if (peakValue <= 0.05f)
            {
                scaledValue = 3f + (peakValue - 0.01f) * 450f;
            }
            else if (peakValue <= 0.15f)
            {
                scaledValue = 21f + (peakValue - 0.05f) * 390f;
            }
            else if (peakValue <= 0.25f)
            {
                scaledValue = 60f + (peakValue - 0.15f) * 300f;
            }
            else if (peakValue <= 0.4f)
            {
                scaledValue = 90f + (peakValue - 0.25f) * 60f;
            }
            else
            {
                scaledValue = 99f + (peakValue - 0.4f) * 1.67f;
            }
            
            return Math.Min(scaledValue, 100f);
        }
    }
}