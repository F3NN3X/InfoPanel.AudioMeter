# InfoPanel Audio Meter Plugin

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0--windows-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Windows Core Audio API](https://img.shields.io/badge/Windows%20Core%20Audio%20API-Direct%20P%2FInvoke-green.svg)](https://docs.microsoft.com/en-us/windows/win32/coreaudio/core-audio-apis-in-windows-vista)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A professional real-time audio level monitoring plugin for [InfoPanel](https://github.com/InfoPanel-Project/InfoPanel) that provides ultra-responsive VU meter functionality with realistic scaling curves for all audio output devices. Features direct Windows Core Audio API integration for maximum performance and compatibility.

## Features

- 🎵 **Advanced Multi-Device Audio Monitoring** - Tracks audio levels from all available Windows output devices simultaneously
- 📊 **Ultra-Responsive VU Meters** - Professional scaling curves that make music at 100% volume consistently hit 95-100% on meters
- 🎚️ **Realistic Scaling Algorithm** - Optimized curves that make typical music content (0.25-0.4 raw) display as 90-99% for authentic VU meter behavior
- 🔊 **Multi-channel Support** - Intelligently averages left and right audio channels per device
- 🎛️ **Dynamic Default Device Tracking** - Automatically follows Windows default audio device changes in real-time
- ⚡ **High-Performance Refresh Rate** - 50ms update intervals (20 FPS) for buttery-smooth visual metering
- 🎯 **Professional Percentage Display** - Advanced scaling converts raw audio peaks to realistic 0-100% VU meter readings
- 🏷️ **Comprehensive Device Information** - Shows friendly names, device names, and unique device identification
- 🔌 **Native InfoPanel Integration** - Seamless plugin architecture with multi-container support
- 🛠️ **Direct Windows API Integration** - Uses Windows Core Audio API via P/Invoke for maximum performance and reliability
- 💾 **Advanced Memory Management** - Proper COM object lifecycle management with comprehensive disposal patterns
- 🔍 **Structured Logging** - Serilog integration with detailed diagnostics and performance monitoring

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

> **Note**: No external dependencies required! The plugin uses direct Windows Core Audio API integration via P/Invoke for maximum performance and compatibility.

## Usage

Once installed, the Audio Meter plugin will appear in InfoPanel's sensor list with multiple containers:

- **Default Audio Meter** - Dynamically tracks the Windows default audio device with automatic switching
  - Device name and ultra-responsive audio level (0-100% with professional scaling)
- **Audio Meter - [Device Name]** - Individual containers for each available audio device
  - Device friendly name, device name, and real-time audio level with realistic VU meter behavior
- **Update Rate**: 20 FPS (50ms intervals) for smooth, professional-grade metering

### Advanced VU Meter Behavior

The plugin features **ultra-responsive scaling curves** specifically designed for realistic music monitoring:

- **Typical Music Content**: Raw audio signals around 0.25-0.4 (normal loud music) display as **90-99%** on the meters
- **Full Volume Music**: Music at 100% system volume consistently hits **95-100%** on the VU meters  
- **Professional Scaling**: Multi-tier algorithm that mimics real audio equipment VU meter response
- **Dynamic Range**: Quiet sounds (0.01-0.05 raw) scale to 3-21%, moderate sounds (0.05-0.15 raw) scale to 21-60%

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

# Output will be in InfoPanel.AudioMeter/bin/Release/net8.0/
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

### Advanced Multi-Device Audio Processing

- **API Integration**: Direct Windows Core Audio API via P/Invoke and CoCreateInstance for maximum performance
- **COM Interfaces**: `IMMDeviceEnumerator`, `IMMDeviceCollection`, `IMMDevice`, `IAudioMeterInformation`
- **Device Discovery**: Advanced enumeration of all active audio render endpoints with robust error handling
- **Device Management**: Intelligent device caching with comprehensive COM object lifecycle management
- **Memory Safety**: Proper disposal patterns and safe COM marshaling to prevent memory leaks
- **Default Device Tracking**: Real-time monitoring that dynamically follows Windows default device changes
- **Channel Processing**: Sophisticated averaging of left/right peak values for optimal mono output per device
- **VU Meter Algorithms**: 
  - **Decay**: `Math.Max(peakValue, currentValue * 0.85f)` for realistic falloff behavior
  - **Ultra-Responsive Scaling**: Professional multi-tier curves that make music hit 95-100% consistently
- **Error Recovery**: Comprehensive exception handling for COM marshaling failures and audio system issues

### Enhanced Device Container Structure

Each audio device gets its own professionally structured container with:
- **Comprehensive Device Information**: Friendly names, device names, and unique device identification
- **Ultra-Responsive Audio Level Sensor**: Real-time level monitoring with professional scaling (0-100%)
- **Independent VU Meter State**: Each device maintains its own decay values and scaling algorithms
- **Dynamic Device Tracking**: Automatic detection of device changes, additions, and removals
- **Memory-Safe COM Integration**: Proper device object caching with safe disposal patterns

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
- Plugin uses minimal CPU with optimized 50ms update intervals
- Advanced device caching reduces COM overhead
- Audio device disposal happens automatically on plugin shutdown
- Direct Windows API integration provides maximum performance

**VU meter scaling issues:**
- The plugin uses professional scaling curves designed for realistic music response
- Music at 100% volume should consistently hit 95-100% on the meters
- If levels seem low, ensure Windows volume levels are properly set
- The scaling is optimized for typical music content (not pure sine waves)

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
- Professional VU meter scaling algorithms inspired by broadcast and recording industry equipment
- COM interop patterns based on Windows audio development best practices

## Version History

- **1.0.0** - Professional release with advanced multi-device monitoring, ultra-responsive VU meter scaling, and direct Windows Core Audio API integration

---

For more information about InfoPanel plugin development, see the [plugin documentation](_docs/InfoPanel_PluginDocumentation.md).