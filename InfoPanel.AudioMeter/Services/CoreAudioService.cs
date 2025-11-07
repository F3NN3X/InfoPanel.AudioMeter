using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using AudioLevelMeterPlugin.Models;

namespace AudioLevelMeterPlugin.Services
{
    /// <summary>
    /// Service for interacting with Windows Core Audio API
    /// </summary>
    public class CoreAudioService : IDisposable
    {
        private IMMDeviceEnumerator? _deviceEnumerator;
        private readonly Dictionary<string, IMMDevice> _cachedDevices = new();
        private readonly Dictionary<string, IAudioMeterInformation> _audioMeters = new();
        private bool _disposed = false;

        [DllImport("ole32.dll")]
        private static extern void PropVariantClear(ref PROPVARIANT pvar);

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

        /// <summary>
        /// Initialize the Core Audio API
        /// </summary>
        public bool Initialize()
        {
            try
            {
                var clsid = CoreAudioConstants.MMDeviceEnumeratorCLSID;
                var iid = typeof(IMMDeviceEnumerator).GUID;
                IntPtr pUnknown;
                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CoreAudioConstants.CLSCTX_INPROC_SERVER, ref iid, out pUnknown);
                
                if (hr == CoreAudioConstants.S_OK && pUnknown != IntPtr.Zero)
                {
                    _deviceEnumerator = (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(pUnknown);
                    Marshal.Release(pUnknown);
                    Console.WriteLine("CoreAudioService: Core Audio API enumerator created successfully");
                    return true;
                }
                else
                {
                    Console.WriteLine($"CoreAudioService: Failed to create Core Audio API enumerator. HRESULT: 0x{hr:X8}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CoreAudioService: Core Audio API initialization failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get all active audio output devices
        /// </summary>
        public List<AudioDeviceInfo> GetAudioOutputDevices()
        {
            var devices = new List<AudioDeviceInfo>();
            
            if (_deviceEnumerator == null)
                return devices;

            try
            {
                IMMDeviceCollection deviceCollection;
                int hr = _deviceEnumerator.EnumAudioEndpoints(DataFlow.Render, 1, out deviceCollection); // 1 = DEVICE_STATE_ACTIVE
                if (hr != CoreAudioConstants.S_OK)
                {
                    Console.WriteLine($"CoreAudioService: Failed to enumerate devices. HRESULT: 0x{hr:X8}");
                    return devices;
                }

                uint deviceCount;
                deviceCollection.GetCount(out deviceCount);
                Console.WriteLine($"CoreAudioService: Found {deviceCount} active audio output devices");

                for (uint i = 0; i < deviceCount; i++)
                {
                    try
                    {
                        IMMDevice device;
                        deviceCollection.Item(i, out device);
                        
                        string deviceId = GetDeviceId(device);
                        string friendlyName = GetDeviceFriendlyName(device);
                        string containerId = $"audio-meter-{deviceId.Replace("{", "").Replace("}", "").Replace(".", "-")}";

                        var deviceInfo = new AudioDeviceInfo
                        {
                            DeviceId = deviceId,
                            FriendlyName = friendlyName,
                            ContainerId = containerId,
                            IsDefault = false
                        };

                        devices.Add(deviceInfo);
                        _cachedDevices[containerId] = device;
                        
                        Console.WriteLine($"CoreAudioService: Device {i}: {friendlyName} (ID: {deviceId})");
                    }
                    catch (Exception deviceEx)
                    {
                        Console.WriteLine($"CoreAudioService: Failed to process device {i}: {deviceEx.Message}");
                    }
                }

                Marshal.ReleaseComObject(deviceCollection);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CoreAudioService: Device enumeration failed: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Get the current default audio output device
        /// </summary>
        public AudioDeviceInfo? GetDefaultAudioDevice()
        {
            if (_deviceEnumerator == null)
                return null;

            try
            {
                IMMDevice defaultDevice;
                int hr = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out defaultDevice);
                if (hr == CoreAudioConstants.S_OK)
                {
                    string deviceId = GetDeviceId(defaultDevice);
                    string friendlyName = GetDeviceFriendlyName(defaultDevice);

                    var deviceInfo = new AudioDeviceInfo
                    {
                        DeviceId = deviceId,
                        FriendlyName = friendlyName,
                        ContainerId = "default-audio",
                        IsDefault = true
                    };

                    Marshal.ReleaseComObject(defaultDevice);
                    return deviceInfo;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CoreAudioService: Failed to get default device: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Get an audio meter for the specified container
        /// </summary>
        public IAudioMeterInformation? GetAudioMeter(string containerId)
        {
            if (_audioMeters.TryGetValue(containerId, out var existingMeter))
                return existingMeter;

            if (!_cachedDevices.TryGetValue(containerId, out var device))
                return null;

            try
            {
                Guid audioMeterGuid = typeof(IAudioMeterInformation).GUID;
                IntPtr audioMeterPtr;
                int hr = device.Activate(ref audioMeterGuid, 0, IntPtr.Zero, out audioMeterPtr);
                if (hr == CoreAudioConstants.S_OK)
                {
                    var audioMeter = (IAudioMeterInformation)Marshal.GetObjectForIUnknown(audioMeterPtr);
                    _audioMeters[containerId] = audioMeter;
                    Console.WriteLine($"CoreAudioService: Audio meter activated for device: {containerId}");
                    return audioMeter;
                }
                else
                {
                    Console.WriteLine($"CoreAudioService: Failed to activate audio meter for device: {containerId} (HR: 0x{hr:X8})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CoreAudioService: Failed to activate audio meter for device: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Get an audio meter for the default device
        /// </summary>
        public IAudioMeterInformation? GetDefaultAudioMeter()
        {
            if (_deviceEnumerator == null)
                return null;

            try
            {
                IMMDevice defaultDevice;
                int hr = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out defaultDevice);
                if (hr == CoreAudioConstants.S_OK)
                {
                    Guid audioMeterGuid = typeof(IAudioMeterInformation).GUID;
                    IntPtr audioMeterPtr;
                    hr = defaultDevice.Activate(ref audioMeterGuid, 0, IntPtr.Zero, out audioMeterPtr);
                    if (hr == CoreAudioConstants.S_OK)
                    {
                        var audioMeter = (IAudioMeterInformation)Marshal.GetObjectForIUnknown(audioMeterPtr);
                        Marshal.ReleaseComObject(defaultDevice);
                        return audioMeter;
                    }
                    Marshal.ReleaseComObject(defaultDevice);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CoreAudioService: Failed to get default audio meter: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Get peak values for all audio channels from an audio meter
        /// </summary>
        public float[] GetChannelPeakValues(IAudioMeterInformation audioMeter)
        {
            try
            {
                if (audioMeter == null)
                {
                    Console.WriteLine("CoreAudioService: audioMeter is null");
                    return new float[] { 0f };
                }

                // For now, use only the single peak value method to avoid COM marshaling issues
                // TODO: Implement proper multi-channel support once marshaling is resolved
                float peakValue;
                int hr = audioMeter.GetPeakValue(out peakValue);
                if (hr == CoreAudioConstants.S_OK)
                {
                    // Debug logging
                    if (peakValue > 0f)
                    {
                        Console.WriteLine($"CoreAudioService: Got peak value: {peakValue:F4}");
                    }
                    
                    // Return as stereo pair for compatibility with existing code
                    return new float[] { peakValue, peakValue };
                }
                else
                {
                    Console.WriteLine($"CoreAudioService: GetPeakValue failed with HRESULT: 0x{hr:X8}");
                    return new float[] { 0f, 0f };
                }

                /* 
                // DISABLED: Multi-channel implementation causing COM marshaling crashes
                // Get channel count
                uint channelCount;
                int hr = audioMeter.GetMeteringChannelCount(out channelCount);
                if (hr != CoreAudioConstants.S_OK || channelCount == 0)
                {
                    // Fallback to single peak value
                    float peakValue;
                    hr = audioMeter.GetPeakValue(out peakValue);
                    return hr == CoreAudioConstants.S_OK ? new float[] { peakValue } : new float[] { 0f };
                }
                
                // Validate channel count is reasonable
                if (channelCount > 32) // Sanity check - most systems won't have more than 32 channels
                {
                    Console.WriteLine($"CoreAudioService: Unreasonable channel count: {channelCount}, falling back to peak value");
                    float peakValue;
                    hr = audioMeter.GetPeakValue(out peakValue);
                    return hr == CoreAudioConstants.S_OK ? new float[] { peakValue } : new float[] { 0f };
                }
                
                // Pre-allocate array for peak values
                float[] channelPeaks = new float[channelCount];
                hr = audioMeter.GetChannelsPeakValues(channelCount, channelPeaks);
                if (hr == CoreAudioConstants.S_OK)
                {
                    return channelPeaks;
                }
                
                // Fallback to single peak value
                float fallbackPeak;
                hr = audioMeter.GetPeakValue(out fallbackPeak);
                return hr == CoreAudioConstants.S_OK ? new float[] { fallbackPeak } : new float[] { 0f };
                */
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CoreAudioService: Error getting channel peak values: {ex.Message}");
                return new float[] { 0f, 0f };
            }
        }

        /// <summary>
        /// Get all initialized audio meters
        /// </summary>
        public Dictionary<string, IAudioMeterInformation> GetAllAudioMeters()
        {
            return _audioMeters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
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
                if (hr == CoreAudioConstants.S_OK && deviceIdPtr != IntPtr.Zero)
                {
                    string? deviceIdStr = Marshal.PtrToStringUni(deviceIdPtr);
                    Marshal.FreeCoTaskMem(deviceIdPtr);
                    return deviceIdStr ?? "Unknown";
                }
                return "Unknown";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CoreAudioService: Failed to get device ID: {ex.Message}");
                return "Unknown";
            }
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
                if (hr != CoreAudioConstants.S_OK) return "Unknown Device";

                var nameKey = CoreAudioConstants.PKEY_Device_FriendlyName;
                PROPVARIANT nameValue;
                hr = propertyStore.GetValue(ref nameKey, out nameValue);
                if (hr == CoreAudioConstants.S_OK && nameValue.vt == 31) // VT_LPWSTR
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

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                Console.WriteLine("CoreAudioService: Cleaning up resources...");
                
                // Release all audio meter COM objects
                foreach (var audioMeter in _audioMeters.Values)
                {
                    try
                    {
                        Marshal.ReleaseComObject(audioMeter);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"CoreAudioService: Error releasing audio meter: {ex.Message}");
                    }
                }
                _audioMeters.Clear();
                
                // Release all audio device COM objects
                foreach (var device in _cachedDevices.Values)
                {
                    try
                    {
                        Marshal.ReleaseComObject(device);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"CoreAudioService: Error releasing audio device: {ex.Message}");
                    }
                }
                _cachedDevices.Clear();
                
                // Release device enumerator
                if (_deviceEnumerator != null)
                {
                    Marshal.ReleaseComObject(_deviceEnumerator);
                    _deviceEnumerator = null;
                }
                
                Console.WriteLine("CoreAudioService: Cleanup complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CoreAudioService: Error during cleanup: {ex.Message}");
            }
            
            _disposed = true;
        }
    }
}