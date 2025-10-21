# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Upcoming features and improvements will be listed here

### Changed
- Future changes will be documented here

### Deprecated
- Features that will be removed in future versions

### Removed
- Features removed in future versions

### Fixed
- Bug fixes for future versions

### Security
- Security improvements for future versions

## [1.0.0] - 2025-10-21

### Added
- Initial release of InfoPanel Audio Meter Plugin
- Real-time audio level monitoring using NAudio library
- VU meter functionality with realistic decay algorithm (0.85f falloff)
- Multi-channel audio support (averages left/right channels)
- High refresh rate monitoring (50ms update intervals / 20 FPS)
- Percentage-based audio level display (0-100%)
- Automatic default audio device detection
- InfoPanel plugin architecture integration
- Comprehensive plugin lifecycle management (`Initialize()`, `Load()`, `UpdateAsync()`, `Close()`)
- Graceful error handling for missing audio devices
- Resource management with proper disposal patterns
- Structured logging support with Serilog integration
- Professional build system with automatic ZIP packaging
- Clean dependency management and output optimization

### Technical Details
- Built on .NET 8.0 targeting Windows platform
- NAudio 2.2.1 for Windows audio system access
- Uses `MMDeviceEnumerator` for audio device detection
- Implements `AudioMeterInformation.PeakValues` for level reading
- Plugin container system with sensor data exposure
- Kebab-case ID conventions (`audio-level-meter`, `audio-level`)

### Documentation
- Comprehensive README.md with installation and usage instructions
- Detailed copilot instructions for AI development assistance
- InfoPanel plugin development guidelines
- Logging best practices and structured message templates
- Build and deployment documentation

### Build System
- Professional MSBuild configuration
- Automatic dependency flattening and cleanup
- Release ZIP packaging with versioned directories
- PDB and debug symbol management
- Platform-specific build optimizations (x64)
- Satellite resource language limiting (English only)

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