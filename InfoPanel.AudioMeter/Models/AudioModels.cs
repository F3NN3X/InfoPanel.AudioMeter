using System;

namespace AudioLevelMeterPlugin.Models
{
    /// <summary>
    /// Advanced audio level data structure containing peak, RMS, and peak hold values
    /// </summary>
    public class AudioLevels
    {
        public float PeakLevel { get; set; }      // Instantaneous peak level in dB
        public float MagnitudeLevel { get; set; } // RMS/average level in dB
        public float PeakHold { get; set; }       // Peak hold indicator in dB
        public float LeftChannel { get; set; }   // Left channel level (percentage)
        public float RightChannel { get; set; }  // Right channel level (percentage)
        public float PercentageLevel { get; set; } // Backward compatibility percentage
        
        public AudioLevels()
        {
            PeakLevel = -96f;      // Professional audio -96dB floor
            MagnitudeLevel = -96f;
            PeakHold = -96f;
            LeftChannel = 0f;
            RightChannel = 0f;
            PercentageLevel = 0f;
        }
    }

    /// <summary>
    /// Represents an audio device with its metadata
    /// </summary>
    public class AudioDeviceInfo
    {
        public string DeviceId { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string CustomName { get; set; } = string.Empty;
        public string ContainerId { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        
        /// <summary>
        /// Gets the display name (custom name if available, otherwise friendly name)
        /// </summary>
        public string DisplayName => !string.IsNullOrWhiteSpace(CustomName) ? CustomName : FriendlyName;
    }

    /// <summary>
    /// Configuration settings for the audio level meter
    /// </summary>
    public class AudioMeterConfiguration
    {
        public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromMilliseconds(50);
        public int PeakHoldDurationMs { get; set; } = 1000;
        public float PeakHoldDecayRate { get; set; } = 0.99f;
        public float LevelDecayRate { get; set; } = 0.85f;
        public int CalculatorHistoryMs { get; set; } = 300;
        
        /// <summary>
        /// Professional audio floor level in dB
        /// </summary>
        public float AudioFloorDb { get; set; } = -96f;
        
        /// <summary>
        /// dB level that maps to 100% for percentage scaling
        /// </summary>
        public float AudioCeilingDb { get; set; } = -20f;

        /// <summary>
        /// Spectrum band settings (the [Spectrum] section of the ini)
        /// </summary>
        public SpectrumSettings Spectrum { get; } = new SpectrumSettings();

        /// <summary>
        /// How levels map to percent (the [Meter] section of the ini)
        /// </summary>
        public MeterSettings Meter { get; } = new MeterSettings();
    }

    /// <summary>
    /// Level scale settings (the [Meter] section of the ini)
    /// </summary>
    public class MeterSettings
    {
        public const string DbScale = "db";
        public const string LegacyScale = "legacy";

        /// <summary>
        /// "db": percent is linear in dB, FloorDb reads 0% and 0 dBFS 100%.
        /// "legacy": the curve before 1.4.0, which reads 90% at -12 dBFS and 99% from -8 dBFS.
        /// </summary>
        public string Scale { get; set; } = DbScale;

        /// <summary>
        /// The level that reads 0% on the dB scale
        /// </summary>
        public float FloorDb { get; set; } = -60f;
    }

    /// <summary>
    /// Settings for the loopback spectrum bands
    /// </summary>
    public class SpectrumSettings
    {
        public bool Enabled { get; set; } = true;
        public int Bands { get; set; } = 32;
        public float MinHz { get; set; } = 30f;
        public float MaxHz { get; set; } = 16000f;
        public float FloorDb { get; set; } = -80f;
    }
}