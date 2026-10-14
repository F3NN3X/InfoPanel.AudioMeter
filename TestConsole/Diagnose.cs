using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using AudioLevelMeterPlugin.Services;

/// <summary>
/// `--diagnose &lt;device-id-part&gt;`: opens WASAPI loopback on a render device for ~3 s and prints the raw
/// sample peak and RMS, the analyzer's bands, the endpoint volume and the meter peak side by side.
/// `--noise`: feeds the analyzer synthetic white and pink noise and a sine at known levels.
/// Read-only: never changes a volume or the default device.
/// </summary>
static class Diagnose
{
    private const int Bands = 32;
    private const float MinHz = 30f, MaxHz = 16000f, FloorDb = -80f;

    public static int Run(string[] args)
    {
        if (args[0] == "--noise") return Noise();
        if (args.Length < 2) { Console.WriteLine("Usage: TestConsole --diagnose <device-id-part>"); return 2; }
        return Live(args[1]);
    }

    private static int Noise()
    {
        const int sampleRate = 48000;
        var rng = new Random(1);
        var buffer = new float[Bands];
        int n = 48000 * 2;

        float[] white = new float[n];
        for (int i = 0; i < n; i++) white[i] = (float)(rng.NextDouble() * 2 - 1);

        // Paul Kellet's pink filter over white noise
        float[] pink = new float[n];
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        for (int i = 0; i < n; i++)
        {
            double w = rng.NextDouble() * 2 - 1;
            b0 = 0.99886 * b0 + w * 0.0555179; b1 = 0.99332 * b1 + w * 0.0750759;
            b2 = 0.96900 * b2 + w * 0.1538520; b3 = 0.86650 * b3 + w * 0.3104856;
            b4 = 0.55000 * b4 + w * 0.5329522; b5 = -0.7616 * b5 - w * 0.0168980;
            pink[i] = (float)(b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362);
            b6 = w * 0.115926;
        }

        float[] sine = new float[n];
        for (int i = 0; i < n; i++) sine[i] = (float)Math.Sin(2 * Math.PI * 1000.0 * i / sampleRate);

        foreach (var (label, src) in new[] { ("white", white), ("pink", pink), ("sine 1k", sine) })
        {
            // Scale to -10 dBFS RMS
            double rms = Math.Sqrt(src.Average(v => (double)v * v));
            float gain = (float)(Math.Pow(10, -10.0 / 20) / rms);
            var scaled = src.Select(v => v * gain).ToArray();
            var analyzer = new SpectrumAnalyzer(sampleRate, Bands, MinHz, MaxHz, FloorDb);
            analyzer.Push(scaled);
            analyzer.CopyBands(buffer);
            Console.WriteLine($"{label,-8} @ -10 dBFS RMS: {string.Join(",", buffer.Select(v => (int)Math.Round(v)))}");
        }
        return 0;
    }

    private static int Live(string idPart)
    {
        var clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(clsid)!)!;
        Check(enumerator.EnumAudioEndpoints(0, 1, out var collection), "EnumAudioEndpoints");
        collection.GetCount(out uint count);
        IMMDevice? device = null;
        string? id = null;
        for (uint i = 0; i < count; i++)
        {
            collection.Item(i, out var d);
            d.GetId(out IntPtr p);
            string s = Marshal.PtrToStringUni(p)!;
            Marshal.FreeCoTaskMem(p);
            if (s.Contains(idPart, StringComparison.OrdinalIgnoreCase)) { device = d; id = s; break; }
        }
        if (device == null) { Console.WriteLine($"no active render device matching '{idPart}'"); return 1; }
        Console.WriteLine($"device {id}");

        var iidVol = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        Check(device.Activate(ref iidVol, 0x17, IntPtr.Zero, out IntPtr pVol), "Activate volume");
        var volume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(pVol);
        var iidMeter = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
        Check(device.Activate(ref iidMeter, 0x17, IntPtr.Zero, out IntPtr pMeter), "Activate meter");
        var meter = (IAudioMeterInformation)Marshal.GetObjectForIUnknown(pMeter);
        var iidClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        Check(device.Activate(ref iidClient, 0x17, IntPtr.Zero, out IntPtr pClient), "Activate client");
        var client = (IAudioClient)Marshal.GetObjectForIUnknown(pClient);

        volume.GetMasterVolumeLevelScalar(out float scalar);
        volume.GetMasterVolumeLevel(out float volDb);
        volume.GetMute(out bool muted);
        volume.QueryHardwareSupport(out uint hw);
        Console.WriteLine($"endpoint volume scalar {scalar:F3} ({20 * Math.Log10(Math.Max(scalar, 1e-9)):F1} dB as amplitude), level {volDb:F1} dB, muted {muted}, hw support 0x{hw:X}");

        Check(client.GetMixFormat(out IntPtr pFormat), "GetMixFormat");
        short tag = Marshal.ReadInt16(pFormat, 0);
        short channels = Marshal.ReadInt16(pFormat, 2);
        int rate = Marshal.ReadInt32(pFormat, 4);
        short bits = Marshal.ReadInt16(pFormat, 14);
        Console.WriteLine($"mix format tag 0x{tag:X4} ch {channels} rate {rate} bits {bits}");
        Check(client.Initialize(0, 0x00020000, 200 * 10000, 0, pFormat, IntPtr.Zero), "Initialize");
        var iidCapture = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
        Check(client.GetService(ref iidCapture, out IntPtr pCapture), "GetService");
        var capture = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(pCapture);
        Check(client.Start(), "Start");

        var analyzer = new SpectrumAnalyzer(rate, Bands, MinHz, MaxHz, FloorDb);
        var bands = new float[Bands];
        var mono = new float[1 << 16];
        var clock = Stopwatch.StartNew();
        long nextReport = 500;
        double peak = 0, sumSq = 0; long samples = 0; float meterPeak = 0;
        while (clock.ElapsedMilliseconds < 3500)
        {
            meter.GetPeakValue(out float mp);
            if (mp > meterPeak) meterPeak = mp;
            while (capture.GetNextPacketSize(out uint pkt) == 0 && pkt > 0)
            {
                capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _);
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0;
                    for (int c = 0; c < channels; c++)
                    {
                        float v = (flags & 2) != 0 ? 0f : BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, (f * channels + c) * 4));
                        peak = Math.Max(peak, Math.Abs(v));
                        sumSq += v * v; samples++;
                        sum += v;
                    }
                    mono[f] = sum / channels;
                }
                capture.ReleaseBuffer(frames);
                analyzer.Push(mono.AsSpan(0, (int)frames));
            }
            if (clock.ElapsedMilliseconds >= nextReport)
            {
                nextReport += 500;
                analyzer.CopyBands(bands);
                double rmsDb = samples > 0 ? 10 * Math.Log10(Math.Max(sumSq / samples, 1e-18)) : double.NegativeInfinity;
                Console.WriteLine($"t={clock.ElapsedMilliseconds,5}ms samples peak {Db(peak),6:F1} dBFS rms {rmsDb,6:F1} dBFS | meter peak {Db(meterPeak),6:F1} dBFS | bands {string.Join(",", bands.Select(v => (int)Math.Round(v)))}");
                peak = 0; sumSq = 0; samples = 0; meterPeak = 0;
            }
            Thread.Sleep(10);
        }
        client.Stop();
        Marshal.FreeCoTaskMem(pFormat);
        return 0;
    }

    private static double Db(double v) => 20 * Math.Log10(Math.Max(v, 1e-9));

    private static void Check(int hr, string what)
    {
        if (hr != 0) throw new Exception($"{what} failed 0x{hr:X8}");
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, out IntPtr iface);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr store);
        [PreserveSig] int GetId(out IntPtr id);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid context);
        [PreserveSig] int VolumeStepDown(ref Guid context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incDb);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long def, out long min);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, out IntPtr service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
