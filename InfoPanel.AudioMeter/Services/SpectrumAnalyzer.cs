using System;

namespace AudioLevelMeterPlugin.Services
{
    /// <summary>
    /// Turns a mono sample stream into log-spaced spectrum bands (0..100).
    /// Every 1024 samples a 2048-point Hann-windowed FFT runs; nothing allocates after construction.
    /// Not thread-safe: one thread pushes and reads.
    /// </summary>
    public sealed class SpectrumAnalyzer
    {
        public const int FftSize = 2048;
        public const int Hop = 1024;
        private const float Release = 0.6f;

        private readonly float[] _ring = new float[FftSize];
        private readonly float[] _window = new float[FftSize];
        private readonly float[] _cos = new float[FftSize / 2];
        private readonly float[] _sin = new float[FftSize / 2];
        private readonly int[] _bitReverse = new int[FftSize];
        private readonly float[] _re = new float[FftSize];
        private readonly float[] _im = new float[FftSize];
        private readonly float[] _mag = new float[FftSize / 2];
        private readonly int[] _bandLo;
        private readonly int[] _bandHi;
        private readonly float[] _bands;
        private readonly float _floorDb;
        private readonly float _scale;
        private int _writePos;
        private int _sinceHop;

        public int BandCount => _bands.Length;

        public SpectrumAnalyzer(int sampleRate, int bandCount, float minHz, float maxHz, float floorDb)
        {
            _floorDb = floorDb;
            _bands = new float[bandCount];
            _bandLo = new int[bandCount];
            _bandHi = new int[bandCount];

            double windowSum = 0;
            for (int i = 0; i < FftSize; i++)
            {
                _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FftSize));
                windowSum += _window[i];
            }
            // A full-scale sine centred on a bin reads 1.0 (0 dBFS)
            _scale = (float)(2.0 / windowSum);

            for (int i = 0; i < FftSize / 2; i++)
            {
                _cos[i] = (float)Math.Cos(2 * Math.PI * i / FftSize);
                _sin[i] = (float)-Math.Sin(2 * Math.PI * i / FftSize);
            }

            int bits = (int)Math.Log2(FftSize);
            for (int i = 0; i < FftSize; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++)
                    if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                _bitReverse[i] = r;
            }

            double binHz = (double)sampleRate / FftSize;
            double ratio = maxHz / minHz;
            for (int b = 0; b < bandCount; b++)
            {
                double lo = minHz * Math.Pow(ratio, (double)b / bandCount);
                double hi = minHz * Math.Pow(ratio, (double)(b + 1) / bandCount);
                int loBin = (int)Math.Ceiling(lo / binHz);
                int hiBin = (int)Math.Ceiling(hi / binHz) - 1;
                if (hiBin < loBin)
                {
                    // Narrower than one bin: take the nearest bin to the band centre
                    loBin = hiBin = (int)Math.Round(Math.Sqrt(lo * hi) / binHz);
                }
                loBin = Math.Clamp(loBin, 1, FftSize / 2 - 1);
                hiBin = Math.Clamp(hiBin, loBin, FftSize / 2 - 1);
                _bandLo[b] = loBin;
                _bandHi[b] = hiBin;
            }
        }

        /// <summary>
        /// Index of the band that contains the given frequency (for tests and tooling).
        /// </summary>
        public static int BandForFrequency(double hz, int bandCount, double minHz, double maxHz)
        {
            double t = Math.Log(hz / minHz) / Math.Log(maxHz / minHz);
            return Math.Clamp((int)Math.Floor(t * bandCount), 0, bandCount - 1);
        }

        /// <summary>Feed mono samples; returns how many hops (FFTs) ran.</summary>
        public int Push(ReadOnlySpan<float> samples)
        {
            int hops = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                _ring[_writePos] = samples[i];
                _writePos = (_writePos + 1) & (FftSize - 1);
                if (++_sinceHop == Hop)
                {
                    _sinceHop = 0;
                    ProcessHop();
                    hops++;
                }
            }
            return hops;
        }

        /// <summary>Copy the current bands (0..100) into dest.</summary>
        public void CopyBands(Span<float> dest) => _bands.AsSpan().CopyTo(dest);

        /// <summary>Release one step towards silence when no audio is arriving.</summary>
        public void Decay()
        {
            for (int b = 0; b < _bands.Length; b++)
            {
                float v = _bands[b] * Release;
                _bands[b] = v < 0.5f ? 0f : v;
            }
        }

        private void ProcessHop()
        {
            // _writePos is the oldest sample in the ring
            for (int i = 0; i < FftSize; i++)
            {
                int dst = _bitReverse[i];
                _re[dst] = _ring[(_writePos + i) & (FftSize - 1)] * _window[i];
                _im[dst] = 0f;
            }

            for (int size = 2; size <= FftSize; size <<= 1)
            {
                int half = size >> 1;
                int step = FftSize / size;
                for (int start = 0; start < FftSize; start += size)
                {
                    for (int k = 0, t = 0; k < half; k++, t += step)
                    {
                        int a = start + k;
                        int b = a + half;
                        float wr = _cos[t], wi = _sin[t];
                        float tr = _re[b] * wr - _im[b] * wi;
                        float ti = _re[b] * wi + _im[b] * wr;
                        _re[b] = _re[a] - tr;
                        _im[b] = _im[a] - ti;
                        _re[a] += tr;
                        _im[a] += ti;
                    }
                }
            }

            for (int k = 0; k < FftSize / 2; k++)
                _mag[k] = MathF.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) * _scale;

            for (int b = 0; b < _bands.Length; b++)
            {
                float max = 0f;
                for (int k = _bandLo[b]; k <= _bandHi[b]; k++)
                    if (_mag[k] > max) max = _mag[k];

                float db = 20f * MathF.Log10(MathF.Max(max, 1e-9f));
                float level = Math.Clamp((db - _floorDb) / -_floorDb, 0f, 1f) * 100f;
                float prev = _bands[b];
                // Attack instant, mild release
                _bands[b] = level >= prev ? level : prev * Release + level * (1f - Release);
            }
        }
    }
}
