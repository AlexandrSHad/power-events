# Seq Logging Implementation

This document describes the structured logging observability feature using Seq.

## Overview

All components in the Power Events system can now send structured logs to a centralized Seq instance running in a Docker container. This provides a unified view of logs from Windows Service, Backend, Web Client, and ESP32 devices.

## Architecture

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│ Windows Service │────▶│      Seq        │◀────│   Web Client    │
│ (Serilog)       │     │   (Docker)      │     │  (seq-logging-js)│
└─────────────────┘     └────────┬────────┘     └─────────────────┘
                                │
                         ┌──────▼──────┐
                         │   Backend   │
                         │ (Serilog)   │
                         └──────┬──────┘
                                │
                         ┌──────▼──────┐
                         │   ESP32     │
                         │ (MQTT logs) │
                         └─────────────┘
```

## Configuration

### 1. Seq Container

Start Seq using Docker Compose:

```bash
docker-compose up -d
```

Seq will be available at `http://localhost:5341`

**Note:** The first time you run Seq, you'll need to create an admin user. The free tier allows:
- 500 MB of log data
- 30-day log retention

### 2. Windows Service

Edit `windows-service/appsettings.json`:

```json
{
  "Seq": {
    "Enabled": true,
    "ServerUrl": "http://localhost:5341"
  },
  "Mqtt": {
    "Host": "rpi.local",
    "Port": 1883
  }
}
```

To disable Seq logging, set `"Seq:Enabled": false`

### 3. Backend

Edit `backend/appsettings.json`:

```json
{
  "Mqtt": {
    "Host": "localhost",
    "Port": 1883
  },
  "Seq": {
    "Enabled": true,
    "ServerUrl": "http://seq:5341"
  }
}
```

To disable Seq logging, set `"Seq:Enabled": false`

### 4. Web Client

Edit `web-client/.env`:

```env
VITE_BACKEND_URL=http://localhost:5550
VITE_SEQ_ENABLED=true
VITE_SEQ_URL=http://localhost:5341
```

To disable Seq logging, set `VITE_SEQ_ENABLED=false`

### 5. ESP32

Edit `esp32-display/src/main.cpp`:

```cpp
// Seq logging configuration (disable to stop sending logs via MQTT)
#define SEQ_LOGGING_ENABLED true
```

To disable Seq logging, set `#define SEQ_LOGGING_ENABLED false`

**Important:** ESP32 logs are ALWAYS sent to Serial0 for debugging, regardless of the SEQ_LOGGING_ENABLED setting.

## Log Properties

All logs are enriched with the following properties:

### Common Properties (All Components)
- `Component`: Component name (PowerEvents, Backend, WebClient, ESP32)
- `Timestamp`: Log timestamp
- `Level`: Log level (Information, Warning, Error, Debug)

### Component-Specific Properties

**Windows Service:**
- `DeviceId`: Machine name

**ESP32:**
- `DeviceId`: ESP32 hostname
- `LogLevel`: Log level from MQTT topic

## MQTT Log Topics (ESP32)

ESP32 devices publish logs to the following MQTT topic structure:

```
logs/esp32/<device-id>/<level>
```

Example: `logs/esp32/esp32-display-setup/info`

Supported log levels:
- `info` - Informational messages
- `warning` - Warning messages
- `error` - Error messages
- `debug` - Debug messages

The Backend subscribes to `logs/esp32/#` and forwards these logs to Seq.

## Usage in Code

### Windows Service (.NET)

```csharp
// Serilog is automatically configured
// Just use ILogger as usual:

logger.LogInformation(
    "Power event: State={State}, TimeGenerated={TimeGenerated}",
    state, timeGenerated);

logger.LogWarning(ex, "Failed to collect metrics");

logger.LogError(ex, "MQTT connection lost");
```

### Backend (.NET)

```csharp
// Serilog is automatically configured
// Use ILogger:

logger.LogInformation("MQTT message received: {Payload}", payload);

// ESP32 logs are automatically forwarded
```

### Web Client (Vue.js)

```javascript
import { logInfo, logWarning, logError, logDebug } from '@/utils/logger';

// Log information
logInfo('Power event received', { State: state, Time: timestamp });

// Log warning
logWarning('High CPU usage detected', { CpuPercent: 95.5 });

// Log error
logError('Failed to connect to backend', { Error: error.message });

// Log debug
logDebug('Component mounted', { Component: 'StatusCard' });
```

### ESP32 (C++)

```cpp
// Log to both Serial0 and Seq (if enabled)
logToSeq("info", "System initialized");
logToSeq("warning", "WiFi reconnection required");
logToSeq("error", "Failed to parse JSON");
```

## Disabling Seq Logging

To disable Seq logging for any component:

### Windows Service
```json
"Seq: { "Enabled": false }
```

### Backend
```json
"Seq": { "Enabled": false }
```

### Web Client
```env
VITE_SEQ_ENABLED=false
```

### ESP32
```cpp
#define SEQ_LOGGING_ENABLED false
```

**Note:** Disabling Seq logging does not affect other logging mechanisms:
- Windows Service/Backend: Still log to console
- Web Client: Still log to browser console
- ESP32: Still logs to Serial0

## Viewing Logs

1. Open Seq UI: `http://localhost:5341`
2. Use the query bar to filter logs:
   - Search by component: `Component = "ESP32"`
   - Search by device: `DeviceId = "esp32-display-setup"`
   - Search by level: `@Level = "Error"`
   - Time range: Click the time selector
3. Click on any log entry to see full details and properties

## Query Examples

```
# All errors from ESP32 devices
Component = "ESP32" and @Level = "Error"

# Power state changes from Windows Service
Component = "PowerEvents" and State is not null

# Backend MQTT messages
Component = "Backend" and MQTT is not null

# High CPU warnings
Component = "WebClient" and CpuPercent > 90

# Time range
@Timestamp >= DateTime.Today
```

## Troubleshooting

### Seq container not starting

Check Docker logs:
```bash
docker logs seq
```

Ensure port 5341 is not already in use.

### Logs not appearing in Seq

1. Verify Seq is running: `http://localhost:5341`
2. Check if Seq is enabled in component configuration
3. Check firewall/network connectivity
4. Verify Seq URL is correct:
   - Windows Service: `http://localhost:5341` (or VPS IP)
   - Backend: `http://seq:5341` (Docker network)
   - Web Client: `http://localhost:5341` (or VPS IP)

### ESP32 logs not appearing

1. Verify MQTT broker is running
2. Check Backend is subscribed to `logs/esp32/#`
3. Verify SEQ_LOGGING_ENABLED is true in ESP32 code
4. Check Backend logs for MQTT errors

### High log volume from ESP32

The ESP32 sends metrics every 2 seconds. This can generate many logs. Consider:
- Reducing log frequency in ESP32 code
- Using Warning/Error levels only for ESP32 metrics
- Increasing Seq retention/storage limits (paid tier)

## Deployment on VPS

When deploying to a VPS:

1. Update Seq URL in configurations to VPS IP:
   ```json
   "Seq": { "ServerUrl": "http://your-vps-ip:5341" }
   ```

2. Or use a domain name:
   ```json
   "Seq": { "ServerUrl": "http://logs.yourdomain.com:5341" }
   ```

3. Ensure firewall allows port 5341:
   ```bash
   ufw allow 5341
   ```

4. For production, consider:
   - Using HTTPS with a reverse proxy (nginx)
   - Seq API keys for authentication
   - Paid Seq tier for longer retention

## License

Seq uses a dual license model:
- **Free tier**: 500 MB logs, 30-day retention
- **Paid tiers**: Higher limits, additional features

See https://datalust.co/seq/pricing for details.

## Further Reading

- Seq Documentation: https://docs.datalust.co/
- Serilog Documentation: https://serilog.net/
- MQTT Topic Structure: See `backend/server.cs` for ESP32 log forwarding logic