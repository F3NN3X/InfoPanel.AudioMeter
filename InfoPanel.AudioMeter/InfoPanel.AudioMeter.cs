using InfoPanel.Plugins;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace AudioLevelMeterPlugin
{
    public class AudioLevelMeter : BasePlugin
    {
        private MMDevice? _defaultDevice;
        private PluginSensor _audioLevel;
        private float _currentValue = 0f; // For decay effect

        public AudioLevelMeter() : base("audio-level-meter", "Audio Level Meter", "Provides real-time audio output level metering")
        {
            _audioLevel = new PluginSensor("audio-level", "Audio Level", 0f, "%");
        }

        public override string? ConfigFilePath => null;

        public override TimeSpan UpdateInterval => TimeSpan.FromMilliseconds(50); // Smooth updates for metering

        public override void Initialize()
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                _defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch (Exception ex)
            {
                // Log or handle error (e.g., no audio device)
                Console.WriteLine($"AudioLevelMeter Initialize error: {ex.Message}");
            }
        }

        public override void Load(List<IPluginContainer> containers)
        {
            var container = new PluginContainer("audio-meter", "Audio Meter");
            container.Entries.Add(_audioLevel);
            containers.Add(container);
        }

        public override async Task UpdateAsync(CancellationToken cancellationToken)
        {
            if (_defaultDevice == null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var peakValues = _defaultDevice.AudioMeterInformation.PeakValues;
                float leftPeak = peakValues.Count > 0 ? peakValues[0] : 0f;
                float rightPeak = peakValues.Count > 1 ? peakValues[1] : leftPeak;
                float averagePeak = (leftPeak + rightPeak) / 2;

                // Apply decay for realistic VU meter (adjust 0.85f for fall-off speed)
                _currentValue = Math.Max(averagePeak, _currentValue * 0.85f);

                _audioLevel.Value = _currentValue * 100f;
            }
            catch (Exception ex)
            {
                // Ignore transient errors, log if needed
                Console.WriteLine($"AudioLevelMeter Update error: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        public override void Update()
        {
            throw new NotImplementedException();
        }

        public override void Close()
        {
            _defaultDevice?.Dispose();
            _defaultDevice = null;
        }
    }
}