using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using AudioLevelMeterPlugin.Models;

namespace AudioLevelMeterPlugin.Services
{
    /// <summary>
    /// Service for interacting with Windows Core Audio API.
    /// Not thread-safe: the plugin serialises Update and Close with its own lock.
    /// </summary>
    public class CoreAudioService : IDisposable
    {
        private const int DefaultCheckIntervalMs = 1000;
        private const int MeterRetryIntervalMs = 5000;

        private IMMDeviceEnumerator? _deviceEnumerator;
        private readonly Dictionary<string, IMMDevice> _cachedDevices = new();
        private readonly Dictionary<string, IAudioMeterInformation> _audioMeters = new();
        private readonly Dictionary<string, long> _meterRetryAt = new();
        private readonly float[] _peakBuffer = new float[2];
        // Channel peaks of a meter with other than two channels (reused while the count stays the same)
        private float[] _channelBuffer = new float[2];
        private bool _disposed = false;

        // Default endpoint state: resolved once, re-checked about once a second
        private readonly DefaultEndpoint _defaultOutput = new(DataFlow.Render, Role.Multimedia, "default-audio");
        private readonly DefaultEndpoint _defaultInput = new(DataFlow.Capture, Role.Console, "default-input");

        // Last peak-read failure that was logged (0 = none), so a failing meter logs once instead of every tick
        private int _lastPeakErrorHr;

        /// <summary>
        /// Cached state of one default endpoint (the default output or the default input)
        /// </summary>
        private sealed class DefaultEndpoint
        {
            public DefaultEndpoint(DataFlow flow, Role role, string containerId)
            {
                Flow = flow;
                Role = role;
                ContainerId = containerId;
            }

            public DataFlow Flow { get; }
            public Role Role { get; }
            public string ContainerId { get; }
            public IAudioMeterInformation? Meter;
            public AudioDeviceInfo? Info;
            public long LastCheck;
            public long NextRetry;
        }

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
                    try
                    {
                        _deviceEnumerator = (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(pUnknown);
                    }
                    finally
                    {
                        Marshal.Release(pUnknown);
                    }
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
        /// Get all active audio output devices, and the input devices when asked for
        /// </summary>
        public List<AudioDeviceInfo> GetAudioDevices(bool includeInputs)
        {
            var devices = new List<AudioDeviceInfo>();

            if (_deviceEnumerator == null)
                return devices;

            var seen = new HashSet<string>();
            EnumerateDevices(DataFlow.Render, devices, seen);
            if (includeInputs) EnumerateDevices(DataFlow.Capture, devices, seen);

            // Devices that are no longer active
            foreach (var staleId in _cachedDevices.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                ReleaseCachedDevice(staleId);
            }

            return devices;
        }

        private void EnumerateDevices(DataFlow flow, List<AudioDeviceInfo> devices, HashSet<string> seen)
        {
            string flowName = flow == DataFlow.Capture ? "input" : "output";
            IMMDeviceCollection? deviceCollection = null;
            try
            {
                int hr = _deviceEnumerator!.EnumAudioEndpoints(flow, 1, out deviceCollection); // 1 = DEVICE_STATE_ACTIVE
                if (hr != CoreAudioConstants.S_OK)
                {
                    Console.WriteLine($"CoreAudioService: Failed to enumerate {flowName} devices. HRESULT: 0x{hr:X8}");
                    return;
                }

                uint deviceCount;
                deviceCollection.GetCount(out deviceCount);
                Console.WriteLine($"CoreAudioService: Found {deviceCount} active audio {flowName} devices");

                for (uint i = 0; i < deviceCount; i++)
                {
                    IMMDevice? device = null;
                    bool stored = false;
                    try
                    {
                        deviceCollection.Item(i, out device);

                        string deviceId = GetDeviceId(device);
                        string friendlyName = GetDeviceFriendlyName(device);
                        string containerId = $"audio-meter-{deviceId.Replace("{", "").Replace("}", "").Replace(".", "-")}";

                        var deviceInfo = new AudioDeviceInfo
                        {
                            DeviceId = deviceId,
                            FriendlyName = friendlyName,
                            ContainerId = containerId,
                            IsDefault = false,
                            IsInput = flow == DataFlow.Capture
                        };

                        devices.Add(deviceInfo);
                        ReleaseCachedDevice(containerId);
                        _cachedDevices[containerId] = device;
                        stored = true;
                        seen.Add(containerId);

                        Console.WriteLine($"CoreAudioService: Device {i} ({flowName}): {friendlyName} (ID: {deviceId})");
                    }
                    catch (Exception deviceEx)
                    {
                        Console.WriteLine($"CoreAudioService: Failed to process device {i}: {deviceEx.Message}");
                    }
                    finally
                    {
                        if (!stored && device != null) Marshal.ReleaseComObject(device);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CoreAudioService: Device enumeration failed: {ex.Message}");
            }
            finally
            {
                if (deviceCollection != null) Marshal.ReleaseComObject(deviceCollection);
            }
        }

        /// <summary>
        /// Release a cached device and the meter activated from it
        /// </summary>
        private void ReleaseCachedDevice(string containerId)
        {
            if (_audioMeters.Remove(containerId, out var meter))
            {
                try { Marshal.ReleaseComObject(meter); } catch { }
            }
            _meterRetryAt.Remove(containerId);

            if (_cachedDevices.Remove(containerId, out var device))
            {
                try { Marshal.ReleaseComObject(device); } catch { }
            }
        }

        /// <summary>
        /// Get the current default audio output device (cached; re-resolved when the default endpoint changes)
        /// </summary>
        public AudioDeviceInfo? GetDefaultAudioDevice()
        {
            RefreshDefault(_defaultOutput);
            return _defaultOutput.Info;
        }

        /// <summary>
        /// Get the current default audio input device (cached; re-resolved when the default endpoint changes)
        /// </summary>
        public AudioDeviceInfo? GetDefaultInputDevice()
        {
            RefreshDefault(_defaultInput);
            return _defaultInput.Info;
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

            long now = Environment.TickCount64;
            if (_meterRetryAt.TryGetValue(containerId, out var retryAt) && now < retryAt)
                return null;

            var audioMeter = ActivateMeter(device);
            if (audioMeter == null)
            {
                _meterRetryAt[containerId] = now + MeterRetryIntervalMs;
                return null;
            }

            _meterRetryAt.Remove(containerId);
            _audioMeters[containerId] = audioMeter;
            Console.WriteLine($"CoreAudioService: Audio meter activated for device: {containerId}");
            return audioMeter;
        }

        /// <summary>
        /// Get the audio meter of the default output device. The service owns it: callers must not release it,
        /// and it stays valid only until the next call (the default endpoint may change).
        /// </summary>
        public IAudioMeterInformation? GetDefaultAudioMeter()
        {
            RefreshDefault(_defaultOutput);
            return _defaultOutput.Meter;
        }

        /// <summary>
        /// Get the audio meter of the default input device, with the same ownership rules as GetDefaultAudioMeter
        /// </summary>
        public IAudioMeterInformation? GetDefaultInputMeter()
        {
            RefreshDefault(_defaultInput);
            return _defaultInput.Meter;
        }

        /// <summary>
        /// Activate IAudioMeterInformation on a device, releasing the raw interface pointer after wrapping it
        /// </summary>
        private static IAudioMeterInformation? ActivateMeter(IMMDevice device)
        {
            IntPtr audioMeterPtr = IntPtr.Zero;
            try
            {
                Guid audioMeterGuid = typeof(IAudioMeterInformation).GUID;
                int hr = device.Activate(ref audioMeterGuid, 0, IntPtr.Zero, out audioMeterPtr);
                if (hr == CoreAudioConstants.S_OK && audioMeterPtr != IntPtr.Zero)
                {
                    return (IAudioMeterInformation)Marshal.GetObjectForIUnknown(audioMeterPtr);
                }

                Console.WriteLine($"CoreAudioService: Failed to activate audio meter (HR: 0x{hr:X8})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CoreAudioService: Failed to activate audio meter: {ex.Message}");
            }
            finally
            {
                if (audioMeterPtr != IntPtr.Zero) Marshal.Release(audioMeterPtr);
            }

            return null;
        }

        /// <summary>
        /// Re-resolve a default endpoint at most once per interval; the meter and device info are only
        /// rebuilt when the endpoint id changed (or when there is no valid meter yet).
        /// </summary>
        private void RefreshDefault(DefaultEndpoint endpoint)
        {
            if (_deviceEnumerator == null)
                return;

            long now = Environment.TickCount64;
            if (endpoint.Meter != null ? now - endpoint.LastCheck < DefaultCheckIntervalMs : now < endpoint.NextRetry)
                return;

            endpoint.LastCheck = now;
            endpoint.NextRetry = now + DefaultCheckIntervalMs;

            IMMDevice? defaultDevice = null;
            try
            {
                int hr = _deviceEnumerator.GetDefaultAudioEndpoint(endpoint.Flow, endpoint.Role, out defaultDevice);
                if (hr != CoreAudioConstants.S_OK)
                {
                    ReleaseDefault(endpoint);
                    return;
                }

                string deviceId = GetDeviceId(defaultDevice);
                if (endpoint.Meter != null && endpoint.Info?.DeviceId == deviceId)
                    return;

                ReleaseDefault(endpoint);

                var meter = ActivateMeter(defaultDevice);
                if (meter == null)
                    return;

                endpoint.Meter = meter;
                endpoint.Info = new AudioDeviceInfo
                {
                    DeviceId = deviceId,
                    FriendlyName = GetDeviceFriendlyName(defaultDevice),
                    ContainerId = endpoint.ContainerId,
                    IsDefault = true,
                    IsInput = endpoint.Flow == DataFlow.Capture
                };
                _lastPeakErrorHr = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CoreAudioService: Failed to get default device: {ex.Message}");
                ReleaseDefault(endpoint);
            }
            finally
            {
                if (defaultDevice != null) Marshal.ReleaseComObject(defaultDevice);
            }
        }

        private static void ReleaseDefault(DefaultEndpoint endpoint)
        {
            var meter = endpoint.Meter;
            endpoint.Meter = null;
            endpoint.Info = null;
            if (meter != null)
            {
                try { Marshal.ReleaseComObject(meter); } catch { }
            }
        }

        /// <summary>
        /// Get peak values for all audio channels from an audio meter.
        /// The returned array is reused by the next call; consume it before calling again.
        /// </summary>
        public float[] GetChannelPeakValues(IAudioMeterInformation audioMeter)
        {
            try
            {
                if (audioMeter == null)
                {
                    return ZeroPeaks();
                }

                // Per-channel peaks, so left and right differ; any failure falls back to the overall peak
                if (audioMeter.GetMeteringChannelCount(out uint channelCount) == CoreAudioConstants.S_OK && channelCount is >= 1 and <= 32)
                {
                    var buffer = channelCount == 2 ? _peakBuffer : _channelBuffer.Length == channelCount ? _channelBuffer : (_channelBuffer = new float[channelCount]);
                    if (audioMeter.GetChannelsPeakValues(channelCount, buffer) == CoreAudioConstants.S_OK)
                    {
                        _lastPeakErrorHr = 0;
                        return buffer;
                    }
                }

                float peakValue;
                int hr = audioMeter.GetPeakValue(out peakValue);
                if (hr == CoreAudioConstants.S_OK)
                {
                    _lastPeakErrorHr = 0;

                    // Return as stereo pair for compatibility with existing code
                    _peakBuffer[0] = peakValue;
                    _peakBuffer[1] = peakValue;
                    return _peakBuffer;
                }
                else
                {
                    OnPeakReadFailed(audioMeter, hr, $"GetPeakValue failed with HRESULT: 0x{hr:X8}");
                    return ZeroPeaks();
                }

            }
            catch (Exception ex)
            {
                OnPeakReadFailed(audioMeter, ex.HResult, $"Error getting channel peak values: {ex.Message}");
                return ZeroPeaks();
            }
        }

        private float[] ZeroPeaks()
        {
            _peakBuffer[0] = 0f;
            _peakBuffer[1] = 0f;
            return _peakBuffer;
        }

        /// <summary>
        /// Log a peak read failure once per distinct HRESULT and drop the default meter so the
        /// next refresh re-resolves the endpoint (device removed or invalidated)
        /// </summary>
        private void OnPeakReadFailed(IAudioMeterInformation audioMeter, int hr, string message)
        {
            if (hr != _lastPeakErrorHr)
            {
                _lastPeakErrorHr = hr;
                Console.WriteLine($"CoreAudioService: {message}");
            }

            foreach (var endpoint in new[] { _defaultOutput, _defaultInput })
            {
                if (ReferenceEquals(audioMeter, endpoint.Meter))
                {
                    ReleaseDefault(endpoint);
                    endpoint.NextRetry = 0;
                }
            }
        }

        /// <summary>
        /// Get all initialized audio meters (the live dictionary, not a copy; do not hold it across updates)
        /// </summary>
        public IReadOnlyDictionary<string, IAudioMeterInformation> GetAllAudioMeters()
        {
            return _audioMeters;
        }

        /// <summary>
        /// Safely gets the device ID from an IMMDevice
        /// </summary>
        private string GetDeviceId(IMMDevice device)
        {
            IntPtr deviceIdPtr = IntPtr.Zero;
            try
            {
                int hr = device.GetId(out deviceIdPtr);
                if (hr == CoreAudioConstants.S_OK && deviceIdPtr != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUni(deviceIdPtr) ?? "Unknown";
                }
                return "Unknown";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CoreAudioService: Failed to get device ID: {ex.Message}");
                return "Unknown";
            }
            finally
            {
                if (deviceIdPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(deviceIdPtr);
            }
        }

        /// <summary>
        /// Gets the friendly name of an audio device
        /// </summary>
        private string GetDeviceFriendlyName(IMMDevice device)
        {
            IPropertyStore? propertyStore = null;
            try
            {
                int hr = device.OpenPropertyStore(0, out propertyStore);
                if (hr != CoreAudioConstants.S_OK) return "Unknown Device";

                var nameKey = CoreAudioConstants.PKEY_Device_FriendlyName;
                PROPVARIANT nameValue;
                hr = propertyStore.GetValue(ref nameKey, out nameValue);
                if (hr != CoreAudioConstants.S_OK) return "Unknown Device";

                try
                {
                    if (nameValue.vt == 31) // VT_LPWSTR
                    {
                        return Marshal.PtrToStringUni(nameValue.pwszVal) ?? "Unknown Device";
                    }
                    return "Unknown Device";
                }
                finally
                {
                    PropVariantClear(ref nameValue);
                }
            }
            catch
            {
                return "Unknown Device";
            }
            finally
            {
                if (propertyStore != null) Marshal.ReleaseComObject(propertyStore);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                Console.WriteLine("CoreAudioService: Cleaning up resources...");

                ReleaseDefault(_defaultOutput);
                ReleaseDefault(_defaultInput);

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
                _meterRetryAt.Clear();

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
