import { BarData, Exchange, OrderData, OrderType, TickData } from '../common/models';
import { Strategy } from '../strategy/base';
import { StrategyContext } from '../strategy/context';
import { TradingGateway } from './gateway';
import { AccountManager } from './account';
import { logger } from '../common/logger';

export class TradingEngine {
  private _gateway: TradingGateway;
  private _account: AccountManager;
  private _strategy?: Strategy;
  private _running = false;

  constructor(gateway: TradingGateway, account?: AccountManager) {
    this._gateway = gateway;
    this._account = account ?? new AccountManager();
    this._gateway.onOrder = (o) => this._onOrder(o);
    this._gateway.onTrade = (t) => this._onTrade(t);
    this._gateway.onBar = (b) => this._onBar(b);
  }

  addStrategy(strategy: Strategy): void {
    strategy.ctx = new StrategyContext(this._account.cash);
    this._strategy = strategy;
  }

  subscribe(symbol: string): void { this._gateway.subscribe(symbol); }
  start(): void {
    if (!this._strategy) throw new Error('No strategy');
    this._gateway.connect();
    this._strategy.onInit();
    this._running = true;
    logger.info('TradingEngine started');
  }
  stop(): void {
    this._running = false;
    this._strategy?.onStop();
    this._gateway.disconnect();
    logger.info('TradingEngine stopped');
  }

  private _onBar(bar: BarData): void {
    if (!this._running || !this._strategy) return;
    this._strategy.ctx.currentBarTime = bar.datetime;
    this._strategy.onBar(bar);
    this._processOrders(bar);
    this._strategy.ctx.recordEquity(bar.datetime, { [bar.symbol]: bar.close });
  }

  private _onOrder(_order: OrderData): void {}
  private _onTrade(trade: any): void { this._account.updateTrade(trade); }

  private _processOrders(bar: BarData): void {
    if (!this._strategy) return;
    const orders = this._strategy.ctx.popPendingOrders();
    for (const order of orders) {
      if (order.orderType === OrderType.MARKET) order.price = bar.close;
      this._gateway.sendOrder(order);
    }
  }

  get account(): AccountManager { return this._account; }
  get gateway(): TradingGateway { return this._gateway; }
  get isRunning(): boolean { return this._running; }
}
