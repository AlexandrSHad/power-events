# Phase 3: Web Client Structured Logging

## Dependencies

- **Phase 2 Required**: This phase requires the `/api/logs` endpoint from Phase 2 (Backend Structured Logging) to be implemented first for log forwarding to work.

## Overview

Implement structured logging for the Vue 3 web client with optional backend log forwarding.

## Breaking Changes

- SSE event payload field names change from PascalCase to camelCase:
  - `State` → `state`
  - `TimeGenerated` → `timeGenerated`
  - `CorrelationId` → `correlationId`

## Scope

- Create `useLogger()` composable for structured logging
- Extract correlation ID from SSE events
- Log SSE connection lifecycle and received events
- Optional: Forward logs to backend endpoint for Seq ingestion

## File Changes

| File | Change Type | Description |
|------|-------------|-------------|
| `composables/useLogger.js` | New | Structured logging composable |
| `composables/useEventSource.js` | Modify | Extract correlationId from events |
| `.env.example` | Update | Add VITE_LOG_ENDPOINT |

## Key Components

### Logger Composable

```javascript
// composables/useLogger.js
export function useLogger(component) {
  const log = (level, message, context = {}) => {
    const entry = {
      timestamp: new Date().toISOString(),
      level,
      component,
      message,
      ...context
    };

    if (import.meta.env.DEV) {
      console[level](entry);
    }

    // Optional: send to backend
    if (import.meta.env.VITE_LOG_ENDPOINT) {
      navigator.sendBeacon(endpoint, JSON.stringify(entry));
    }
  };

  return {
    info: (msg, ctx) => log('info', msg, ctx),
    warn: (msg, ctx) => log('warn', msg, ctx),
    error: (msg, ctx) => log('error', msg, ctx)
  };
}
```

### SSE Event Handling with Correlation ID

```javascript
// composables/useEventSource.js
const handleEvent = (event) => {
  const data = JSON.parse(event.data);
  const { correlationId, state, timeGenerated } = data;

  logger.info('Power event received', {
    correlationId,
    state,
    timeGenerated
  });

  // Process event...
};
```

### Log Points

- SSE connection established/lost
- Power event received (with correlationId)
- Component mount/unmount (debug level)
- User interactions (if relevant)

## Configuration

```env
# .env
VITE_LOG_ENDPOINT=          # Empty = console only
VITE_LOG_LEVEL=info         # info, warn, error
```

## Backend Endpoint (Optional)

Backend provides `/api/logs` endpoint that forwards to Seq.
