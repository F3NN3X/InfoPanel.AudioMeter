# InfoPanel Audio Meter Plugin

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0--windows-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Windows Core Audio API](https://img.shields.io/badge/Windows%20Core%20Audio%20API-Direct%20P%2FInvoke-green.svg)](https://docs.microsoft.com/en-us/windows/win32/coreaudio/core-audio-apis-in-windows-vista)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A real-time audio level monitoring plugin for [InfoPanel](https://github.com/InfoPanel-Project/InfoPanel) that provides VU meter functionality with responsive scaling curves for all audio output devices. Uses direct Windows Core Audio API integration for reliable performance.

## Features

- 🎵 **Multi-Device Audio Monitoring** - Tracks audio levels from all available Windows output devices
- 🎛️ **Professional Audio Processing** - OBS-style dual level calculation with peak, RMS, and peak hold indicators
- 📊 **Professional dB Scale** - True audio engineering decibel readings (-96dB to 0dB) alongside familiar percentage display
- 🔊 **Per-Channel Analysis** - Separate left/right channel monitoring with stereo analysis
- � **Peak Hold Indicators** - Visual peak hold markers like professional audio equipment
- �📊 **Responsive VU Meters** - Scaling curves that make music at full volume reach 95-100% on meters
- 🌈 **Spectrum Bands** - Real frequency bands of what each device is playing (WASAPI loopback + FFT), exposed as the `spectrum` entry
- 🎚️ **Device Name Customization** - Edit INI file to customize how device names appear in InfoPanel
- � **Backward Compatibility** - All existing percentage sensors preserved for seamless upgrades
- 🎛️ **Default Device Tracking** - Automatically follows Windows default audio device changes
- ⚡ **Smooth Updates** - 50ms update intervals (20 FPS) for smooth visual metering
- 🎯 **Multi-Level Display** - Peak, RMS, peak hold, channels, and percentage all available simultaneously
- 🏷️ **Device Information** - Shows friendly names, device names, and device identification
- 🔌 **InfoPanel Integration** - Works with InfoPanel's plugin architecture and multi-container support
- 🛠️ **Windows API Integration** - Uses Windows Core Audio API via P/Invoke for reliable performance
- 💾 **Memory Management** - Proper COM object lifecycle management with disposal patterns
- 🔍 **Logging** - Serilog integration for diagnostics and monitoring

## Installation

### Prerequisites

- [InfoPanel](https://github.com/InfoPanel-Project/InfoPanel) application
- Windows operating system (Windows Core Audio API required)
- .NET 8.0 Runtime (Windows-specific version)

### Plugin Installation

1. Download the latest release from the [Releases](../../releases) page
2. Extract the plugin files to your InfoPanel plugins directory:
   ```
   InfoPanel/plugins/InfoPanel.AudioMeter/
   ├── InfoPanel.AudioMeter.dll
   └── PluginInfo.ini
   ```
3. Restart InfoPanel to load the plugin

> **Note**: No external dependencies required. The plugin uses direct Windows Core Audio API integration via P/Invoke.

## Usage

Once installed, the Audio Meter plugin will appear in InfoPanel's sensor list with multiple containers:

- **Default Audio Meter** - Tracks the Windows default audio device with automatic switching
  - Device name and audio level (0-100% with responsive scaling)
- **Audio Meter - [Device Name]** - Individual containers for each available audio device
  - Device friendly name, device name, and real-time audio level with VU meter behavior
- **Default Input Meter** (`default-input`) - Tracks the Windows default input device (only when `IncludeInputs` is on, see below)
- **Update Rate**: 20 FPS (50ms intervals) for smooth metering

### Sensors

Every container (`default-audio`, `default-input` and each `audio-meter-<id>`) has the same entries:

| Entry | Type | Value |
| --- | --- | --- |
| `data-flow` | text | `output` for a playback device, `input` for a recording device (`default-audio` is `output`, `default-input` is `input`) |
| `device-name` | text | The device's display name |
| `audio-level`, `left-channel`, `right-channel` | sensor, % | Level on the [meter scale](#meter-scale) |
| `peak-level`, `rms-level`, `peak-hold` | sensor, dB | Peak, short RMS average and peak hold |
| `spectrum` | text | Frequency bands, see [Spectrum](#spectrum) |

### Input devices

Input devices (microphones, line in) are metered too, but they are off by default. Windows' level meter for a recording device only reads while some application has a capture stream open on it. Measured on a dev machine with nothing else capturing: the meter of every recording device read 0 with no stream, and a Focusrite interface went from 0 to a live level once a stream was opened. So metering an input means the plugin opens a normal shared-mode capture stream on each input, and Windows shows the microphone-in-use indicator for as long as the plugin runs. That is why inputs are opt-in:

```ini
[Devices]
IncludeInputs = false
```

Set `IncludeInputs = true` and restart InfoPanel to get a container per recording device plus `default-input`. Each input runs a shared-mode capture stream, because Windows only meters a recording device while one is open; Windows shows the microphone-in-use indicator while it runs. With `[Spectrum] Enabled = true` their `spectrum` is computed from that stream, not from loopback. Output devices are unaffected.

### Meter Scale

`audio-level`, `left-channel` and `right-channel` are percentages on a dB scale: `FloorDb` reads 0% and 0 dBFS (full scale) reads 100%, linearly in dB. With the default floor of -60 dB, -6 dBFS reads 90%, -12 dBFS 80% and -30 dBFS 50%, so loud music keeps moving instead of sitting at the top. Left and right are the device's real channel peaks.

`rms-level` is the RMS of the channel peaks over the last 300 ms: Windows' meter gives peaks, not samples, so it is a short loudness average that moves slower than `peak-level`.

Configure it in the `[Meter]` section of the ini (defaults shown). `Scale = legacy` brings back the curve from before 1.4.0, which reads 90% at -12 dBFS and 99% from -8 dBFS:

```ini
[Meter]
Scale = db
FloorDb = -60
```

Restart InfoPanel to apply changes.

### Spectrum

Every container has a `spectrum` text entry with the live frequency bands of the audio playing on that device: comma-separated integers 0..100, lowest band first, no spaces (for example `0,12,40,...`), exactly `Bands` values. It is all zeros when nothing plays and an empty string when the spectrum is disabled or the device failed (a failed device is retried every 5 seconds). In a dashboard it is `plugins.audio-level-meter.<container>.spectrum`.

Bands are log-spaced between `MinHz` and `MaxHz` and use the device's real sample rate; 0 is `FloorDb` or quieter and 100 is full scale. Configure it in the `[Spectrum]` section of the ini (defaults shown):

```ini
[Spectrum]
Enabled = true
Bands = 32
MinHz = 30
MaxHz = 16000
FloorDb = -80
```

`Bands` is clamped to 8..64. Restart InfoPanel to apply changes.

### Device Name Customization

After the first run, the plugin creates an INI configuration file where you can customize device names:

**Location:** `C:\ProgramData\InfoPanel\plugins\InfoPanel.AudioMeter\InfoPanel.AudioMeter.dll.ini`

**Example:**
```ini
[DeviceNames]
{device-guid-1} = Studio Monitors
{device-guid-2} = Gaming Headset  
{device-guid-3} = Streaming Audio
```

Restart InfoPanel to apply custom names.

The plugin automatically detects all available audio output devices and provides real-time monitoring for each device independently. When you change your default audio device in Windows, the "Default Audio Meter" container will automatically switch to monitor the new device with zero configuration required.

## Development

### Building from Source

```powershell
# Clone the repository
git clone https://github.com/F3NN3X/InfoPanel.AudioMeter.git
cd InfoPanel.AudioMeter

# Restore dependencies
dotnet restore

# Build the project
dotnet build -c Release

# Output will be in InfoPanel.AudioMeter/bin/Release/net8.0-windows/InfoPanel.AudioMeter-v<version>/InfoPanel.AudioMeter/
```

### Testing

Use the InfoPanel Plugin Simulator for isolated testing:

```powershell
dotnet run --project InfoPanel.Plugins.Simulator/InfoPanel.Plugins.Simulator.csproj
```

### Project Structure

```
InfoPanel.AudioMeter/
├── InfoPanel.AudioMeter/
│   ├── InfoPanel.AudioMeter.cs     # Main plugin implementation
│   └── InfoPanel.AudioMeter.csproj # Project configuration
├── PluginInfo.ini                  # Plugin metadata
├── _docs/                          # Documentation
└── README.md                       # This file
```

## Technical Details

### Multi-Device Audio Processing

- **API Integration**: Direct Windows Core Audio API via P/Invoke and CoCreateInstance
- **COM Interfaces**: `IMMDeviceEnumerator`, `IMMDeviceCollection`, `IMMDevice`, `IAudioMeterInformation`
- **Device Discovery**: Enumeration of all active audio render endpoints with error handling
- **Device Management**: Device caching with COM object lifecycle management
- **Memory Safety**: Proper disposal patterns and safe COM marshaling to prevent memory leaks
- **Default Device Tracking**: Monitoring that follows Windows default device changes
- **Channel Processing**: Per-channel peaks from `GetChannelsPeakValues`, falling back to the overall peak
- **VU Meter Algorithms**: 
  - **Decay**: `Math.Max(peakValue, currentValue * 0.85f)` for realistic falloff behavior
  - **Scaling**: Linear in dB from `FloorDb` to 0 dBFS (`[Meter] Scale = legacy` for the old multi-tier curve)
- **Error Recovery**: Exception handling for COM marshaling failures and audio system issues

### Device Container Structure

Each audio device gets its own container with:
- **Device Information**: Friendly names, device names, and unique device identification
- **Audio Level Sensor**: Real-time level monitoring with responsive scaling (0-100%)
- **Independent VU Meter State**: Each device maintains its own decay values and scaling algorithms
- **Device Tracking**: Automatic detection of device changes, additions, and removals
- **COM Integration**: Proper device object caching with safe disposal patterns

### Logging Integration

- **Serilog**: Structured logging with class-specific loggers
- **Device Events**: Logs device initialization, errors, and lifecycle events
- **Performance**: Debug-level logging for audio level updates

### Plugin Architecture

Inherits from `InfoPanel.Plugins.BasePlugin` with the following lifecycle:

```csharp
Initialize() → Load(containers) → UpdateAsync() loop → Close()
```

## Configuration

No configuration required - the plugin automatically detects and monitors your default audio output device.

## Troubleshooting

### Common Issues

**Plugin not loading:**
- Ensure InfoPanel.Plugins framework is available
- Check that PluginInfo.ini is present alongside the DLL
- Verify .NET 8.0 runtime is installed

**No audio data:**
- Confirm default audio device is set correctly in Windows Sound settings
- Check Windows audio permissions and ensure InfoPanel has audio access
- Ensure audio is actually playing and the device is active
- Verify Windows Core Audio API is available (Windows Vista+)

**Performance issues:**
- Plugin uses minimal CPU with 50ms update intervals
- Device caching reduces COM overhead
- Audio device disposal happens automatically on plugin shutdown
- Direct Windows API integration provides reliable performance

**VU meter scaling issues:**
- The plugin uses scaling curves designed for realistic music response
- Music at 100% volume should consistently hit 95-100% on the meters
- If levels seem low, ensure Windows volume levels are properly set
- The scaling is optimized for typical music content (not pure sine waves)

**Device name customization:**
- Edit the INI file in the plugin directory to customize device names
- Restart InfoPanel after making changes to apply new names
- Delete the INI file to reset to default device names

## Releasing

1. Bump `<Version>` in `InfoPanel.AudioMeter/InfoPanel.AudioMeter.csproj` and `Version` in `InfoPanel.AudioMeter/PluginInfo.ini`, and add a `CHANGELOG.md` entry (`## [X.Y.Z] - yyyy-MM-dd`).
2. Commit, then push a tag: `git tag vX.Y.Z && git push origin vX.Y.Z`.

The *Release Build* workflow (`.github/workflows/release.yml`) builds the plugin, checks the tag matches `<Version>`, and publishes `InfoPanel.AudioMeter-vX.Y.Z.zip` to the GitHub release with the changelog section as notes. It can also be run manually (workflow_dispatch) to build without releasing.
## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

Please follow the [InfoPanel plugin development guidelines](.github/copilot-instructions.md) and use structured logging with Serilog.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [InfoPanel Project](https://github.com/InfoPanel-Project/InfoPanel) - The extensible hardware monitoring platform
- [Microsoft Windows Core Audio API](https://docs.microsoft.com/en-us/windows/win32/coreaudio/core-audio-apis-in-windows-vista) - Direct Windows audio system integration
- VU meter scaling algorithms inspired by broadcast and recording industry equipment
- COM interop patterns based on Windows audio development best practices

## Version History

- **1.4.0** - 🎚️ **Meter scale** - Levels on a dB scale that keeps moving with loud music, real left/right peaks, an RMS that averages the last 300 ms, a `[Meter]` ini section, and the host process no longer leaks memory
- **1.3.0** - 🌈 **Spectrum** - New `spectrum` entry with real frequency bands from WASAPI loopback capture, and a `[Spectrum]` ini section
- **1.2.0** - 🎛️ **Professional Audio Monitoring** - Added OBS-style dual level processing (peak + RMS), professional dB scale readings, per-channel analysis, and peak hold indicators while maintaining full backward compatibility
- **1.1.0** - Added device name customization via INI file, improved VU meter scaling for realistic music response, enhanced error handling and logging
- **1.0.0** - Initial release with multi-device monitoring, responsive VU meter scaling, and direct Windows Core Audio API integration

---

For more information about InfoPanel plugin development, see the [plugin documentation](_docs/InfoPanel_PluginDocumentation.md).