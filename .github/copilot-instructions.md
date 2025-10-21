# InfoPanel Audio Meter Plugin - AI Coding Guide

## Project Overview
This is an **InfoPanel plugin** that provides real-time system audio output level monitoring. It's a .NET 8.0 C# library that integrates with the InfoPanel ecosystem to display audio VU meter data as sensor readings.

## Architecture & Key Components

### Plugin Architecture Pattern
- **Base Class**: All plugins inherit from `InfoPanel.Plugins.BasePlugin`
- **Required Overrides**: `Initialize()`, `Load()`, `UpdateAsync()`, `Close()`, `UpdateInterval`, `ConfigFilePath`
- **Plugin Identity**: Constructor takes `(id, name, description)` - use kebab-case for IDs
- **Container System**: Plugins expose data through `PluginContainer` objects containing `PluginSensor` entries

### Core Implementation Pattern
```csharp
// Plugin structure follows this pattern:
public class MyPlugin : BasePlugin
{
    private PluginSensor _sensor;
    
    public MyPlugin() : base("plugin-id", "Display Name", "Description")
    {
        _sensor = new PluginSensor("sensor-id", "Sensor Name", defaultValue, "unit");
    }
    
    public override TimeSpan UpdateInterval => TimeSpan.FromMilliseconds(50); // For smooth real-time data
    
    public override void Load(List<IPluginContainer> containers)
    {
        var container = new PluginContainer("container-id", "Container Name");
        container.Entries.Add(_sensor);
        containers.Add(container);
    }
}
```

## Audio-Specific Implementation Details

### Multi-Device NAudio Integration
- Uses `NAudio.CoreAudioApi` for Windows audio system access
- Enumerate devices: `_deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)`
- Get default device: `new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)`
- Read peak levels: `device.AudioMeterInformation.PeakValues` (returns left/right channel array)
- Device caching: Maintain `Dictionary<string, MMDevice>` for device instances
- Device disposal: Properly dispose all cached devices in `Close()` method

### VU Meter Behavior
- **Decay Algorithm**: Apply `_currentValue = Math.Max(peakValue, _currentValue * 0.85f)` for realistic falloff
- **Update Frequency**: 50ms intervals for smooth visual metering
- **Value Scaling**: Convert 0.0-1.0 peak values to 0-100% for display
- **Channel Averaging**: Average left/right channels: `(leftPeak + rightPeak) / 2`
- **Per-Device Decay**: Each device maintains independent decay values in `Dictionary<string, float>`

### Multi-Device Container Pattern
- **Default Container**: Special container (`default-audio-meter`) that tracks Windows default device
- **Device Containers**: Individual containers per device with pattern `audio-meter-{deviceId}`
- **Device Information**: Include `PluginText` entries for device names and friendly names
- **Dynamic Default Tracking**: Default container follows device changes automatically

## Logging Guidelines
 - Use **Serilog** for all logging. Each class should declare its own logger:
     ```csharp
     private static readonly ILogger Logger = Log.ForContext<ClassName>();
     ```
 - Log at appropriate levels: Debug (diagnostics), Information (lifecycle/events), Warning (abnormal but non-fatal), Error (handled exceptions), Fatal (critical failures).
 - Always use structured logging (message templates with parameters):
     ```csharp
     Logger.Information("Device {DeviceId} connected at {Timestamp}", deviceId, DateTime.Now);
     ```
 - Include relevant context (device IDs, plugin names, etc.) in log messages.
 - Pass exceptions as the first parameter when logging errors:
     ```csharp
     Logger.Error(ex, "Failed to perform operation for device {DeviceId}", deviceId);
     ```
 - Avoid logging sensitive information (passwords, API keys, personal data).
 - Use consistent message formats and parameter names across the codebase.


## Development Conventions

### Error Handling
- Use try-catch in `Initialize()` and `UpdateAsync()` 
- Log errors to Console but don't crash the plugin system
- Gracefully handle missing audio devices or transient audio API failures

### Resource Management
- Dispose audio devices in `Close()` method
- Set disposed objects to null after disposal
- Check for null and cancellation tokens in `UpdateAsync()`

### Project Structure & C# Best Practices
- Organize code into `Models/` and `Services/` folders where appropriate:
    - **Models**: Define data structures, DTOs, and plugin data types.
    - **Services**: Encapsulate business logic, audio device access, or reusable operations.
- Single plugin per solution/project.
- `PluginInfo.ini` contains metadata (Name, Description, Author, Version, Website).
- Main plugin class goes in namespace matching the purpose, not the assembly name.

## Build & Development

### Dependencies
- **.NET 8.0** with nullable reference types enabled
- **NAudio 2.2.1** for audio system integration
- **InfoPanel.Plugins** framework (external dependency)

### Build Commands
```powershell
dotnet restore                  # Restore packages
dotnet build                    # Build solution
dotnet build -c Release        # Release build for deployment
```

### Plugin Testing
- Use `InfoPanel.Plugins.Simulator` for isolated plugin testing
- Run: `dotnet run --project InfoPanel.Plugins.Simulator/InfoPanel.Plugins.Simulator.csproj`
- Plugin simulator loads plugins independently from main InfoPanel app

### Plugin Deployment
- Built assemblies go to InfoPanel plugin directory
- `PluginInfo.ini` must accompany the plugin DLL  
- Target framework must match InfoPanel host requirements

## Critical Implementation Details

### Async vs Sync Updates
- **Never implement** `Update()` method - throw `NotImplementedException()`
- **Always use** `UpdateAsync()` for data collection and sensor updates
- InfoPanel calls `UpdateAsync()` based on `UpdateInterval` property

### Container and Sensor IDs
- Use **kebab-case** for all IDs: `"audio-level-meter"`, `"audio-level"`
- Container IDs group related sensors in InfoPanel UI
- Sensor IDs must be unique within each container

### Data Flow Pattern
```csharp
Initialize() → Load(containers) → UpdateAsync() loop → Close()
```

## Key Files
- `InfoPanel.AudioMeter.cs` - Main plugin implementation
- `PluginInfo.ini` - Plugin metadata for InfoPanel discovery
- `InfoPanel.AudioMeter.csproj` - Project configuration with NAudio dependency
- `_docs/` - Contains InfoPanel ecosystem documentation