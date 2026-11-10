import { Direction, OrderData, TradeData, BarData, TickData, OrderType } from '../common/models';
import { TradingGateway } from './gateway';
import { logger } from '../common/logger';

export class AccountManager {
  private _cash: number;
  private _frozen: number = 0;
  private _positions: Map<string, { symbol: string; direction: Direction; volume: number; price: number }> = new Map();

  constructor(public readonly initialCapital: number = 1_000_000) {
    this._cash = initialCapital;
  }

  updateTrade(trade: TradeData): void {
    const key = `${trade.symbol}_long`;
    if (trade.direction === Direction.LONG) {
      let pos = this._positions.get(key);
      if (!pos) {
        pos = { symbol: trade.symbol, direction: Direction.LONG, volume: 0, price: 0 };
        this._positions.set(key, pos);
      }
      const totalCost = pos.volume * pos.price + trade.volume * trade.price;
      pos.volume += trade.volume;
      pos.price = pos.volume > 0 ? totalCost / pos.volume : 0;
      this._cash -= trade.volume * trade.price + (trade.commission ?? 0);
    } else {
      const pos = this._positions.get(key);
      if (!pos) { logger.warn(`No position to close: ${trade.symbol}`); return; }
      pos.volume -= trade.volume;
      this._cash += trade.volume * trade.price - (trade.commission ?? 0);
      if (pos.volume <= 0) this._positions.delete(key);
    }
  }

  get totalEquity(): number {
    let posValue = 0;
    this._positions.forEach(p => { posValue += p.volume * p.price; });
    return this._cash + posValue;
  }

  get cash(): number { return this._cash; }
  get positions() { return this._positions; }

  summary(): Record<string, any> {
    const pos: Record<string, any> = {};
    this._positions.forEach((p, k) => { pos[k] = { symbol: p.symbol, direction: p.direction, volume: p.volume, price: p.price }; });
    return {
      initialCapital: this.initialCapital,
      cash: this._cash,
      totalEquity: this.totalEquity,
      totalPnl: this.totalEquity - this.initialCapital,
      positions: pos,
    };
  }
}
