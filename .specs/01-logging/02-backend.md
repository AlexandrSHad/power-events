# Phase 2: Backend Server Structured Logging

## Overview

Implement Serilog-based structured logging for the ASP.NET Core backend (`server.cs`) with Seq integration and correlation ID propagation.

## Scope

- Replace `builder.Services.AddLogging(builder => builder.AddConsole())` with Serilog
- Extract correlation ID from incoming MQTT messages
- Propagate correlation ID to SSE clients
- Log MQTT subscription lifecycle, SSE connections, and message flow

## Key Components

### Sinks

| Sink | Environment |
|------|-------------|
| Console (structured) | Development |
| File (JSON) | Production |
| Seq | If configured |

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

### Log Points

- MQTT connection/disconnection
- Message received from MQTT topic
- SSE client connected/disconnected
- Message pushed to SSE stream
- Health check requests

## Packages

Same as Phase 1 (Serilog ecosystem)

## Configuration

Reuse similar `appsettings.json` structure with Seq configuration.
