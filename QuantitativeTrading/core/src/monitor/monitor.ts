import { TradeData, OrderData } from '../common/models';
import { logger } from '../common/logger';

export interface AlertChannel {
  readonly name: string;
  send(level: string, title: string, message?: string): void;
}

export class ConsoleAlert implements AlertChannel {
  readonly name = 'console';
  send(level: string, title: string, message?: string): void {
    const msg = `[${level}] ${title}${message ? ' | ' + message : ''}`;
    if (level === 'ERROR') logger.error(msg);
    else if (level === 'WARN') logger.warn(msg);
    else logger.info(msg);
  }
}

export class AlertManager {
  private _channels: Map<string, AlertChannel> = new Map();
  addChannel(ch: AlertChannel): void { this._channels.set(ch.name, ch); }
  send(level: string, title: string, message?: string): void {
    this._channels.forEach(ch => ch.send(level, title, message));
  }
  info(title: string, msg?: string): void { this.send('INFO', title, msg); }
  warn(title: string, msg?: string): void { this.send('WARN', title, msg); }
  error(title: string, msg?: string): void { this.send('ERROR', title, msg); }
}

export class MonitorEngine {
  private _alert = new AlertManager();
  private _tradeLog: TradeData[] = [];
  private _equitySnapshots: Array<{ datetime: Date; equity: number }> = [];

  get alert(): AlertManager { return this._alert; }

  constructor() { this._alert.addChannel(new ConsoleAlert()); }

  onTrade(trade: TradeData): void {
    this._tradeLog.push(trade);
    this._alert.info(`Trade: ${trade.symbol}`, `${trade.direction} ${trade.volume}@${trade.price}`);
  }

  onEquityChange(equity: number, datetime?: Date): void {
    this._equitySnapshots.push({ datetime: datetime ?? new Date(), equity });
  }

  report(): Record<string, any> {
    return {
      tradesLogged: this._tradeLog.length,
      equitySnapshots: this._equitySnapshots.length,
    };
  }
}
