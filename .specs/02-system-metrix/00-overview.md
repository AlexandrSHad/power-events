# System Metrics Expansion: Battery, Temperature & CPU Fix

## Overview

Expand system metrics collection with battery status, CPU/GPU temperatures, and fix CPU averaging. Update ESP32 display to show temperatures in the center area and make the static battery indicator functional.

## Goals

- **Battery Monitoring**: Charging status and percentage via Hardware.Info (existing package)
- **Temperature Monitoring**: CPU and GPU temperatures via LibreHardwareMonitorLib (new package)
- **CPU Fix**: Average across all processors instead of reading only the first one
- **ESP32 UI**: Display temperatures in center, make battery indicator dynamic

## Architecture

```
Windows Service (Hardware.Info + LibreHardwareMonitorLib)
    │
    │  MQTT topic: system-metrics
    │  {CpuPercent, RamPercent, CpuTempCelsius, GpuTempCelsius, BatteryPercent, BatteryCharging}
    │
    ▼
MQTT Broker (EMQX)
    │
    ▼
ESP32 Display (subscribes to system-metrics)
    ├── CPU/RAM arcs (existing)
    ├── CPU/GPU temperature labels (new, center area)
    └── Battery indicator (existing static → dynamic)
```

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| MQTT structure | Extend existing `system-metrics` topic | ESP32 already subscribes; avoids extra topic |
| GPU color | Orange `#FF6D00` | Warm contrast with green CPU and cyan RAM |
| Temperature format | `42°` (no "C") | Space-limited 240px display |
| Temperature library | LibreHardwareMonitorLib | Only reliable option for temps in C#; service runs as LocalSystem so admin is fine |
| Battery library | Hardware.Info (existing) | Already in project, supports battery natively |

## MQTT Payload (After)

```json
{
  "CpuPercent": 23.5,
  "RamPercent": 67.2,
  "CpuTempCelsius": 52.3,
  "GpuTempCelsius": 41.0,
  "BatteryPercent": 78.0,
  "BatteryCharging": true,
  "Timestamp": "2026-02-08T14:30:00"
}
```

When sensors are unavailable (e.g., desktop without battery):
```json
{
  "CpuTempCelsius": 52.3,
  "GpuTempCelsius": null,
  "BatteryPercent": null,
  "BatteryCharging": null
}
```

## Phases

| Phase | Component | Document |
|-------|-----------|----------|
| 1 | Windows Service | [01-windows-service.md](./01-windows-service.md) |
| 2 | ESP32 Display | [02-esp32.md](./02-esp32.md) |

## Risks

| Risk | Mitigation |
|------|------------|
| Sensor names vary by hardware (CPU "Package" / GPU "Core") | Fallback to first available temperature sensor |
| PubSubClient default 256-byte buffer too small | Increase to 512 bytes |
| Hardware.Info battery API string format varies | Use `Contains("Charging")` for robust matching |
| `LV_FONT_MONTSERRAT_14` not explicitly enabled | Add to `lv_conf.h` |

## Verification

1. Deploy Windows service, run `mosquitto_sub -t system-metrics`
2. Confirm CPU% differs from single-CPU reading (averaging works)
3. Confirm temperatures are reasonable (30-90°C)
4. Confirm battery fields populated (or null on desktop)
5. Build ESP32: `pio run`, upload, verify display
