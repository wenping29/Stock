import { OrderData, Direction, OrderType, OrderStatus, TradeData, BarData, TickData, Exchange } from '../common/models';
import { TradingGateway } from './gateway';
import { logger } from '../common/logger';

export class SimGateway implements TradingGateway {
  readonly name = 'sim';
  isConnected = false;
  onOrder?: (order: OrderData) => void;
  onTrade?: (trade: TradeData) => void;
  onBar?: (bar: BarData) => void;
  onTick?: (tick: TickData) => void;

  private _orders: Map<string, OrderData> = new Map();
  private _trades: TradeData[] = [];
  private _currentPrice: Record<string, number> = {};

  connect(): void {
    this.isConnected = true;
    logger.info('SimGateway connected');
  }

  disconnect(): void {
    this.isConnected = false;
    logger.info('SimGateway disconnected');
  }

  sendOrder(order: OrderData): string {
    if (!this.isConnected) throw new Error('Gateway not connected');
    order.status = OrderStatus.FILLED;
    const price = this._currentPrice[order.symbol] ?? order.price;
    order.price = price;
    order.filled = order.volume;

    this._orders.set(order.orderId, order);
    const trade: TradeData = {
      symbol: order.symbol,
      exchange: order.exchange,
      tradeId: `sim_${Date.now()}`,
      orderId: order.orderId,
      direction: order.direction,
      price,
      volume: order.volume,
      datetime: new Date(),
      commission: price * order.volume * 0.0003,
    };
    this._trades.push(trade);
    this.onOrder?.(order);
    this.onTrade?.(trade);
    logger.info(`Sim fill: ${order.symbol} ${order.direction} ${order.volume}@${price}`);
    return order.orderId;
  }

  cancelOrder(_orderId: string): void {}

  subscribe(_symbol: string): void {}
  unsubscribe(_symbol: string): void {}
  queryOrders(): OrderData[] { return Array.from(this._orders.values()); }
  queryPositions(): any { return {}; }
  queryAccount(): any { return {}; }

  simulateBar(bar: BarData): void {
    this._currentPrice[bar.symbol] = bar.close;
    this.onBar?.(bar);
  }

  updateBar(bar: BarData): void {
    this._currentPrice[bar.symbol] = bar.close;
    this.onBar?.(bar);
  }

  updateTick(tick: TickData): void {
    this._currentPrice[tick.symbol] = tick.lastPrice;
    this.onTick?.(tick);
  }
}
