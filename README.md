# InfoPanel Audio Meter Plugin

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![NAudio](https://img.shields.io/badge/NAudio-2.2.1-green.svg)](https://github.com/naudio/NAudio)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A real-time audio level monitoring plugin for [InfoPanel](https://github.com/InfoPanel-Project/InfoPanel) that provides VU meter functionality with smooth decay effects.

## Features

- 🎵 **Real-time Audio Monitoring** - Tracks system audio output levels in real-time
- 📊 **VU Meter with Decay** - Realistic falloff algorithm for smooth visual metering  
- 🔊 **Multi-channel Support** - Averages left and right audio channels
- ⚡ **High Refresh Rate** - 50ms update intervals for fluid animations
- 🎯 **Percentage Display** - Converts audio levels to 0-100% scale
- 🔌 **InfoPanel Integration** - Seamless plugin architecture compatibility

## Installation

### Prerequisites

- [InfoPanel](https://github.com/InfoPanel-Project/InfoPanel) application
- Windows operating system (NAudio dependency)
- .NET 8.0 Runtime

### Plugin Installation

1. Download the latest release from the [Releases](../../releases) page
2. Extract the plugin files to your InfoPanel plugins directory:
   ```
   InfoPanel/plugins/InfoPanel.AudioMeter/
   ├── InfoPanel.AudioMeter.dll
   ├── PluginInfo.ini
   └── NAudio.dll
   ```
3. Restart InfoPanel to load the plugin

## Usage

Once installed, the Audio Meter plugin will appear in InfoPanel's sensor list:

- **Container**: Audio Meter
- **Sensor**: Audio Level (0-100%)
- **Update Rate**: 20 FPS (50ms intervals)

The plugin automatically detects your default audio output device and begins monitoring audio levels immediately.

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

### Audio Processing

- **Library**: NAudio.CoreAudioApi for Windows audio system access
- **Device Detection**: Automatically uses default multimedia audio endpoint
- **Channel Processing**: Averages left/right peak values for mono output
- **Decay Algorithm**: `Math.Max(peakValue, currentValue * 0.85f)` for realistic VU meter behavior

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
- Confirm default audio device is set correctly in Windows
- Check Windows audio permissions
- Ensure audio is actually playing

**Performance issues:**
- Plugin uses minimal CPU with 50ms update intervals
- Audio device disposal happens automatically on plugin shutdown

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
- [NAudio](https://github.com/naudio/NAudio) - .NET audio library providing Windows audio system access
- Audio VU meter algorithms inspired by professional audio equipment

## Version History

- **1.0.0** - Initial release with real-time audio monitoring and VU meter decay

---

For more information about InfoPanel plugin development, see the [plugin documentation](_docs/InfoPanel_PluginDocumentation.md).