using System;
using System.Collections.Generic;
using System.Linq;
using AudioLevelMeterPlugin.Models;

namespace AudioLevelMeterPlugin.Services
{
    /// <summary>
    /// Advanced audio level calculator implementing OBS-style audio processing
    /// </summary>
    public class AdvancedAudioCalculator
    {
        private readonly Queue<float[]> _sampleHistory;
        private readonly int _historySize;
        private readonly AudioMeterConfiguration _configuration;
        private float _peakHold;
        private DateTime _peakHoldTime;
        private float _currentDecayValue;
        
        public AdvancedAudioCalculator(AudioMeterConfiguration? configuration = null)
        {
            _configuration = configuration ?? new AudioMeterConfiguration();
            _historySize = _configuration.CalculatorHistoryMs / (int)_configuration.UpdateInterval.TotalMilliseconds;
            _sampleHistory = new Queue<float[]>();
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
            
            // Debug logging
            if (channelPeaks.Any(p => p > 0f))
            {
                Console.WriteLine($"AdvancedAudioCalculator: Input peaks: [{string.Join(", ", channelPeaks.Select(p => p.ToString("F4")))}]");
            }
            
            // Calculate instantaneous peak across all channels
            float instantaneousPeak = 0f;
            float sumSquares = 0f;
            
            foreach (float channelPeak in channelPeaks)
            {
                float abs = Math.Abs(channelPeak);
                instantaneousPeak = Math.Max(instantaneousPeak, abs);
                sumSquares += abs * abs;
            }
            
            // RMS calculation (magnitude level)
            float rms = channelPeaks.Length > 0 ? (float)Math.Sqrt(sumSquares / channelPeaks.Length) : 0f;
            
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
                levels.LeftChannel = ScaleToPercentage(channelPeaks[0]);
                levels.RightChannel = ScaleToPercentage(channelPeaks[1]);
            }
            else if (channelPeaks.Length == 1)
            {
                // Mono - duplicate to both channels
                levels.LeftChannel = levels.RightChannel = ScaleToPercentage(channelPeaks[0]);
            }
            
            // Backward compatibility percentage using RMS for smoother response
            levels.PercentageLevel = DbToPercentage(levels.MagnitudeLevel);
            
            // Apply decay to percentage level for existing behavior
            levels.PercentageLevel = Math.Max(levels.PercentageLevel, _currentDecayValue * _configuration.LevelDecayRate);
            _currentDecayValue = levels.PercentageLevel;
            
            // Store history for future enhancements
            _sampleHistory.Enqueue(channelPeaks);
            if (_sampleHistory.Count > _historySize)
                _sampleHistory.Dequeue();
            
            return levels;
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
        /// Scale individual channel to percentage using existing ultra-responsive algorithm
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
            _sampleHistory.Clear();
        }
    }
}