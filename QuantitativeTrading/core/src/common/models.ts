export enum Direction {
  LONG = 'long',
  SHORT = 'short',
}

export enum OrderType {
  MARKET = 'market',
  LIMIT = 'limit',
  STOP = 'stop',
}

export enum OrderStatus {
  PENDING = 'pending',
  SUBMITTED = 'submitted',
  PARTIAL_FILLED = 'partial_filled',
  FILLED = 'filled',
  CANCELLED = 'cancelled',
  REJECTED = 'rejected',
}

export enum Exchange {
  SHH = 'SHH',
  SHZ = 'SHZ',
  BJ = 'BJ',
  NYSE = 'NYSE',
  NASDAQ = 'NASDAQ',
  AMEX = 'AMEX',
  HKEX = 'HKEX',
}

export enum BarInterval {
  TICK = 'tick',
  MIN1 = '1m',
  MIN5 = '5m',
  MIN15 = '15m',
  MIN30 = '30m',
  MIN60 = '60m',
  DAILY = '1d',
  WEEKLY = '1w',
  MONTHLY = '1M',
}

export interface BarData {
  symbol: string;
  exchange: Exchange;
  datetime: Date;
  interval?: BarInterval;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
  turnover?: number;
  openInterest?: number;
  adjFactor?: number;
  gateway?: string;
}

export interface TickData {
  symbol: string;
  exchange: Exchange;
  datetime: Date;
  lastPrice: number;
  volume?: number;
  turnover?: number;
  bidPrice1?: number;
  bidVolume1?: number;
  askPrice1?: number;
  askVolume1?: number;
  bidPrice2?: number;
  bidVolume2?: number;
  askPrice2?: number;
  askVolume2?: number;
  gateway?: string;
}

export interface OrderData {
  symbol: string;
  exchange: Exchange;
  orderId: string;
  direction: Direction;
  orderType?: OrderType;
  price: number;
  volume: number;
  filled?: number;
  status?: OrderStatus;
  datetime?: Date;
  gateway?: string;
}

export interface TradeData {
  symbol: string;
  exchange: Exchange;
  tradeId: string;
  orderId: string;
  direction: Direction;
  price: number;
  volume: number;
  datetime: Date;
  commission?: number;
  gateway?: string;
}

export interface PositionData {
  symbol: string;
  exchange: Exchange;
  direction: Direction;
  volume: number;
  frozen?: number;
  price: number;
  pnl?: number;
  gateway?: string;
}
