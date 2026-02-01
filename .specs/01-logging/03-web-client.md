# Phase 3: Web Client Structured Logging

## Overview

Implement structured logging for the Vue 3 web client with optional backend log forwarding.

## Scope

- Create `useLogger()` composable for structured logging
- Extract correlation ID from SSE events
- Log SSE connection lifecycle and received events
- Optional: Forward logs to backend endpoint for Seq ingestion

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
