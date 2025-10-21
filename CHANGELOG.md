# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Features to be added in future versions

### Changed
- Changes to be made in future versions

### Deprecated
- Features that will be removed in future versions

### Removed
- Features removed in future versions

### Fixed
- Bug fixes for future versions

### Security
- Security improvements for future versions

## [1.1.0] - 2025-10-21

### Added
- **Device Name Customization** - Users can now customize audio device display names via INI configuration file
- **Automatic INI File Management** - Plugin creates and maintains device configuration file automatically
- **Custom Name Persistence** - Device name customizations persist across plugin restarts and InfoPanel sessions
- **Enhanced Configuration Loading** - Improved initialization sequence to load custom names before container creation
- **Dynamic Device Name Updates** - Real-time application of custom names from configuration file

### Changed
- **Improved VU Meter Scaling** - Enhanced scaling algorithm for more realistic music response (full volume music now consistently hits 95-100%)
- **Enhanced Initialization Process** - Restructured plugin initialization to properly handle custom device names
- **Better Error Handling** - Improved COM marshaling error recovery and device enumeration stability
- **Optimized Container Creation** - Device containers now created with custom names from the start
- **Enhanced Logging Output** - More detailed diagnostic messages for configuration loading and device processing

### Fixed
- **INI File Population Issue** - Fixed timing issue where INI file was created before devices were enumerated
- **Custom Name Application** - Fixed issue where custom names weren't being applied to containers after restart
- **Configuration Timing** - Resolved initialization order problems that prevented custom names from loading properly
- **Container Name Updates** - Fixed device name entries not updating with custom names from configuration
- **Memory Management** - Improved COM object disposal patterns for better stability

## [1.0.0] - 2025-10-21

### Added
- Initial release of InfoPanel Audio Meter Plugin with comprehensive multi-device support
- **Multi-device audio monitoring** support for all available Windows output devices
- **Real-time audio level monitoring** with Windows Core Audio API implementation
- **Enhanced VU meter functionality** with ultra-responsive scaling algorithm
- **Professional scaling curves** that make music at 100% volume consistently hit 95-100% on meters
- **Dynamic default device tracking** that follows Windows audio device changes automatically  
- **Individual device containers** with unique device identification and friendly names
- **Enhanced device container pattern** with separate containers per audio device
- **Multi-channel audio support** (averages left/right channels for stereo devices)
- **High refresh rate monitoring** (50ms update intervals / 20 FPS for smooth VU meters)
- **Intelligent device caching** with proper COM object lifecycle management
- **Serilog structured logging** integration for comprehensive diagnostics and debugging
- **Professional build system** with automatic ZIP packaging and deployment
- **Comprehensive plugin lifecycle management** (`Initialize()`, `Load()`, `UpdateAsync()`, `Close()`)
- **Graceful error handling** for missing audio devices and COM marshaling issues
- **Advanced resource management** with proper disposal patterns and memory cleanup

### Changed
- **Major architecture upgrade** from single-device to multi-device audio monitoring
- **Replaced NAudio dependency** with direct Windows Core Audio API P/Invoke implementation
- **Enhanced container structure** expanded from single device to comprehensive multi-device pattern
- **Optimized VU meter scaling** with aggressive curves for realistic music response (0.25-0.4 raw → 90-99% display)
- **Improved device management** with robust caching, enumeration, and disposal patterns
- **Advanced COM interop** using CoCreateInstance for reliable Windows audio system access

### Fixed
- **COM object casting errors** that prevented NAudio from working with InfoPanel's audio integration
- **Device enumeration crashes** caused by improper COM marshaling and pointer handling
- **Memory leaks** in device caching through proper IDisposable implementation
- **VU meter scaling issues** where full-volume music only reached 30-90% instead of 95-100%
- **Device identification problems** with safe string marshaling from COM interfaces
- **Plugin stability issues** through comprehensive error handling and resource cleanup

### Technical Details
- **Platform**: Built on .NET 8.0-windows with unsafe COM interop support
- **Audio System**: Direct Windows Core Audio API integration via P/Invoke and CoCreateInstance
- **COM Interfaces**: `IMMDeviceEnumerator`, `IMMDeviceCollection`, `IMMDevice`, `IAudioMeterInformation`
- **Device Detection**: Advanced multi-device enumeration with proper COM object lifecycle management
- **Level Reading**: Real-time peak value monitoring with enhanced VU meter scaling algorithms
- **Architecture**: InfoPanel plugin container system with multi-device sensor data exposure
- **Memory Management**: Comprehensive IDisposable patterns with safe COM object cleanup
- **Error Handling**: Robust exception handling for COM marshaling and audio system failures
- **Naming Conventions**: Kebab-case ID conventions (`audio-level-meter`, `default-audio-meter`)
- **Scaling Algorithm**: Ultra-responsive curves designed for realistic music VU meter behavior

### Documentation
- Comprehensive README.md with installation and usage instructions
- Detailed copilot instructions for AI development assistance
- InfoPanel plugin development guidelines
- Logging best practices and structured message templates
- Build and deployment documentation

### Build System
- **Professional MSBuild configuration** with advanced packaging and deployment
- **Automatic dependency flattening** and cleanup for streamlined distribution
- **Release ZIP packaging** with versioned directories and proper file organization
- **PDB and debug symbol management** for production debugging capabilities
- **Platform-specific optimizations** (x64) for Windows audio system compatibility
- **Advanced deployment pipeline** with automatic InfoPanel plugin directory deployment
- **Satellite resource optimization** (English only) for reduced package size
- **COM interop compilation** with unsafe code blocks and Windows-specific targeting

---

## Version History Format

This changelog follows these conventions:

- **Added** for new features
- **Changed** for changes in existing functionality  
- **Deprecated** for soon-to-be removed features
- **Removed** for now removed features
- **Fixed** for any bug fixes
- **Security** for vulnerability fixes

Each version entry includes:
- Version number following [Semantic Versioning](https://semver.org/)
- Release date in YYYY-MM-DD format
- Categorized changes with detailed descriptions
- Technical implementation details where relevant
- Breaking changes clearly marked

## Links

- [InfoPanel Project](https://github.com/habibrehmansg/infopanel)
- [NAudio Library](https://github.com/naudio/NAudio)
- [Keep a Changelog](https://keepachangelog.com/)
- [Semantic Versioning](https://semver.org/)