using System;
using System.Collections.Generic;
using AudioLevelMeterPlugin.Models;

namespace AudioLevelMeterPlugin.Services
{
    /// <summary>
    /// Advanced audio level calculator implementing OBS-style audio processing
    /// </summary>
    public class AdvancedAudioCalculator
    {
        private readonly float[][] _sampleHistory; // ring buffer of preallocated slots
        private readonly int _historySize;
        private int _historyHead;
        private int _historyCount;
        private readonly AudioMeterConfiguration _configuration;
        private float _peakHold;
        private DateTime _peakHoldTime;
        private float _currentDecayValue;
        
        public AdvancedAudioCalculator(AudioMeterConfiguration? configuration = null)
        {
            _configuration = configuration ?? new AudioMeterConfiguration();
            _historySize = Math.Max(0, _configuration.CalculatorHistoryMs / (int)_configuration.UpdateInterval.TotalMilliseconds);
            _sampleHistory = new float[_historySize][];
            _peakHold = 0f;
            _peakHoldTime = DateTime.Now;
            _currentDecayValue = 0f;
        }
        
        /// <summary>
        /// Calculate advanced audio levels from channel peak values
        /// </summary>
        public AudioLevels CalculateAdvancedLevels(float[] channelPeaks)
        {
            var levels = new AudioLevels();
            
            if (channelPeaks == null || channelPeaks.Length == 0)
                return levels;
            
            // Calculate instantaneous peak across all channels
            float instantaneousPeak = 0f;
            foreach (float channelPeak in channelPeaks)
            {
                instantaneousPeak = Math.Max(instantaneousPeak, Math.Abs(channelPeak));
            }

            // History first: the RMS below averages it
            // (copied into a reused slot: the caller's array is reused between ticks)
            if (_historySize > 0)
            {
                var slot = _sampleHistory[_historyHead];
                if (slot == null || slot.Length != channelPeaks.Length)
                {
                    slot = new float[channelPeaks.Length];
                    _sampleHistory[_historyHead] = slot;
                }
                Array.Copy(channelPeaks, slot, channelPeaks.Length);
                _historyHead = (_historyHead + 1) % _historySize;
                if (_historyCount < _historySize) _historyCount++;
            }

            // RMS (magnitude level) of the channel peaks over the last CalculatorHistoryMs. The meter API
            // gives peaks, not samples, so this is a short loudness average that moves slower than the peak.
            float rms = WindowRms(channelPeaks);
            
            // Peak hold logic (like OBS)
            if (instantaneousPeak > _peakHold)
            {
                _peakHold = instantaneousPeak;
                _peakHoldTime = DateTime.Now;
            }
            else if ((DateTime.Now - _peakHoldTime).TotalMilliseconds > _configuration.PeakHoldDurationMs)
            {
                _peakHold = Math.Max(0f, _peakHold * _configuration.PeakHoldDecayRate);
            }
            
            // Convert to dB scale
            levels.PeakLevel = AmplitudeToDecibels(instantaneousPeak);
            levels.MagnitudeLevel = AmplitudeToDecibels(rms);
            levels.PeakHold = AmplitudeToDecibels(_peakHold);
            
            // Per-channel analysis (stereo)
            if (channelPeaks.Length >= 2)
            {
                levels.LeftChannel = ToPercent(channelPeaks[0]);
                levels.RightChannel = ToPercent(channelPeaks[1]);
            }
            else if (channelPeaks.Length == 1)
            {
                // Mono - duplicate to both channels
                levels.LeftChannel = levels.RightChannel = ToPercent(channelPeaks[0]);
            }
            
            // Overall level on the same scale as the channels (the stereo peak, max of L/R)
            levels.PercentageLevel = ToPercent(instantaneousPeak);
            
            // Apply decay to percentage level for existing behavior
            levels.PercentageLevel = Math.Max(levels.PercentageLevel, _currentDecayValue * _configuration.LevelDecayRate);
            _currentDecayValue = levels.PercentageLevel;
            
            return levels;
        }

        /// <summary>
        /// Root mean square of every channel peak in the history, or of the current peaks without one
        /// </summary>
        private float WindowRms(float[] current)
        {
            double sum = 0;
            int n = 0;
            if (_historyCount == 0)
            {
                foreach (float p in current) { sum += p * p; n++; }
            }
            else
            {
                for (int i = 0; i < _historyCount; i++)
                {
                    foreach (float p in _sampleHistory[i]) { sum += p * p; n++; }
                }
            }
            return n > 0 ? (float)Math.Sqrt(sum / n) : 0f;
        }

        /// <summary>
        /// A peak amplitude (0.0-1.0) as a meter percentage on the configured scale
        /// </summary>
        private float ToPercent(float amplitude)
        {
            var meter = _configuration.Meter;
            if (meter.Scale == MeterSettings.LegacyScale) return ScaleToPercentage(amplitude);
            if (amplitude <= 0f) return 0f;
            float db = 20f * (float)Math.Log10(amplitude);
            return Math.Clamp((db - meter.FloorDb) / -meter.FloorDb * 100f, 0f, 100f);
        }
        
        /// <summary>
        /// Convert amplitude (0.0-1.0) to decibels using professional audio formula
        /// </summary>
        private float AmplitudeToDecibels(float amplitude)
        {
            if (amplitude <= 0f) return _configuration.AudioFloorDb;
            return 20f * (float)Math.Log10(amplitude);
        }
        
        /// <summary>
        /// Convert dB level to percentage for backward compatibility
        /// </summary>
        private float DbToPercentage(float dbLevel)
        {
            // Map audio floor to 0% and ceiling to 100%
            float normalizedDb = Math.Max(_configuration.AudioFloorDb, Math.Min(_configuration.AudioCeilingDb, dbLevel));
            float dbRange = _configuration.AudioCeilingDb - _configuration.AudioFloorDb;
            return ((normalizedDb - _configuration.AudioFloorDb) / dbRange) * 100f;
        }
        
        /// <summary>
        /// The legacy scale ([Meter] Scale = legacy): reads 90% at -12 dBFS and 99% from -8 dBFS
        /// </summary>
        private static float ScaleToPercentage(float channelValue)
        {
            if (channelValue <= 0f) return 0f;
            
            // Use existing ultra-responsive scaling
            float scaledValue;
            
            if (channelValue <= 0.01f)
            {
                scaledValue = channelValue * 300f;
            }
            else if (channelValue <= 0.05f)
            {
                scaledValue = 3f + (channelValue - 0.01f) * 450f;
            }
            else if (channelValue <= 0.15f)
            {
                scaledValue = 21f + (channelValue - 0.05f) * 390f;
            }
            else if (channelValue <= 0.25f)
            {
                scaledValue = 60f + (channelValue - 0.15f) * 300f;
            }
            else if (channelValue <= 0.4f)
            {
                scaledValue = 90f + (channelValue - 0.25f) * 60f;
            }
            else
            {
                scaledValue = 99f + (channelValue - 0.4f) * 1.67f;
            }
            
            return Math.Min(scaledValue, 100f);
        }

        /// <summary>
        /// Reset the peak hold state
        /// </summary>
        public void ResetPeakHold()
        {
            _peakHold = 0f;
            _peakHoldTime = DateTime.Now;
        }

        /// <summary>
        /// Reset all internal state
        /// </summary>
        public void Reset()
        {
            _peakHold = 0f;
            _peakHoldTime = DateTime.Now;
            _currentDecayValue = 0f;
            _historyHead = 0;
            _historyCount = 0;
        }
    }
}