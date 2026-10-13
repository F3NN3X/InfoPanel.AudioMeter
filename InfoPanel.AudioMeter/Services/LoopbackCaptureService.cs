using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using AudioLevelMeterPlugin.Models;

namespace AudioLevelMeterPlugin.Services
{
    /// <summary>
    /// Captures what each render device is playing (WASAPI loopback) on one background thread
    /// and publishes the spectrum of each device as a comma-separated string.
    /// </summary>
    public class LoopbackCaptureService : IDisposable
    {
        private const int PollIntervalMs = 10;
        private const int SilenceTimeoutMs = 150;
        private const int DecayIntervalMs = 20;
        private const int RetryIntervalMs = 5000;
        private const int DefaultCheckIntervalMs = 1000;

        private readonly SpectrumSettings _settings;
        private readonly List<CaptureTarget> _targets = new();
        private readonly Dictionary<string, CaptureTarget> _targetsById = new();
        private Thread? _thread;
        private volatile bool _stop;
        private bool _disposed;

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        public LoopbackCaptureService(SpectrumSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Register a container to capture. A null deviceId follows the default render endpoint.
        /// Call before Start.
        /// </summary>
        public void AddTarget(string containerId, string? deviceId)
        {
            if (_thread != null || _targetsById.ContainsKey(containerId)) return;
            var target = new CaptureTarget(containerId, deviceId);
            _targets.Add(target);
            _targetsById[containerId] = target;
        }

        public void Start()
        {
            if (!_settings.Enabled || _thread != null || _targets.Count == 0) return;
            _thread = new Thread(PollLoop) { IsBackground = true, Name = "AudioMeter.Loopback" };
            _thread.Start();
        }

        /// <summary>
        /// Latest bands for a container as "0,12,40,..."; empty when disabled or the device failed.
        /// </summary>
        public string GetSpectrum(string containerId)
        {
            return _targetsById.TryGetValue(containerId, out var target) ? target.Latest : string.Empty;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stop = true;
            try
            {
                if (_thread != null && _thread.IsAlive && !_thread.Join(2000))
                {
                    Console.WriteLine("LoopbackCaptureService: capture thread did not stop in time");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LoopbackCaptureService: Error stopping capture thread: {ex.Message}");
            }
        }

        private void PollLoop()
        {
            IMMDeviceEnumerator? enumerator = null;
            var clock = Stopwatch.StartNew();
            try
            {
                var clsid = CoreAudioConstants.MMDeviceEnumeratorCLSID;
                var iid = typeof(IMMDeviceEnumerator).GUID;
                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CoreAudioConstants.CLSCTX_INPROC_SERVER, ref iid, out IntPtr pUnknown);
                if (hr != CoreAudioConstants.S_OK || pUnknown == IntPtr.Zero)
                {
                    Console.WriteLine($"LoopbackCaptureService: Failed to create enumerator. HRESULT: 0x{hr:X8}");
                    foreach (var t in _targets) t.Latest = string.Empty;
                    return;
                }
                enumerator = (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(pUnknown);
                Marshal.Release(pUnknown);

                while (!_stop)
                {
                    long now = clock.ElapsedMilliseconds;
                    foreach (var target in _targets)
                    {
                        try
                        {
                            target.Poll(enumerator, now, _settings);
                        }
                        catch (Exception ex)
                        {
                            target.Fail(now, ex.Message);
                        }
                    }
                    Thread.Sleep(PollIntervalMs);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LoopbackCaptureService: Capture thread error: {ex.Message}");
            }
            finally
            {
                foreach (var target in _targets)
                {
                    target.Teardown();
                    target.Latest = string.Empty;
                }
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        /// <summary>
        /// One loopback client plus its analyzer. Only the capture thread touches it,
        /// except Latest, which is swapped atomically.
        /// </summary>
        private sealed class CaptureTarget
        {
            private readonly string _containerId;
            private readonly string? _deviceId;

            private IAudioClient? _client;
            private IAudioCaptureClient? _capture;
            private SpectrumAnalyzer? _analyzer;
            private string? _activeDeviceId;
            private int _channels;
            private int _bitsPerSample;
            private bool _isFloat;
            private float[] _mono = new float[4096];
            private readonly float[] _bandBuffer;

            private bool _loggedFailure;
            private long _nextRetry;
            private long _lastData;
            private long _lastDecay;
            private long _lastDefaultCheck;

            public volatile string Latest = string.Empty;

            public CaptureTarget(string containerId, string? deviceId)
            {
                _containerId = containerId;
                _deviceId = deviceId;
                _bandBuffer = new float[64];
            }

            public void Poll(IMMDeviceEnumerator enumerator, long now, SpectrumSettings settings)
            {
                if (_client == null)
                {
                    if (now < _nextRetry) return;
                    if (!TryStart(enumerator, now, settings)) return;
                }

                if (_deviceId == null && now - _lastDefaultCheck >= DefaultCheckIntervalMs)
                {
                    _lastDefaultCheck = now;
                    if (ResolveDefaultId(enumerator) != _activeDeviceId)
                    {
                        // Default endpoint changed: re-resolve on the next poll
                        Teardown();
                        _nextRetry = 0;
                        return;
                    }
                }

                bool gotData = false;
                while (true)
                {
                    int hr = _capture!.GetNextPacketSize(out uint packetFrames);
                    if (hr != CoreAudioConstants.S_OK) { Fail(now, $"GetNextPacketSize 0x{hr:X8}"); return; }
                    if (packetFrames == 0) break;

                    hr = _capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _);
                    if (hr != CoreAudioConstants.S_OK) { Fail(now, $"GetBuffer 0x{hr:X8}"); return; }

                    try
                    {
                        MixToMono(data, (int)frames, (flags & CoreAudioConstants.AUDCLNT_BUFFERFLAGS_SILENT) != 0);
                    }
                    finally
                    {
                        _capture.ReleaseBuffer(frames);
                    }

                    if (_analyzer!.Push(_mono.AsSpan(0, (int)frames)) > 0) Publish();
                    gotData = true;
                }

                if (gotData)
                {
                    _lastData = now;
                }
                else if (now - _lastData > SilenceTimeoutMs && now - _lastDecay >= DecayIntervalMs)
                {
                    // Loopback sends nothing while nothing plays: ease the bands to zero
                    _lastDecay = now;
                    _analyzer!.Decay();
                    Publish();
                }
            }

            public void Fail(long now, string reason)
            {
                Teardown();
                Latest = string.Empty;
                _nextRetry = now + RetryIntervalMs;
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Console.WriteLine($"LoopbackCaptureService: Capture failed for '{_containerId}' ({reason}); retrying every {RetryIntervalMs / 1000}s");
                }
            }

            public void Teardown()
            {
                try { _client?.Stop(); } catch { }
                if (_capture != null) { try { Marshal.ReleaseComObject(_capture); } catch { } }
                if (_client != null) { try { Marshal.ReleaseComObject(_client); } catch { } }
                _capture = null;
                _client = null;
                _analyzer = null;
                _activeDeviceId = null;
            }

            private bool TryStart(IMMDeviceEnumerator enumerator, long now, SpectrumSettings settings)
            {
                IMMDevice? device = null;
                IntPtr pFormat = IntPtr.Zero;
                try
                {
                    if (_deviceId == null)
                    {
                        int hrDefault = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console, out device);
                        if (hrDefault != CoreAudioConstants.S_OK) { Fail(now, $"no default render device 0x{hrDefault:X8}"); return false; }
                    }
                    else
                    {
                        int hrDev = enumerator.GetDevice(_deviceId, out device);
                        if (hrDev != CoreAudioConstants.S_OK) { Fail(now, $"device not found 0x{hrDev:X8}"); return false; }
                    }

                    _activeDeviceId = GetId(device);

                    var iidClient = CoreAudioConstants.IID_IAudioClient;
                    device.Activate(ref iidClient, CoreAudioConstants.CLSCTX_ALL, IntPtr.Zero, out IntPtr pClient);
                    _client = (IAudioClient)Marshal.GetObjectForIUnknown(pClient);
                    Marshal.Release(pClient);

                    int hr = _client.GetMixFormat(out pFormat);
                    if (hr != CoreAudioConstants.S_OK) { Fail(now, $"GetMixFormat 0x{hr:X8}"); return false; }
                    ReadFormat(pFormat, out int sampleRate);

                    hr = _client.Initialize(CoreAudioConstants.AUDCLNT_SHAREMODE_SHARED, CoreAudioConstants.AUDCLNT_STREAMFLAGS_LOOPBACK,
                        200 * 10000, 0, pFormat, IntPtr.Zero);
                    if (hr != CoreAudioConstants.S_OK) { Fail(now, $"Initialize 0x{hr:X8}"); return false; }

                    var iidCapture = CoreAudioConstants.IID_IAudioCaptureClient;
                    hr = _client.GetService(ref iidCapture, out IntPtr pCapture);
                    if (hr != CoreAudioConstants.S_OK) { Fail(now, $"GetService 0x{hr:X8}"); return false; }
                    _capture = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(pCapture);
                    Marshal.Release(pCapture);

                    hr = _client.Start();
                    if (hr != CoreAudioConstants.S_OK) { Fail(now, $"Start 0x{hr:X8}"); return false; }

                    _analyzer = new SpectrumAnalyzer(sampleRate, settings.Bands, settings.MinHz, settings.MaxHz, settings.FloorDb);
                    _lastData = now;
                    _lastDecay = now;
                    _lastDefaultCheck = now;
                    if (_loggedFailure) Console.WriteLine($"LoopbackCaptureService: Capture recovered for '{_containerId}'");
                    _loggedFailure = false;
                    Publish();
                    return true;
                }
                catch (Exception ex)
                {
                    Fail(now, ex.Message);
                    return false;
                }
                finally
                {
                    if (pFormat != IntPtr.Zero) CoTaskMemFree(pFormat);
                    if (device != null) Marshal.ReleaseComObject(device);
                }
            }

            private static string? ResolveDefaultId(IMMDeviceEnumerator enumerator)
            {
                IMMDevice? device = null;
                try
                {
                    if (enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console, out device) != CoreAudioConstants.S_OK) return null;
                    return GetId(device);
                }
                finally
                {
                    if (device != null) Marshal.ReleaseComObject(device);
                }
            }

            private static string? GetId(IMMDevice device)
            {
                if (device.GetId(out IntPtr ptr) != CoreAudioConstants.S_OK || ptr == IntPtr.Zero) return null;
                string? id = Marshal.PtrToStringUni(ptr);
                Marshal.FreeCoTaskMem(ptr);
                return id;
            }

            private void ReadFormat(IntPtr pFormat, out int sampleRate)
            {
                var fmt = Marshal.PtrToStructure<WAVEFORMATEX>(pFormat);
                sampleRate = (int)fmt.nSamplesPerSec;
                _channels = Math.Max(1, (int)fmt.nChannels);
                _bitsPerSample = fmt.wBitsPerSample;

                ushort tag = fmt.wFormatTag;
                if (tag == CoreAudioConstants.WAVE_FORMAT_EXTENSIBLE && fmt.cbSize >= 22)
                {
                    var ext = Marshal.PtrToStructure<WAVEFORMATEXTENSIBLE>(pFormat);
                    // SubFormat GUIDs start with the WAVE_FORMAT_* tag in Data1
                    tag = (ushort)ext.SubFormat.ToByteArray()[0];
                }

                _isFloat = tag == CoreAudioConstants.WAVE_FORMAT_IEEE_FLOAT;
                bool supported = _isFloat ? _bitsPerSample == 32
                    : tag == CoreAudioConstants.WAVE_FORMAT_PCM && (_bitsPerSample == 16 || _bitsPerSample == 24 || _bitsPerSample == 32);
                if (!supported) throw new NotSupportedException($"mix format tag {tag}, {_bitsPerSample} bit");
                if (sampleRate <= 0) throw new NotSupportedException("invalid sample rate");
            }

            private unsafe void MixToMono(IntPtr data, int frames, bool silent)
            {
                if (_mono.Length < frames) _mono = new float[frames];
                var mono = _mono;

                if (silent || data == IntPtr.Zero)
                {
                    Array.Clear(mono, 0, frames);
                    return;
                }

                byte* p = (byte*)data;
                int ch = _channels;
                float inv = 1f / ch;

                for (int f = 0; f < frames; f++)
                {
                    float sum = 0f;
                    for (int c = 0; c < ch; c++)
                    {
                        if (_isFloat)
                        {
                            sum += *(float*)p;
                            p += 4;
                        }
                        else if (_bitsPerSample == 16)
                        {
                            sum += *(short*)p / 32768f;
                            p += 2;
                        }
                        else if (_bitsPerSample == 24)
                        {
                            int v = (p[0] << 8) | (p[1] << 16) | (p[2] << 24);
                            sum += v / 2147483648f;
                            p += 3;
                        }
                        else
                        {
                            sum += *(int*)p / 2147483648f;
                            p += 4;
                        }
                    }
                    mono[f] = sum * inv;
                }
            }

            private void Publish()
            {
                var analyzer = _analyzer;
                if (analyzer == null) return;

                int n = analyzer.BandCount;
                analyzer.CopyBands(_bandBuffer.AsSpan(0, n));

                var sb = new StringBuilder(n * 4);
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append((int)Math.Round(_bandBuffer[i]));
                }
                Latest = sb.ToString();
            }
        }
    }
}
