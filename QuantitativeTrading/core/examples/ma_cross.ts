import { Strategy } from '../src/strategy/base';
import { BarData, OrderType } from '../src/common/models';

export class MaCrossStrategy extends Strategy {
  private _prices: number[] = [];
  private _positionOpen = false;

  constructor(private _fastPeriod = 5, private _slowPeriod = 20) {
    super();
  }

  onInit(): void {
    this._prices = [];
    this._positionOpen = false;
  }

  onBar(bar: BarData): void {
    this._prices.push(bar.close);
    if (this._prices.length < this._slowPeriod + 1) return;

    const fast = this._ma(this._fastPeriod);
    const slow = this._ma(this._slowPeriod);
    const prevFast = this._maPrev(this._fastPeriod);
    const prevSlow = this._maPrev(this._slowPeriod);

    if (prevFast <= prevSlow && fast > slow && !this._positionOpen) {
      this.buy(bar.symbol, bar.exchange, bar.close, 100);
      this._positionOpen = true;
    } else if (prevFast >= prevSlow && fast < slow && this._positionOpen) {
      this.sell(bar.symbol, bar.exchange, bar.close, 100);
      this._positionOpen = false;
    }
  }

  private _ma(p: number): number {
    return this._prices.slice(-p).reduce((a, b) => a + b, 0) / p;
  }

  private _maPrev(p: number): number {
    return this._prices.slice(-(p + 1), -1).reduce((a, b) => a + b, 0) / p;
  }
}
