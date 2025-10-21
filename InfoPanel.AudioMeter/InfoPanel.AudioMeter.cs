using InfoPanel.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using IniParser;
using IniParser.Model;

namespace AudioLevelMeterPlugin
{
    // Windows Core Audio API P/Invoke declarations
    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(DataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
        int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice ppEndpoint);
        int GetDevice(string pwstrId, out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);
        int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        int GetCount(out uint pcDevices);
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
        int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
        int GetId(out IntPtr ppstrId);
        int GetState(out uint pdwState);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
        int Commit();
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioMeterInformation
    {
        int GetPeakValue(out float pfPeak);
        int GetMeteringChannelCount(out uint pnChannelCount);
        int GetChannelsPeakValues(uint u32ChannelCount, out IntPtr afPeakValues);
        int QueryHardwareSupport(out uint pdwHardwareSupportMask);
    }

    [ComImport]
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMNotificationClient
    {
        int OnDeviceStateChanged(string pwstrDeviceId, uint dwNewState);
        int OnDeviceAdded(string pwstrDeviceId);
        int OnDeviceRemoved(string pwstrDeviceId);
        int OnDefaultDeviceChanged(DataFlow flow, Role role, string pwstrDefaultDeviceId);
        int OnPropertyValueChanged(string pwstrDeviceId, PROPERTYKEY key);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)]
        public ushort vt;
        [FieldOffset(8)]
        public IntPtr pwszVal;
    }

    internal enum DataFlow
    {
        Render,
        Capture,
        All
    }

    internal enum Role
    {
        Console,
        Multimedia,
        Communications
    }

    public class AudioLevelMeter : BasePlugin
    {
        // COM constants
        private const uint CLSCTX_INPROC_SERVER = 0x1;
        private const int S_OK = 0;

        [DllImport("ole32.dll")]
        private static extern void PropVariantClear(ref PROPVARIANT pvar);

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

        private readonly object _lockObject = new object();
        private IMMDeviceEnumerator? _deviceEnumerator;
        private readonly Dictionary<string, IMMDevice> _audioDevices = new();
        private readonly Dictionary<string, IAudioMeterInformation> _audioMeters = new();
        private readonly Dictionary<string, float> _decayValues = new();
        private readonly List<PluginContainer> _containers = new();
        
        // Configuration management
        private string _configFilePath = string.Empty;
        private readonly Dictionary<string, string> _customDeviceNames = new();
        private bool _initializationFailed = false;

        public AudioLevelMeter() : base("audio-level-meter", "Audio Level Meter", "Real-time system audio output level monitoring for all devices")
        {
        }

        public override TimeSpan UpdateInterval => TimeSpan.FromMilliseconds(50);

        #region Configuration Management

        /// <summary>
        /// Creates a default INI file with discovered audio devices
        /// </summary>
        private void CreateDefaultIniFile(FileIniDataParser parser)
        {
            var config = new IniData();
            config.Sections.AddSection("DeviceNames");

            // Add discovered devices with their default names
            foreach (var deviceEntry in _audioDevices)
            {
                try
                {
                    // Get the actual device ID (not the container ID)
                    string actualDeviceId = GetDeviceId(deviceEntry.Value);
                    string defaultName = GetDeviceFriendlyName(deviceEntry.Value);
                    config["DeviceNames"][actualDeviceId] = defaultName;
                    Console.WriteLine($"AudioLevelMeter: Added device to INI - {actualDeviceId} = '{defaultName}'");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"AudioLevelMeter: Error adding device to INI: {ex.Message}");
                }
            }

            try
            {
                parser.WriteFile(_configFilePath, config);
                Console.WriteLine($"AudioLevelMeter: Created default INI file at {_configFilePath}");
                
                // Write a header comment manually
                string iniContent = File.ReadAllText(_configFilePath);
                string headerComment = "; Audio Device Custom Names Configuration\n" +
                                     "; Edit the device names below to customize how they appear in InfoPanel\n" +
                                     "; Format: DeviceID = Custom Display Name\n\n";
                File.WriteAllText(_configFilePath, headerComment + iniContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AudioLevelMeter: Error creating default INI file: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads device name configuration from INI file
        /// </summary>
        private void LoadConfigurationFromIni(IniData config)
        {
            _customDeviceNames.Clear();

            if (config.Sections.ContainsSection("DeviceNames"))
            {
                foreach (var keyData in config["DeviceNames"])
                {
                    if (!string.IsNullOrWhiteSpace(keyData.Value))
                    {
                        _customDeviceNames[keyData.KeyName] = keyData.Value;
                        Console.WriteLine($"AudioLevelMeter: Custom name loaded - {keyData.KeyName} = '{keyData.Value}'");
                    }
                }
            }
        }

        /// <summary>
        /// Updates the INI file with any newly discovered devices
        /// </summary>
        private void UpdateIniWithNewDevices()
        {
            if (!File.Exists(_configFilePath))
                return;

            try
            {
                var parser = new FileIniDataParser();
                var config = parser.ReadFile(_configFilePath);

                bool hasNewDevices = false;

                // Add any new devices that aren't in the INI yet
                foreach (var deviceEntry in _audioDevices)
                {
                    try
                    {
                        // Get the actual device ID (not the container ID)
                        string actualDeviceId = GetDeviceId(deviceEntry.Value);
                        if (!config["DeviceNames"].ContainsKey(actualDeviceId))
                        {
                            string defaultName = GetDeviceFriendlyName(deviceEntry.Value);
                            config["DeviceNames"][actualDeviceId] = defaultName;
                            hasNewDevices = true;
                            Console.WriteLine($"AudioLevelMeter: Added new device to INI - {actualDeviceId} = '{defaultName}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"AudioLevelMeter: Error checking device for INI update: {ex.Message}");
                    }
                }

                // Save the updated INI file if we added new devices
                if (hasNewDevices)
                {
                    parser.WriteFile(_configFilePath, config);
                    Console.WriteLine("AudioLevelMeter: INI file updated with new devices");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AudioLevelMeter: Error updating INI file: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the custom display name for a device, falling back to default name
        /// </summary>
        private string GetCustomDeviceName(string deviceId, IMMDevice device)
        {
            if (_customDeviceNames.TryGetValue(deviceId, out string? customName) && !string.IsNullOrWhiteSpace(customName))
            {
                return customName;
            }
            return GetDeviceFriendlyName(device);
        }

        /// <summary>
        /// Handles device configuration after device enumeration is complete
        /// </summary>
        private void HandleDeviceConfiguration()
        {
            var parser = new FileIniDataParser();
            IniData config;
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    Console.WriteLine($"AudioLevelMeter: INI file not found at {_configFilePath}, creating default with {_audioDevices.Count} devices.");
                    CreateDefaultIniFile(parser);
                }
                else
                {
                    Console.WriteLine($"AudioLevelMeter: Reading INI file from {_configFilePath}");
                    config = parser.ReadFile(_configFilePath);
                    LoadConfigurationFromIni(config);
                    
                    // Update INI with any newly discovered devices
                    UpdateIniWithNewDevices();
                    
                    // Reload configuration in case we added new devices
                    config = parser.ReadFile(_configFilePath);
                    LoadConfigurationFromIni(config);
                    
                    // Update container names with custom names
                    UpdateContainerNamesWithCustomNames();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AudioLevelMeter: Error reading INI file '{_configFilePath}': {ex.Message}");
            }
            
            // If INI file was just created, still try to update containers (though they'll use default names)
            UpdateContainerNamesWithCustomNames();
        }

        /// <summary>
        /// Loads existing configuration if the INI file exists (before device enumeration)
        /// </summary>
        private void LoadExistingConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    Console.WriteLine($"AudioLevelMeter: Loading existing configuration from {_configFilePath}");
                    var parser = new FileIniDataParser();
                    var config = parser.ReadFile(_configFilePath);
                    LoadConfigurationFromIni(config);
                }
                else
                {
                    Console.WriteLine($"AudioLevelMeter: No existing configuration found at {_configFilePath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AudioLevelMeter: Error loading existing configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates already-created container names with custom names from configuration
        /// </summary>
        private void UpdateContainerNamesWithCustomNames()
        {
            try
            {
                foreach (var deviceEntry in _audioDevices)
                {
                    string containerId = deviceEntry.Key;
                    IMMDevice device = deviceEntry.Value;
                    
                    // Get the actual device ID and check for custom name
                    string actualDeviceId = GetDeviceId(device);
                    string customName = GetCustomDeviceName(actualDeviceId, device);
                    
                    // Find the container for this device
                    var container = _containers.FirstOrDefault(c => c.Id == containerId);
                    if (container != null)
                    {
                        // Update the device name entry within the container
                        var deviceNameEntry = container.Entries.OfType<PluginText>().FirstOrDefault(e => e.Id == "device-name");
                        if (deviceNameEntry != null)
                        {
                            string oldName = deviceNameEntry.Value?.ToString() ?? "";
                            deviceNameEntry.Value = customName;
                            Console.WriteLine($"AudioLevelMeter: Updated device name in container '{containerId}' - '{oldName}' → '{customName}'");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AudioLevelMeter: Error updating container names: {ex.Message}");
            }
        }

        #endregion

        public override void Initialize()
        {
            try
            {
                Console.WriteLine("AudioLevelMeter initializing...");
                
                // Set up configuration file path
                Assembly assembly = Assembly.GetExecutingAssembly();
                _configFilePath = $"{assembly.ManifestModule.FullyQualifiedName}.ini";
                
                // Initialize Core Audio API immediately to enumerate all devices
                InitializeCoreAudioAPI();
                
                // Load existing configuration BEFORE creating containers
                LoadExistingConfiguration();
                
                // Create default container
                PluginContainer defaultContainer = new("default-audio", "Audio Level Meter");
                defaultContainer.Entries.Add(new PluginText("device-name", "Device", "Initializing..."));
                defaultContainer.Entries.Add(new PluginSensor("audio-level", "Audio Level", 0f, "%"));
                _containers.Add(defaultContainer);
                _decayValues["default-audio"] = 0f;
                
                // Enumerate all audio devices and create containers for each
                if (_deviceEnumerator != null)
                {
                    try
                    {
                        IMMDeviceCollection deviceCollection;
                        int hr = _deviceEnumerator.EnumAudioEndpoints(DataFlow.Render, 1, out deviceCollection); // 1 = DEVICE_STATE_ACTIVE
                        if (hr == S_OK)
                        {
                            uint deviceCount;
                            deviceCollection.GetCount(out deviceCount);
                            Console.WriteLine($"Found {deviceCount} active audio output devices during initialization");
                            
                            for (uint i = 0; i < deviceCount; i++)
                            {
                                try
                                {
                                    IMMDevice device;
                                    deviceCollection.Item(i, out device);
                                    
                                    string deviceId = GetDeviceId(device);
                                    string defaultDeviceName;
                                    try
                                    {
                                        defaultDeviceName = GetDeviceFriendlyName(device);
                                    }
                                    catch (Exception nameEx)
                                    {
                                        Console.WriteLine($"Failed to get device name for device {i}: {nameEx.Message}");
                                        defaultDeviceName = $"Audio Device {i + 1}";
                                    }
                                    
                                    // Get custom device name from configuration
                                    string displayDeviceName = GetCustomDeviceName(deviceId, device);
                                    
                                    Console.WriteLine($"Processing device {i}: {defaultDeviceName} (ID: {deviceId})");
                                    if (displayDeviceName != defaultDeviceName)
                                    {
                                        Console.WriteLine($"  Using custom name: '{displayDeviceName}'");
                                    }
                                    
                                    // Create container for this device using device ID
                                    string containerId = $"audio-meter-{deviceId.Replace("{", "").Replace("}", "").Replace(".", "-")}";
                                    PluginContainer deviceContainer = new(containerId, $"🎵 {displayDeviceName}");
                                    deviceContainer.Entries.Add(new PluginText("device-name", "Device", displayDeviceName));
                                    deviceContainer.Entries.Add(new PluginSensor("audio-level", "Audio Level", 0f, "%"));
                                    
                                    _containers.Add(deviceContainer);
                                    _decayValues[containerId] = 0f;
                                    
                                    // Cache the device with the container ID
                                    _audioDevices[containerId] = device;
                                    
                                    Console.WriteLine($"✅ Pre-created container for: {displayDeviceName}");
                                }
                                catch (Exception deviceEx)
                                {
                                    Console.WriteLine($"⚠️ Failed to process device {i}: {deviceEx.Message}");
                                    Console.WriteLine($"⚠️ Exception type: {deviceEx.GetType().Name}");
                                    if (deviceEx.InnerException != null)
                                    {
                                        Console.WriteLine($"⚠️ Inner exception: {deviceEx.InnerException.Message}");
                                    }
                                    
                                    // Create fallback container for this device index
                                    try
                                    {
                                        string fallbackId = $"audio-device-{i}";
                                        PluginContainer fallbackContainer = new(fallbackId, $"🎵 Audio Device {i + 1}");
                                        fallbackContainer.Entries.Add(new PluginText("device-name", "Device", $"Audio Device {i + 1}"));
                                        fallbackContainer.Entries.Add(new PluginSensor("audio-level", "Audio Level", 0f, "%"));
                                        
                                        _containers.Add(fallbackContainer);
                                        _decayValues[fallbackId] = 0f;
                                        
                                        Console.WriteLine($"✅ Created fallback container: Audio Device {i + 1}");
                                    }
                                    catch (Exception fallbackEx)
                                    {
                                        Console.WriteLine($"⚠️ Failed to create fallback container {i}: {fallbackEx.Message}");
                                    }
                                }
                            }
                            
                            Marshal.ReleaseComObject(deviceCollection);
                            Console.WriteLine($"Successfully created {_containers.Count - 1} device containers"); // -1 for default
                            
                            // Now that devices are enumerated, handle INI file configuration
                            HandleDeviceConfiguration();
                        }
                        else
                        {
                            Console.WriteLine($"Failed to enumerate devices. HRESULT: 0x{hr:X8}");
                        }
                    }
                    catch (Exception enumEx)
                    {
                        Console.WriteLine($"⚠️ Device enumeration failed: {enumEx.Message}");
                        Console.WriteLine($"Exception type: {enumEx.GetType().Name}");
                    }
                }
                
                Console.WriteLine($"AudioLevelMeter initialization complete - created {_containers.Count} containers");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AudioLevelMeter Initialize error: {ex.Message}");
                _initializationFailed = true;
                
                // Ensure we have at least one container
                if (_containers.Count == 0)
                {
                    PluginContainer errorContainer = new("audio-error", "Audio (Error)");
                    errorContainer.Entries.Add(new PluginText("device-name", "Device", "Init Error"));
                    errorContainer.Entries.Add(new PluginSensor("audio-level", "Audio Level", 0f, "%"));
                    _containers.Add(errorContainer);
                    _decayValues["audio-error"] = 0f;
                }
            }
        }

        private void InitializeCoreAudioAPI()
        {
            try
            {
                var clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"); // CLSID_MMDeviceEnumerator
                var iid = typeof(IMMDeviceEnumerator).GUID;
                IntPtr pUnknown;
                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out pUnknown);
                
                if (hr == S_OK && pUnknown != IntPtr.Zero)
                {
                    _deviceEnumerator = (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(pUnknown);
                    Marshal.Release(pUnknown);
                    Console.WriteLine("Core Audio API enumerator created successfully");
                }
                else
                {
                    Console.WriteLine($"Failed to create Core Audio API enumerator. HRESULT: 0x{hr:X8}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Core Audio API initialization failed: {ex.Message}");
            }
        }

        public override void Load(List<IPluginContainer> containers)
        {
            containers.AddRange(_containers.Cast<IPluginContainer>());
        }

        /// <summary>
        /// Safely gets the device ID from an IMMDevice
        /// </summary>
        private string GetDeviceId(IMMDevice device)
        {
            try
            {
                IntPtr deviceIdPtr;
                int hr = device.GetId(out deviceIdPtr);
                if (hr == S_OK && deviceIdPtr != IntPtr.Zero)
                {
                    string? deviceIdStr = Marshal.PtrToStringUni(deviceIdPtr);
                    Marshal.FreeCoTaskMem(deviceIdPtr);
                    return deviceIdStr ?? "Unknown";
                }
                return "Unknown";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get device ID: {ex.Message}");
                return "Unknown";
            }
        }

        /// <summary>
        /// Scales raw audio peak value (0.0-1.0) to a more responsive VU meter percentage (0-100)
        /// Very aggressive scaling that pushes music at 100% volume to consistently hit 95-100%
        /// </summary>
        private float ScaleAudioLevel(float peakValue)
        {
            if (peakValue <= 0f) return 0f;
            
            // Ultra-responsive scaling designed to make loud music hit 95-100% consistently
            // Most music at full volume peaks around 0.25-0.45, we want this to show 95-100%
            float scaledValue;
            
            if (peakValue <= 0.01f)
            {
                // Very quiet sounds: linear scaling
                scaledValue = peakValue * 300f; // 0.01 -> 3%
            }
            else if (peakValue <= 0.05f)
            {
                // Quiet sounds: gentle acceleration
                scaledValue = 3f + (peakValue - 0.01f) * 450f; // 0.01-0.05 -> 3%-21%
            }
            else if (peakValue <= 0.15f)
            {
                // Moderate sounds: steeper curve
                scaledValue = 21f + (peakValue - 0.05f) * 390f; // 0.05-0.15 -> 21%-60%
            }
            else if (peakValue <= 0.25f)
            {
                // Loud music territory: very aggressive scaling
                scaledValue = 60f + (peakValue - 0.15f) * 300f; // 0.15-0.25 -> 60%-90%
            }
            else if (peakValue <= 0.4f)
            {
                // Peak music content: push to near-maximum
                scaledValue = 90f + (peakValue - 0.25f) * 60f; // 0.25-0.4 -> 90%-99%
            }
            else
            {
                // True peaks: ensure we hit 100%
                scaledValue = 99f + (peakValue - 0.4f) * 1.67f; // 0.4-1.0 -> 99%-100%
            }
            
            return Math.Min(scaledValue, 100f);
        }

        /// <summary>
        /// Gets the friendly name of an audio device (simplified version)
        /// </summary>
        private string GetDeviceFriendlyName(IMMDevice device)
        {
            try
            {
                IPropertyStore propertyStore;
                int hr = device.OpenPropertyStore(0, out propertyStore);
                if (hr != S_OK) return "Unknown Device";

                PROPERTYKEY nameKey = new PROPERTYKEY 
                { 
                    fmtid = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
                    pid = 14 
                };

                PROPVARIANT nameValue;
                hr = propertyStore.GetValue(ref nameKey, out nameValue);
                if (hr == S_OK && nameValue.vt == 31) // VT_LPWSTR
                {
                    string? name = Marshal.PtrToStringUni(nameValue.pwszVal);
                    PropVariantClear(ref nameValue);
                    Marshal.ReleaseComObject(propertyStore);
                    return name ?? "Unknown Device";
                }
                
                Marshal.ReleaseComObject(propertyStore);
                return "Unknown Device";
            }
            catch
            {
                return "Unknown Device";
            }
        }

        public override void Update()
        {
            if (_initializationFailed)
                return;

            lock (_lockObject)
            {
                try
                {
                    // Initialize audio meters if not already done
                    if (_audioMeters.Count == 0 && _deviceEnumerator != null)
                    {
                        InitializeAudioMeters();
                    }

                    // Update default device audio level
                    UpdateDefaultDeviceLevel();

                    // Update all device audio levels
                    UpdateAllDeviceLevels();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Update error: {ex.Message}");
                }
            }
        }

        private void InitializeAudioMeters()
        {
            try
            {
                // Activate audio meters for all cached devices
                foreach (var kvp in _audioDevices)
                {
                    try
                    {
                        string containerId = kvp.Key;
                        IMMDevice device = kvp.Value;
                        
                        // Get audio meter interface for this device
                        Guid audioMeterGuid = typeof(IAudioMeterInformation).GUID;
                        IntPtr audioMeterPtr;
                        int hr = device.Activate(ref audioMeterGuid, 0, IntPtr.Zero, out audioMeterPtr);
                        if (hr == S_OK)
                        {
                            _audioMeters[containerId] = (IAudioMeterInformation)Marshal.GetObjectForIUnknown(audioMeterPtr);
                            Console.WriteLine($"✅ Audio meter activated for device: {containerId}");
                        }
                        else
                        {
                            Console.WriteLine($"⚠️ Failed to activate audio meter for device: {containerId} (HR: 0x{hr:X8})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Failed to activate audio meter for device: {ex.Message}");
                    }
                }
                
                Console.WriteLine($"Successfully initialized {_audioMeters.Count} audio meters");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Audio meter initialization error: {ex.Message}");
            }
        }

        private void UpdateDefaultDeviceLevel()
        {
            try
            {
                // Get the current default device
                if (_deviceEnumerator != null)
                {
                    IMMDevice defaultDevice;
                    int hr = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out defaultDevice);
                    if (hr == S_OK)
                    {
                        string deviceId = GetDeviceId(defaultDevice);
                        string displayDeviceName = GetCustomDeviceName(deviceId, defaultDevice);
                        
                        // Update default container with current default device info
                        var container = _containers.FirstOrDefault(c => c.Id == "default-audio");
                        if (container != null)
                        {
                            var deviceNameEntry = container.Entries.OfType<PluginText>().FirstOrDefault(e => e.Id == "device-name");
                            var audioLevelEntry = container.Entries.OfType<PluginSensor>().FirstOrDefault(e => e.Id == "audio-level");
                            
                            if (deviceNameEntry != null)
                            {
                                deviceNameEntry.Value = displayDeviceName;
                            }
                            
                            // Get audio level for default device
                            if (audioLevelEntry != null)
                            {
                                try
                                {
                                    Guid audioMeterGuid = typeof(IAudioMeterInformation).GUID;
                                    IntPtr audioMeterPtr;
                                    hr = defaultDevice.Activate(ref audioMeterGuid, 0, IntPtr.Zero, out audioMeterPtr);
                                    if (hr == S_OK)
                                    {
                                        var audioMeter = (IAudioMeterInformation)Marshal.GetObjectForIUnknown(audioMeterPtr);
                                        
                                        float peakValue;
                                        audioMeter.GetPeakValue(out peakValue);
                                        
                                        // Scale the audio level for better VU meter response
                                        float scaledLevel = ScaleAudioLevel(peakValue);
                                        
                                        // Apply decay algorithm
                                        float currentValue = _decayValues["default-audio"];
                                        float newValue = Math.Max(scaledLevel, currentValue * 0.85f);
                                        _decayValues["default-audio"] = newValue;
                                        audioLevelEntry.Value = newValue;
                                        
                                        Marshal.ReleaseComObject(audioMeter);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Default device audio level error: {ex.Message}");
                                }
                            }
                        }
                        
                        Marshal.ReleaseComObject(defaultDevice);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Default device update error: {ex.Message}");
            }
        }

        private void UpdateAllDeviceLevels()
        {
            try
            {
                // Update audio levels for all devices with active meters
                foreach (var kvp in _audioMeters)
                {
                    try
                    {
                        string containerId = kvp.Key;
                        if (containerId == "default-audio") continue; // Already handled above
                        
                        IAudioMeterInformation audioMeter = kvp.Value;
                        
                        // Find the container for this device
                        var container = _containers.FirstOrDefault(c => c.Id == containerId);
                        if (container != null)
                        {
                            var audioLevelEntry = container.Entries.OfType<PluginSensor>().FirstOrDefault(e => e.Id == "audio-level");
                            if (audioLevelEntry != null)
                            {
                                float peakValue;
                                int hr = audioMeter.GetPeakValue(out peakValue);
                                if (hr == S_OK)
                                {
                                    // Scale the audio level for better VU meter response
                                    float scaledLevel = ScaleAudioLevel(peakValue);
                                    
                                    // Apply decay algorithm
                                    float currentValue = _decayValues.ContainsKey(containerId) ? _decayValues[containerId] : 0f;
                                    float newValue = Math.Max(scaledLevel, currentValue * 0.85f);
                                    _decayValues[containerId] = newValue;
                                    audioLevelEntry.Value = newValue;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Device audio level update error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"All devices update error: {ex.Message}");
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
                
                // Release all audio meter COM objects
                foreach (var audioMeter in _audioMeters.Values)
                {
                    try
                    {
                        Marshal.ReleaseComObject(audioMeter);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error releasing audio meter: {ex.Message}");
                    }
                }
                _audioMeters.Clear();
                
                // Release all audio device COM objects
                foreach (var device in _audioDevices.Values)
                {
                    try
                    {
                        Marshal.ReleaseComObject(device);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error releasing audio device: {ex.Message}");
                    }
                }
                _audioDevices.Clear();
                
                // Release device enumerator
                if (_deviceEnumerator != null)
                {
                    Marshal.ReleaseComObject(_deviceEnumerator);
                    _deviceEnumerator = null;
                }
                
                _decayValues.Clear();
                _containers.Clear();
                
                Console.WriteLine("AudioLevelMeter cleanup complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        public override string ConfigFilePath => "";
    }
}