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
            retainedFileCountLimit: 7,
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

**Before**:
```json
{
  "State": "Awake",
  "TimeGenerated": "2026-01-20T10:15:30Z"
}
```

**After**:
```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "state": "Awake",
  "timeGenerated": "2026-01-20T10:15:30Z",
  "source": {
    "machine": "DESKTOP-ABC123",
    "service": "PowerEvents.WindowsService"
  }
}
```

### Structured Log Points

Replace string interpolation with structured properties:

| Current | Updated |
|---------|---------|
| `$"Connected to MQTT at {host}:{port}"` | `"Connected to MQTT broker at {Host}:{Port}", host, port` |
| `$"Published to {topic}"` | `"Published message to MQTT topic {Topic}", topic` |
| `$"CPU {cpu}%, RAM {ram}%"` | `"System metrics collected: CPU={CpuPercent}%, RAM={RamPercent}%", cpu, ram` |

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

## Open Source Considerations

- Works out-of-box without Seq (console + file logging)
- Seq is opt-in via configuration
- Clear documentation for enabling Seq
- Graceful degradation if Seq is unreachable (logs to file, no crash)
