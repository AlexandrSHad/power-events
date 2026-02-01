# Phase 5: Seq Infrastructure

## Overview

Deploy and configure Seq as the centralized log aggregation platform for all Power Events components.

## Scope

- Add Seq to Docker Compose stack
- Configure log retention and storage
- Set up API keys for each component
- Create dashboards and saved queries
- Document access and usage

## Docker Compose Addition

```yaml
# .build/docker-compose.yml
services:
  seq:
    image: datalust/seq:latest
    container_name: seq
    restart: unless-stopped
    environment:
      - ACCEPT_EULA=Y
      - SEQ_FIRSTRUN_ADMINPASSWORDHASH=<hash>  # Optional
    ports:
      - "5341:80"      # Web UI and ingestion
    volumes:
      - seq-data:/data

volumes:
  seq-data:
```

## Network Configuration

| Component | Seq Access |
|-----------|------------|
| Windows Service | `http://<docker-host>:5341` |
| Backend (Docker) | `http://seq:80` (internal) |
| Web Client | Via backend `/api/logs` endpoint |
| ESP32 | Via backend log forwarder |

## API Keys (Optional)

Separate API keys per component for:
- Access control
- Log source identification
- Rate limiting

## Saved Queries

Pre-configured queries for common troubleshooting:

| Query | Purpose |
|-------|---------|
| `CorrelationId = "..."` | Trace single event flow |
| `CorrelationId = "..." and Application in [...]` | Trace correlationId across all components |
| `@Level = "Error"` | All errors |
| `Application = "PowerEvents.WindowsService"` | Windows Service logs |
| `Component = "MqttSubscriberService"` | MQTT-related logs |
| `Topic = "system-metrics"` | System metrics logs (CPU/RAM) |
| `device = "esp32-display"` | ESP32 display logs |

## Backup & Retention

| Component | Retention |
|-----------|-----------|
| Windows Service file logs | 2 days |
| Seq default | 7 days (configurable) |

### Seq Retention Policy Configuration

Configure retention via Seq Admin UI or API:

```json
{
  "retentionPolicy": {
    "retentionTime": "7.00:00:00",
    "deleteOnlyWhenSpaceNeeded": false,
    "minimumFreeSpaceBytes": 1073741824
  }
}
```

Alternatively, set via environment variables:

```yaml
environment:
  - SEQ_RETENTION_RETENTIONPERIOD=7.00:00:00
  - SEQ_STORAGE_MINIMUMFREESPACEBYTES=1073741824
```

Consider storage implications for high-volume scenarios.

## Dashboards

- Overview: Log volume by component, error rate
- Power Events: Event flow, latency tracking
- System Health: Service status, connection errors
