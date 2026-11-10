import { Direction, OrderData, PositionData, TradeData } from '../common/models';

export class StrategyContext {
  initialCapital: number;
  cash: number;
  positions: Map<string, PositionData> = new Map();
  orders: OrderData[] = [];
  trades: TradeData[] = [];
  equityCurve: Array<{ datetime: Date; equity: number }> = [];
  currentBarTime?: Date;
  private _pendingOrders: OrderData[] = [];

  constructor(initialCapital: number = 1_000_000) {
    this.initialCapital = initialCapital;
    this.cash = initialCapital;
  }

  get totalEquity(): number {
    let posValue = 0;
    this.positions.forEach(p => { posValue += p.volume * p.price; });
    return this.cash + posValue;
  }

  getPosition(symbol: string, direction: Direction = Direction.LONG): PositionData | undefined {
    return this.positions.get(`${symbol}_${direction}`);
  }

  updateTrade(trade: TradeData): void {
    this.trades.push(trade);
    const key = `${trade.symbol}_${trade.direction}`;
    let pos = this.positions.get(key);

    if (trade.direction === Direction.LONG) {
      if (!pos) {
        pos = { symbol: trade.symbol, exchange: trade.exchange, direction: Direction.LONG, volume: 0, price: 0 };
        this.positions.set(key, pos);
      }
      const totalCost = pos.volume * pos.price + trade.volume * trade.price;
      pos.volume += trade.volume;
      pos.price = pos.volume > 0 ? totalCost / pos.volume : 0;
      this.cash -= trade.volume * trade.price + (trade.commission ?? 0);
    } else {
      if (!pos) return;
      pos.volume -= trade.volume;
      this.cash += trade.volume * trade.price - (trade.commission ?? 0);
      if (pos.volume <= 0) this.positions.delete(key);
    }
  }

  recordEquity(dt: Date, _priceMap: Record<string, number>): void {
    this.currentBarTime = dt;
    let posValue = 0;
    this.positions.forEach(p => { posValue += p.volume * p.price; });
    this.equityCurve.push({ datetime: dt, equity: this.cash + posValue });
  }

  addOrder(order: OrderData): void {
    this._pendingOrders.push(order);
    this.orders.push(order);
  }

  popPendingOrders(): OrderData[] {
    const orders = [...this._pendingOrders];
    this._pendingOrders = [];
    return orders;
  }

  reset(): void {
    this.cash = this.initialCapital;
    this.positions.clear();
    this.orders = [];
    this.trades = [];
    this.equityCurve = [];
    this._pendingOrders = [];
  }
}
