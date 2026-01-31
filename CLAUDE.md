# Power Events Project

## Overview
Windows service monitoring laptop power state (sleep/wake) and system performance,
broadcasting to web clients and ESP32 hardware displays via MQTT.

## Architecture
- Windows Service → MQTT Broker
- MQTT → Backend (SSE) → Web Client
- MQTT → ESP32 Devices (LED/Display)

## Tech Stack
| Component | Technology |
|-----------|------------|
| Windows Service | C#, .NET 10.0-windows, MQTTnet |
| Backend | ASP.NET Core minimal APIs |
| Frontend | Vue 3, Vite, Vuetify 3 |
| ESP32 | PlatformIO, ArduinoJson, LVGL |
| Broker | EMQX (Docker) |

## Project Structure
- `windows-service/` - PowerEvents Windows Service
- `backend/` - ASP.NET Core SSE server
- `web-client/` - Vue 3 dashboard
- `esp32-display/` - ESP32 with 480x480 LVGL round display
- `.build/` - Docker compose orchestration

## Key MQTT Topics
- `power-events` - State changes (Standby/Awake)
- `system-metrics` - CPU/RAM utilization

## Build Commands
# ESP32 (PlatformIO)
pio run
pio run -t upload

# Windows Service
cd windows-service && ./deploy.cmd

# Web Client
cd web-client && npm run dev

## Coding Standards
- C#: Follow .NET conventions, top-level statements OK
- Vue: Use `<script setup>` syntax
- ESP32: Keep main.cpp focused, extract to lib/ for reuse

## AI Guidelines
- Use sub-agents for multi-file exploration
- Always use absolute paths (agent cwd resets between bash calls)
- Test MQTT changes with mosquitto_pub/sub before service deployment
