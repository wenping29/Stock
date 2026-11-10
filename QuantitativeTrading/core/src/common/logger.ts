const LOG_LEVELS: Record<string, number> = {
  debug: 0, info: 1, warn: 2, error: 3,
};

let _minLevel = LOG_LEVELS['info'];

export function setLogLevel(level: string): void {
  _minLevel = LOG_LEVELS[level.toLowerCase()] ?? LOG_LEVELS['info'];
}

function log(level: string, ...args: any[]): void {
  if ((LOG_LEVELS[level] ?? 0) < _minLevel) return;
  const ts = new Date().toISOString();
  const prefix = `[${ts}] [${level.toUpperCase()}]`;
  if (level === 'error') {
    console.error(prefix, ...args);
  } else if (level === 'warn') {
    console.warn(prefix, ...args);
  } else {
    console.log(prefix, ...args);
  }
}

export const logger = {
  debug: (...args: any[]) => log('debug', ...args),
  info: (...args: any[]) => log('info', ...args),
  warn: (...args: any[]) => log('warn', ...args),
  error: (...args: any[]) => log('error', ...args),
};
