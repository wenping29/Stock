import { BarData, Direction, Exchange, OrderData, OrderType, TickData } from '../common/models';
import { StrategyContext } from './context';

export abstract class Strategy {
  ctx!: StrategyContext;
  readonly name: string;

  constructor() {
    this.name = this.constructor.name;
  }

  onInit(): void {}
  abstract onBar(bar: BarData): void;
  onTick(_tick: TickData): void {}
  onStop(): void {}

  buy(symbol: string, exchange: Exchange, price: number, volume: number, orderType: OrderType = OrderType.MARKET): OrderData {
    const order: OrderData = {
      symbol,
      exchange,
      orderId: `${Date.now()}_${Math.random().toString(36).slice(2, 8)}`,
      direction: Direction.LONG,
      orderType,
      price,
      volume,
      filled: 0,
    };
    this.ctx.addOrder(order);
    return order;
  }

  sell(symbol: string, exchange: Exchange, price: number, volume: number, orderType: OrderType = OrderType.MARKET): OrderData {
    const order: OrderData = {
      symbol,
      exchange,
      orderId: `${Date.now()}_${Math.random().toString(36).slice(2, 8)}`,
      direction: Direction.SHORT,
      orderType,
      price,
      volume,
      filled: 0,
    };
    this.ctx.addOrder(order);
    return order;
  }

  get cash(): number { return this.ctx.cash; }
  get positions() { return this.ctx.positions; }
}
