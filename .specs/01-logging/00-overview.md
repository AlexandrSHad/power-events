# Structured Logging & Issue Tracing

## Overview

Implement transparent issue tracing across all Power Events components using structured logging with correlation IDs. Logs flow to a centralized Seq instance for unified querying and analysis.

## Goals

- **Transparent Tracing**: Track events end-to-end from Windows Event Log → Service → MQTT → Backend → Clients
- **Correlation IDs**: Unique identifier flows through entire pipeline for request tracing
- **Structured Format**: JSON-based logs (machine-parsable, queryable)
- **Open Source Friendly**: Works without Seq; centralized logging is opt-in
- **Minimal Overhead**: Appropriate logging levels per environment

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Seq (Centralized)                       │
│                      http://localhost:5341                      │
└─────────────────────────────────────────────────────────────────┘
         ↑              ↑                ↑                ↑
    HTTP POST      HTTP POST        HTTP POST       HTTP POST
         │              │                │                │
┌─────────────┐  ┌─────────────┐  ┌───────────┐  ┌─────────────────┐
│   Windows   │  │   Backend   │  │    Web    │  │  Backend (Log   │
│   Service   │  │  (Serilog)  │  │  Client   │  │   Forwarder)    │
│  (Serilog)  │  │             │  │ (custom)  │  │   for ESP32     │
└─────────────┘  └─────────────┘  └───────────┘  └─────────────────┘
                                                         ↑
                                                    MQTT logs/#
                                                         │
                                                  ┌─────────────┐
                                                  │   ESP32     │
                                                  │  Devices    │
                                                  └─────────────┘
```

## Correlation ID Flow

```
Windows Event Log
    ↓
Windows Service (generates correlationId: "abc-123")
    ↓ MQTT payloads include correlationId
MQTT Broker (power-events AND system-metrics topics)
    ↓
Backend (logs with correlationId: "abc-123")
    ↓ SSE includes correlationId
Web Client (logs with correlationId: "abc-123")

ESP32 (logs with correlationId: "abc-123")
```

## Breaking Changes

This logging implementation introduces breaking changes to MQTT payloads:

### 1. Field Names: PascalCase → camelCase

All MQTT payload field names are changing from PascalCase to camelCase for consistency with JavaScript/JSON conventions.

| Before | After |
|--------|-------|
| `State` | `state` |
| `TimeGenerated` | `timeGenerated` |
| `CpuUsage` | `cpuUsage` |
| `MemoryUsage` | `memoryUsage` |

### 2. correlationId Added to All Payloads

Both `power-events` and `system-metrics` MQTT topics will now include a `correlationId` field for end-to-end tracing.

### Migration Notes

- **Backend**: Update JSON deserialization to use camelCase field names
- **Web Client**: Update property access (e.g., `event.State` → `event.state`)
- **ESP32**: Update ArduinoJson parsing to use camelCase keys

## Phases

| Phase | Component | Document |
|-------|-----------|----------|
| 1 | Windows Service | [01-windows-service.md](./01-windows-service.md) |
| 2 | Backend Server | [02-backend.md](./02-backend.md) |
| 3 | Web Client | [03-web-client.md](./03-web-client.md) |
| 4 | ESP32 Devices | [04-esp32.md](./04-esp32.md) |
| 5 | Seq Infrastructure | [05-seq-infrastructure.md](./05-seq-infrastructure.md) |

## Industry Reference

Logging approach inspired by established Windows service patterns:

- **File-based logging**: Primary persistent storage (like Google Chrome Updater's `updater.log`)
- **Windows Event Log**: Critical errors visible in Event Viewer (standard Windows practice)
- **Centralized sink**: Optional Seq integration for advanced querying
