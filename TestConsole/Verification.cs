using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using AudioLevelMeterPlugin.Models;
using AudioLevelMeterPlugin.Services;

/// <summary>
/// Spectrum verification: `--selftest` (no audio device) and `--live` (loopback of the default output).
/// </summary>
static class Verification
{
    // winmm PlaySound (SND_FILENAME | SND_ASYNC) plays the WAV without extra package references
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);
    private const uint SND_ASYNC = 0x1, SND_FILENAME = 0x20000;

    private const int Bands = 32;
    private const float MinHz = 30f, MaxHz = 16000f, FloorDb = -80f;

    public static int Run(string[] args)
    {
        return args[0] switch
        {
            "--selftest" => SelfTest(),
            "--live" => Live(),
            _ => Usage()
        };
    }

    private static int Usage()
    {
        Console.WriteLine("Usage: TestConsole [--containers [--inputs] | --selftest | --live | --noise | --diagnose <device-id-part>]");
        return 2;
    }

    private static (int Band, float Value) Peak(float[] bands)
    {
        int best = 0;
        for (int i = 1; i < bands.Length; i++) if (bands[i] > bands[best]) best = i;
        return (best, bands[best]);
    }

    private static int SelfTest()
    {
        const int sampleRate = 48000;
        bool ok = true;
        var buffer = new float[Bands];

        foreach (double freq in new[] { 100.0, 1000.0, 5000.0 })
        {
            int expected = SpectrumAnalyzer.BandForFrequency(freq, Bands, MinHz, MaxHz);
            // Where a band is narrower than two FFT bins (bin = 23.4 Hz at 48 kHz) a tone can only
            // be placed to within one band; elsewhere the band must match exactly
            double bandWidthHz = freq * (Math.Pow(MaxHz / MinHz, 1.0 / Bands) - 1);
            int tolerance = bandWidthHz < 2.0 * sampleRate / SpectrumAnalyzer.FftSize ? 1 : 0;
            float fullValue = 0;
            foreach (var (label, amp) in new[] { ("0 dBFS", 1.0), ("-20 dBFS", 0.1) })
            {
                var analyzer = new SpectrumAnalyzer(sampleRate, Bands, MinHz, MaxHz, FloorDb);
                var samples = new float[16384];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = (float)(amp * Math.Sin(2 * Math.PI * freq * i / sampleRate));
                analyzer.Push(samples);
                analyzer.CopyBands(buffer);
                var (band, value) = Peak(buffer);
                bool match = Math.Abs(band - expected) <= tolerance;
                if (!match) ok = false;
                string extra = "";
                if (amp == 1.0) fullValue = value;
                else
                {
                    // 100 points span 80 dB, so 1 point = 0.8 dB
                    double diffDb = (fullValue - value) * 0.8;
                    bool diffOk = Math.Abs(diffDb - 20.0) <= 1.5;
                    if (!diffOk) ok = false;
                    extra = $"  lower by {diffDb:F1} dB {(diffOk ? "OK" : "MISMATCH")}";
                }
                Console.WriteLine($"{freq,6} Hz {label,8}: peak band {band,2} (value {value:F1}), expected band {expected,2} (tolerance {tolerance}) {(match ? "OK" : "MISMATCH")}{extra}");
            }
        }

        var silent = new SpectrumAnalyzer(sampleRate, Bands, MinHz, MaxHz, FloorDb);
        silent.Push(new float[16384]);
        silent.CopyBands(buffer);
        bool allZero = buffer.All(v => v == 0f);
        if (!allZero) ok = false;
        Console.WriteLine($"silence: all {Bands} bands zero = {allZero}");

        // Decay: a tone that stops must fall to zeros
        var decaying = new SpectrumAnalyzer(sampleRate, Bands, MinHz, MaxHz, FloorDb);
        var tone = new float[8192];
        for (int i = 0; i < tone.Length; i++) tone[i] = (float)Math.Sin(2 * Math.PI * 1000.0 * i / sampleRate);
        decaying.Push(tone);
        int steps = 0;
        do { decaying.Decay(); decaying.CopyBands(buffer); steps++; } while (buffer.Any(v => v != 0f) && steps < 100);
        bool decayed = buffer.All(v => v == 0f);
        if (!decayed) ok = false;
        Console.WriteLine($"decay: zeros after {steps} steps = {decayed}");

        // Endpoint volume: loopback samples arrive after a software volume of -42.3 dB (6 % on the slider);
        // the compensation gain must bring a -6 dBFS tone back to where it reads at full volume
        float gain = LoopbackCaptureService.CompensationGain(-42.3f, 0.06f, false, false);
        float[] ReadTone(float scale)
        {
            var analyzer = new SpectrumAnalyzer(sampleRate, Bands, MinHz, MaxHz, FloorDb);
            var samples = new float[16384];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 1000.0 * i / sampleRate)) * scale;
            analyzer.Push(samples);
            var result = new float[Bands];
            analyzer.CopyBands(result);
            return result;
        }
        float attenuation = (float)Math.Pow(10, -42.3 / 20);
        var (_, reference) = Peak(ReadTone(1f));
        var (_, attenuated) = Peak(ReadTone(attenuation));
        var (_, compensated) = Peak(ReadTone(attenuation * gain));
        bool compensatedOk = Math.Abs(compensated - reference) <= 1f;
        if (!compensatedOk) ok = false;
        Console.WriteLine($"volume -42.3 dB: reference {reference:F1}, attenuated {attenuated:F1}, compensated {compensated:F1} {(compensatedOk ? "OK" : "MISMATCH")}");

        bool passThrough = LoopbackCaptureService.CompensationGain(-42.3f, 0.06f, true, false) == 1f
            && LoopbackCaptureService.CompensationGain(-42.3f, 0.06f, false, true) == 1f
            && LoopbackCaptureService.CompensationGain(-96f, 0f, false, false) == 1f
            && LoopbackCaptureService.CompensationGain(0f, 1f, false, false) == 1f;
        if (!passThrough) ok = false;
        Console.WriteLine($"volume muted / hardware / zero / full: gain 1 = {passThrough}");

        Console.WriteLine(ok ? "SELFTEST PASS" : "SELFTEST FAIL");
        return ok ? 0 : 1;
    }

    private static int Live()
    {
        string wavPath = Path.Combine(Path.GetTempPath(), "audiometer-1khz.wav");
        WriteTone(wavPath, 1000.0, 0.5, 8, 44100);
        int expected = SpectrumAnalyzer.BandForFrequency(1000.0, Bands, MinHz, MaxHz);

        var settings = new SpectrumSettings();
        using var service = new LoopbackCaptureService(settings);
        service.AddTarget("default-audio", null);
        service.Start();

        Thread.Sleep(500);
        Console.WriteLine($"before tone: {service.GetSpectrum("default-audio")}");

        PlaySound(wavPath, IntPtr.Zero, SND_FILENAME | SND_ASYNC);

        int bestBand = -1;
        float bestValue = 0;
        for (int i = 0; i < 5; i++)
        {
            Thread.Sleep(1000);
            string spectrum = service.GetSpectrum("default-audio");
            var values = spectrum.Length == 0 ? Array.Empty<float>() : spectrum.Split(',').Select(float.Parse).ToArray();
            string peak = "no data";
            if (values.Length == Bands)
            {
                var (band, value) = Peak(values);
                peak = $"peak band {band} = {value:F0} (expected {expected})";
                if (value > bestValue) { bestValue = value; bestBand = band; }
            }
            Console.WriteLine($"t={i + 1}s {peak}\n  {spectrum}");
        }

        PlaySound(null, IntPtr.Zero, 0);
        Thread.Sleep(600);
        Console.WriteLine($"after tone: {service.GetSpectrum("default-audio")}");
        try { File.Delete(wavPath); } catch { }

        bool ok = bestValue > 0 && Math.Abs(bestBand - expected) <= 1;
        Console.WriteLine(ok ? $"LIVE PASS (best band {bestBand}, value {bestValue:F0})" : "LIVE FAIL (no 1 kHz peak seen)");
        return ok ? 0 : 1;
    }

    private static void WriteTone(string path, double freq, double amp, int seconds, int rate)
    {
        int count = seconds * rate;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF".ToCharArray());
        w.Write(36 + count * 2);
        w.Write("WAVEfmt ".ToCharArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data".ToCharArray());
        w.Write(count * 2);
        for (int i = 0; i < count; i++)
            w.Write((short)(amp * short.MaxValue * Math.Sin(2 * Math.PI * freq * i / rate)));
    }
}
