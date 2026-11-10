import { BarData } from '../common/models';
import { BacktestResult } from './result';

export interface EquityPoint {
  datetime: Date;
  equity: number;
}

export class BacktestEngine {
  private _initialCapital: number;
  private _commissionRate: number;
  private _slippageRate: number;

  constructor(
    opts: {
      initialCapital?: number;
      commissionRate?: number;
      slippageRate?: number;
    } = {}
  ) {
    this._initialCapital = opts.initialCapital ?? 1_000_000;
    this._commissionRate = opts.commissionRate ?? 0.0003;
    this._slippageRate = opts.slippageRate ?? 0.0001;
  }

  async run(
    strategyClass: new (...args: any[]) => import('../strategy/base').Strategy,
    bars: BarData[],
    ...strategyArgs: any[]
  ): Promise<BacktestResult> {
    const { StrategyContext } = await import('../strategy/context');
    const ctx = new StrategyContext(this._initialCapital);
    const strategy = new strategyClass(...strategyArgs);
    strategy.ctx = ctx;

    strategy.onInit();

    for (let i = 0; i < bars.length; i++) {
      const bar = bars[i];
      ctx.currentBarTime = bar.datetime;
      strategy.onBar(bar);

      const pending = ctx.popPendingOrders();
      for (const order of pending) {
        const fillPrice = this._calcSlippage(order.price, order.direction === 'long');
        const commission = this._calcCommission(fillPrice, order.volume);

        order.filled = order.volume;
        order.price = fillPrice;

        ctx.updateTrade({
          symbol: order.symbol,
          exchange: order.exchange,
          tradeId: `t${ctx.trades.length + 1}`,
          orderId: order.orderId,
          direction: order.direction,
          price: fillPrice,
          volume: order.volume,
          datetime: bar.datetime,
          commission,
        });
      }

      ctx.recordEquity(bar.datetime, { [bar.symbol]: bar.close });
    }

    strategy.onStop();

    return new BacktestResult(
      this._initialCapital,
      ctx.equityCurve,
      ctx.trades,
    );
  }

  private _calcCommission(price: number, volume: number): number {
    return price * volume * this._commissionRate;
  }

  private _calcSlippage(price: number, isBuy: boolean): number {
    return isBuy
      ? price * (1 + this._slippageRate)
      : price * (1 - this._slippageRate);
  }
}
