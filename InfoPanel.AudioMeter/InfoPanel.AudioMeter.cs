using InfoPanel.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
        int GetId(out string ppstrId);
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
        private IMMDevice? _defaultDevice;
        private IAudioMeterInformation? _audioMeter;
        private readonly Dictionary<string, float> _decayValues = new();
        private readonly List<PluginContainer> _containers = new();
        private bool _initializationFailed = false;

        public AudioLevelMeter() : base("audio-level-meter", "Audio Level Meter", "Real-time system audio output level monitoring")
        {
        }

        public override TimeSpan UpdateInterval => TimeSpan.FromMilliseconds(50);

        public override void Initialize()
        {
            try
            {
                Console.WriteLine("AudioLevelMeter initializing...");
                
                // Create basic container structure - actual initialization happens in Update()
                PluginContainer container = new("default-audio", "Audio Level Meter");
                container.Entries.Add(new PluginText("device-name", "Device", "Initializing..."));
                container.Entries.Add(new PluginSensor("audio-level", "Audio Level", 0f, "%"));
                _containers.Add(container);
                _decayValues["default-audio"] = 0f;
                
                Console.WriteLine("AudioLevelMeter basic container created - Core Audio initialization will be attempted in updates");
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

        public override void Load(List<IPluginContainer> containers)
        {
            containers.AddRange(_containers.Cast<IPluginContainer>());
        }

        /// <summary>
        /// Gets the friendly name of an audio device
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
                    // Lazy initialization of Windows Core Audio API
                    if (_deviceEnumerator == null)
                    {
                        try
                        {
                            Console.WriteLine("Attempting Windows Core Audio API initialization...");
                            
                            // Create MMDeviceEnumerator COM object using CoCreateInstance to avoid NAudio conflicts
                            Guid clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
                            Guid iid = typeof(IMMDeviceEnumerator).GUID;
                            IntPtr enumeratorPtr;
                            
                            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out enumeratorPtr);
                            if (hr != S_OK)
                            {
                                Console.WriteLine($"CoCreateInstance failed with HRESULT: 0x{hr:X8}");
                                throw new System.ComponentModel.Win32Exception(hr);
                            }
                            
                            _deviceEnumerator = (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(enumeratorPtr);
                            Marshal.Release(enumeratorPtr);
                            
                            Console.WriteLine("Core Audio API enumerator created successfully");
                            
                            // Get default audio device
                            IMMDevice defaultDevice;
                            hr = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out defaultDevice);
                            if (hr == S_OK)
                            {
                                _defaultDevice = defaultDevice;
                                
                                // Get device friendly name
                                string deviceName = GetDeviceFriendlyName(defaultDevice);
                                
                                // Update container with real device info
                                var container = _containers.FirstOrDefault(c => c.Id == "default-audio");
                                if (container != null)
                                {
                                    var deviceNameEntry = container.Entries.OfType<PluginText>().FirstOrDefault(e => e.Id == "device-name");
                                    if (deviceNameEntry != null)
                                    {
                                        deviceNameEntry.Value = deviceName;
                                        Console.WriteLine($"Updated container with device: {deviceName}");
                                    }
                                }
                                
                                // Get audio meter interface
                                Guid audioMeterGuid = typeof(IAudioMeterInformation).GUID;
                                IntPtr audioMeterPtr;
                                hr = defaultDevice.Activate(ref audioMeterGuid, 0, IntPtr.Zero, out audioMeterPtr);
                                if (hr == S_OK)
                                {
                                    _audioMeter = (IAudioMeterInformation)Marshal.GetObjectForIUnknown(audioMeterPtr);
                                    Console.WriteLine("Audio meter interface activated successfully");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Windows Core Audio API initialization failed: {ex.Message}");
                            Console.WriteLine($"Exception type: {ex.GetType().Name}");
                            if (ex.InnerException != null)
                            {
                                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                            }
                            Console.WriteLine($"Stack trace: {ex.StackTrace}");
                            _initializationFailed = true;
                            
                            // Update container to show error
                            var container = _containers.FirstOrDefault(c => c.Id == "default-audio");
                            if (container != null)
                            {
                                var deviceNameEntry = container.Entries.OfType<PluginText>().FirstOrDefault(e => e.Id == "device-name");
                                if (deviceNameEntry != null)
                                {
                                    deviceNameEntry.Value = "Core Audio Error";
                                }
                            }
                            return;
                        }
                    }

                    // Get real-time audio levels
                    if (_audioMeter != null)
                    {
                        try
                        {
                            float peakValue;
                            int hr = _audioMeter.GetPeakValue(out peakValue);
                            if (hr == S_OK)
                            {
                                // Apply decay for realistic VU meter
                                string containerId = "default-audio";
                                float currentDecayValue = _decayValues.GetValueOrDefault(containerId, 0f);
                                currentDecayValue = Math.Max(peakValue, currentDecayValue * 0.85f);
                                _decayValues[containerId] = currentDecayValue;

                                // Update the audio level sensor
                                var container = _containers.FirstOrDefault(c => c.Id == containerId);
                                var sensor = container?.Entries.OfType<PluginSensor>().FirstOrDefault(s => s.Id == "audio-level");
                                if (sensor != null)
                                {
                                    sensor.Value = currentDecayValue * 100f; // Convert to percentage
                                }
                            }
                        }
                        catch (Exception audioEx)
                        {
                            Console.WriteLine($"Error reading audio levels: {audioEx.Message}");
                            // Set sensor to 0 on error
                            var container = _containers.FirstOrDefault(c => c.Id == "default-audio");
                            var sensor = container?.Entries.OfType<PluginSensor>().FirstOrDefault(s => s.Id == "audio-level");
                            if (sensor != null)
                            {
                                sensor.Value = 0f;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"AudioLevelMeter Update error: {ex.Message}");
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
                
                // Release COM objects
                if (_audioMeter != null)
                {
                    Marshal.ReleaseComObject(_audioMeter);
                    _audioMeter = null;
                }
                
                if (_defaultDevice != null)
                {
                    Marshal.ReleaseComObject(_defaultDevice);
                    _defaultDevice = null;
                }
                
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