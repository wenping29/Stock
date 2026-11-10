import { OrderData, TradeData, BarData, TickData } from '../common/models';

export interface TradingGateway {
  readonly name: string;
  readonly isConnected: boolean;
  connect(opts?: Record<string, any>): void;
  disconnect(): void;
  sendOrder(order: OrderData): string;
  cancelOrder(orderId: string): void;
  subscribe(symbol: string): void;
  unsubscribe(symbol: string): void;
  queryOrders(): OrderData[];
  queryPositions(): any;
  queryAccount(): any;
  onOrder?: (order: OrderData) => void;
  onTrade?: (trade: TradeData) => void;
  onBar?: (bar: BarData) => void;
  onTick?: (tick: TickData) => void;
}
