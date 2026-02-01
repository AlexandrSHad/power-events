# Phase 2: Backend Server Structured Logging

## Overview

Implement Serilog-based structured logging for the ASP.NET Core backend (`server.cs`) with Seq integration and correlation ID propagation.

## Scope

- Replace `builder.Services.AddLogging(builder => builder.AddConsole())` with Serilog
- Extract correlation ID from incoming MQTT messages
- Propagate correlation ID to SSE clients
- Log MQTT subscription lifecycle, SSE connections, and message flow

## Breaking Changes

The `PowerEventData` class requires property name updates for JSON serialization consistency:

| Current Property | Required Change |
|------------------|-----------------|
| `State` | Rename to `state` OR add `[JsonPropertyName("state")]` |
| `TimeGenerated` | Rename to `timeGenerated` OR add `[JsonPropertyName("timeGenerated")]` |

**Recommended approach**: Use `[JsonPropertyName]` attributes to maintain C# naming conventions while ensuring JSON compatibility with downstream consumers.

## Key Components

### Sinks

| Sink | Environment |
|------|-------------|
| Console (structured) | Development |
| File (JSON) | Production |
| Seq | If configured |

### Serilog Request Logging Middleware

Add `app.UseSerilogRequestLogging()` to the middleware pipeline for automatic HTTP request/response logging:

```csharp
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("ClientIP", httpContext.Connection.RemoteIpAddress);
    };
});
```

### SSE Client Lifecycle Logging

Track SSE client connections and disconnections with active client count:

```csharp
_logger.LogInformation("SSE client connected. Active clients: {ClientCount}", _clients.Count);
_logger.LogInformation("SSE client disconnected. Active clients: {ClientCount}", _clients.Count);
```

### Correlation ID Flow

```
MQTT Message (contains correlationId)
    ↓
MqttSubscriberService extracts correlationId
    ↓
LogContext.PushProperty("CorrelationId", correlationId)
    ↓
SSE broadcast includes correlationId
```

**Extraction and Propagation**:

```csharp
// In MQTT message handler
var payload = JsonSerializer.Deserialize<PowerEventPayload>(message);
using (LogContext.PushProperty("CorrelationId", payload?.CorrelationId))
{
    _logger.LogInformation("Received power event: {State}", payload?.State);
    // Process and forward to SSE clients
}
```

### Log Points

- MQTT connection/disconnection
- Message received from MQTT topic
- SSE client connected/disconnected (with client count)
- Message pushed to SSE stream
- Health check requests
- Correlation ID extraction success/failure

## Packages

Same as Phase 1 (Serilog ecosystem)

## Log Forwarding Endpoint

### POST /api/logs

Endpoint for web client log forwarding to centralized Seq server.

**Request Format**:

```json
{
  "level": "Information",
  "message": "User clicked refresh button",
  "timestamp": "2026-02-01T10:30:00Z",
  "properties": {
    "component": "Dashboard",
    "userId": "anonymous",
    "correlationId": "abc-123"
  }
}
```

**Implementation Considerations**:

- Validate log level (Verbose, Debug, Information, Warning, Error, Fatal)
- Extract and validate `correlationId` from properties
- Forward to Seq using Serilog's `Log.ForContext()` API
- Return 202 Accepted (fire-and-forget semantics)

**Rate Limiting**:

- Implement rate limiting to prevent log flooding
- Suggested: 100 requests per minute per client IP
- Use ASP.NET Core rate limiting middleware or custom implementation
- Return 429 Too Many Requests when limit exceeded

```csharp
app.MapPost("/api/logs", async (LogEntry entry, ILogger<Program> logger) =>
{
    using (LogContext.PushProperty("Source", "WebClient"))
    using (LogContext.PushProperty("CorrelationId", entry.Properties?.CorrelationId))
    {
        logger.Log(entry.LogLevel, entry.Message);
    }
    return Results.Accepted();
}).RequireRateLimiting("logs");
```

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `backend/server.cs` | Modify | Replace AddLogging with Serilog, add UseSerilogRequestLogging, update SSE lifecycle logging |
| `backend/PowerEventData.cs` | Modify | Add JsonPropertyName attributes for property serialization |
| `backend/MqttSubscriberService.cs` | Modify | Add correlation ID extraction and LogContext propagation |
| `backend/appsettings.json` | Modify | Add Serilog and Seq configuration sections |
| `backend/backend.csproj` | Modify | Add Serilog NuGet package references |

## Configuration

Reuse similar `appsettings.json` structure with Seq configuration.
