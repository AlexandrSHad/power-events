# Phase 1: Windows Service Structured Logging

## Overview

Implement Serilog-based structured logging for the Windows Service (`power-events-service.cs`) with environment-aware sink configuration and optional Seq integration.

## Requirements

### Sink Strategy

| Sink | Service Mode | Interactive Mode |
|------|--------------|------------------|
| **Console** | Disabled | Enabled |
| **File** | Enabled | Disabled |
| **Windows Event Log** | Enabled (Error/Fatal only) | Disabled |
| **Seq** | If configured | If configured |

### Rationale

- **Service Mode**: No console available; use File for persistent logs, Event Log for critical errors (standard Windows practice)
- **Interactive Mode**: Developer watches console; no file clutter needed
- **Seq**: Optional for open source compatibility; power users enable via config

## Technical Specification

### NuGet Packages

```xml
<PackageReference Include="Serilog.Extensions.Hosting" />
<PackageReference Include="Serilog.Sinks.Console" />
<PackageReference Include="Serilog.Sinks.File" />
<PackageReference Include="Serilog.Sinks.EventLog" />
<PackageReference Include="Serilog.Sinks.Seq" />
<PackageReference Include="Serilog.Enrichers.Environment" />
<PackageReference Include="Serilog.Enrichers.Thread" />
<PackageReference Include="Serilog.Enrichers.Process" />
```

### Configuration Schema

**appsettings.json**:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  },
  "Logging": {
    "FilePath": "%ProgramData%/PowerEvents/logs/power-events-.log",
    "EventLogSource": "PowerEvents"
  },
  "Seq": {
    "Enabled": false,
    "ServerUrl": "http://localhost:5341",
    "ApiKey": null
  }
}
```

### Sink Configuration Logic

```csharp
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithProcessId()
    .Enrich.WithThreadId()
    .Enrich.WithProperty("Application", "PowerEvents.WindowsService")
    .WriteTo.Conditional(
        _ => Environment.UserInteractive,
        wt => wt.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"))
    .WriteTo.Conditional(
        _ => !Environment.UserInteractive,
        wt => wt.File(
            path: logFilePath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 2,
            formatter: new CompactJsonFormatter()))
    .WriteTo.Conditional(
        _ => !Environment.UserInteractive,
        wt => wt.EventLog(
            source: "PowerEvents",
            restrictedToMinimumLevel: LogEventLevel.Error))
    .WriteTo.Conditional(
        _ => seqEnabled,
        wt => wt.Seq(seqServerUrl, apiKey: seqApiKey))
    .CreateLogger();
```

### Log Locations

| Mode | Sink | Location |
|------|------|----------|
| Service | File | `%ProgramData%\PowerEvents\logs\power-events-YYYYMMDD.log` |
| Service | Event Log | Windows Event Viewer → Application → Source: "PowerEvents" |
| Service | Seq | Configured URL (default: `http://localhost:5341`) |
| Interactive | Console | Terminal output |

### Correlation ID Implementation

Generate correlation ID when detecting power events:

```csharp
public class PowerEventsBackgroundService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // On power event detection:
        var correlationId = Guid.NewGuid().ToString();

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            _logger.LogInformation("Power event detected: {State}", state);

            // Include correlationId in MQTT payload
            var payload = new PowerEventPayload
            {
                CorrelationId = correlationId,
                State = state,
                TimeGenerated = timeGenerated
            };

            await _mqttPublisher.PublishAsync("power-events", payload);
        }
    }
}
```

### MQTT Payload Schema Update

This must be discussed once again. Probably CorrelationId and Timestamp already exist in MQTT message metadata or there is a way enabling that.

### Structured Log Points

Replace string interpolation with structured properties:

| Current | Updated |
|---------|---------|
| `$"Connected to MQTT at {host}:{port}"` | `"Connected to MQTT broker at {Host}:{Port}", host, port` |
| `$"Published to {topic}"` | `"Published message to MQTT topic {Topic}", topic` |
| `$"CPU {cpu}%, RAM {ram}%"` | `"System metrics collected: CPU={CpuPercent}%, RAM={RamPercent}%, CorrelationId={CorrelationId}", cpu, ram, correlationId` |

### Error Handling

```csharp
try
{
    await PublishToMqttAsync(payload);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to publish to MQTT topic {Topic}. CorrelationId={CorrelationId}",
        topic, correlationId);
}
```

## File Changes

| File | Changes |
|------|---------|
| `power-events-service.cs` | Add Serilog configuration, correlation ID generation, structured log calls |
| `power-events-service.csproj` | Add Serilog NuGet packages |
| `appsettings.json` (new) | Configuration for sinks, Seq settings |
| `appsettings.Development.json` (new) | Development overrides |

## Verification

1. **Interactive Mode**: Run `dotnet run` → verify console output with structured format
2. **Service Mode**: Install and run as service → verify:
   - Logs appear in `%ProgramData%\PowerEvents\logs\`
   - Errors appear in Windows Event Viewer
3. **Seq Integration**: Enable Seq → verify logs appear in Seq UI with correlation IDs
4. **Correlation Flow**: Trigger power event → verify same correlationId in MQTT payload

## Breaking Changes

The MQTT payload schema changes from PascalCase to camelCase property naming. This affects all downstream consumers:

| Consumer | Required Update |
|----------|-----------------|
| **Backend** | Update SSE payload mapping to expect camelCase properties (`state`, `timeGenerated`, `cpuPercent`, `ramPercent`) |
| **ESP32** | Update ArduinoJson deserialization keys to camelCase |
| **Web Client** | Update JavaScript/Vue property access to camelCase (may already be compatible if using JSON.parse) |

**Migration**: All consumers must be updated before deploying the Windows Service changes to avoid parsing failures.

## Open Source Considerations

- Works out-of-box without Seq (console + file logging)
- Seq is opt-in via configuration
- Clear documentation for enabling Seq
- Graceful degradation if Seq is unreachable (logs to file, no crash)
