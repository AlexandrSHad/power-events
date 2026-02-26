import { Logger } from 'seq-logging-js';

const seqEnabled = import.meta.env.VITE_SEQ_ENABLED === 'true';
const seqUrl = import.meta.env.VITE_SEQ_URL || 'http://localhost:5341';

const logger = seqEnabled 
  ? new Logger({
      serverUrl: seqUrl,
      apiKey: null,
      onError: (e) => console.error('Seq logging error:', e),
      useBatch: true,
      batchSize: 10,
      batchTimeout: 1000
    })
  : null;

export default logger;

export function log(level, messageTemplate, properties = {}) {
  if (!logger) {
    console.log(`[${level}]`, messageTemplate, properties);
    return;
  }

  logger.emit({
    timestamp: new Date(),
    level: level,
    messageTemplate: messageTemplate,
    properties: {
      ...properties,
      Component: 'WebClient'
    }
  });
}

export function logInfo(messageTemplate, properties = {}) {
  log('Information', messageTemplate, properties);
}

export function logWarning(messageTemplate, properties = {}) {
  log('Warning', messageTemplate, properties);
}

export function logError(messageTemplate, properties = {}) {
  log('Error', messageTemplate, properties);
}

export function logDebug(messageTemplate, properties = {}) {
  log('Debug', messageTemplate, properties);
}