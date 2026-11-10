import { TradeData } from '../common/models';

export interface EquityPoint {
  datetime: Date;
  equity: number;
}

export class BacktestResult {
  constructor(
    public readonly initialCapital: number,
    public readonly equityCurve: EquityPoint[],
    public readonly trades: TradeData[],
  ) {}

  get finalEquity(): number {
    return this.equityCurve.length > 0
      ? this.equityCurve[this.equityCurve.length - 1].equity
      : this.initialCapital;
  }

  get totalReturn(): number {
    return (this.finalEquity - this.initialCapital) / this.initialCapital;
  }

  get totalDays(): number {
    if (this.equityCurve.length < 2) return 0;
    return Math.floor(
      (this.equityCurve[this.equityCurve.length - 1].datetime.getTime() -
        this.equityCurve[0].datetime.getTime()) /
        (1000 * 60 * 60 * 24)
    );
  }

  getStats(): Record<string, any> {
    const returns: number[] = [];
    for (let i = 1; i < this.equityCurve.length; i++) {
      returns.push(
        (this.equityCurve[i].equity - this.equityCurve[i - 1].equity) /
          this.equityCurve[i - 1].equity
      );
    }

    const avgRet = returns.reduce((a, b) => a + b, 0) / (returns.length || 1);
    const stdRet = Math.sqrt(
      returns.reduce((a, b) => a + (b - avgRet) ** 2, 0) / (returns.length || 1)
    );

    const annRet = this.totalReturn / (this.totalDays / 365 || 1);
    const annVol = stdRet * Math.sqrt(252);
    const sharpe = annVol > 0 ? (annRet - 0.03) / annVol : 0;

    let peak = this.initialCapital;
    let maxDD = 0;
    for (const p of this.equityCurve) {
      if (p.equity > peak) peak = p.equity;
      const dd = (peak - p.equity) / peak;
      if (dd > maxDD) maxDD = dd;
    }

    return {
      initialCapital: this.initialCapital,
      finalEquity: this.finalEquity,
      totalReturn: this.totalReturn,
      annualReturn: annRet,
      sharpeRatio: sharpe,
      maxDrawdown: maxDD,
      calmarRatio: maxDD > 0 ? annRet / maxDD : 0,
      totalTrades: this.trades.length,
      tradingDays: this.equityCurve.length,
    };
  }
}
