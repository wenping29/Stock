import { OrderData } from '../common/models';

export interface RiskRule {
  readonly name: string;
  check(order: OrderData, ctx: RiskContext): RuleResult;
}

export class RuleResult {
  constructor(public readonly passed: boolean, public readonly message: string = '') {}
  static ok(): RuleResult { return new RuleResult(true); }
  static reject(msg: string): RuleResult { return new RuleResult(false, msg); }
}

export class RiskContext {
  positions: Record<string, { volume: number; price: number }> = {};
  totalEquity = 0;
  peakEquity = 0;
  currentPrice: Record<string, number> = {};
}

export class MaxPositionRule implements RiskRule {
  readonly name = 'max_position';
  constructor(private _maxShares: number = 10000) {}
  check(order: OrderData, ctx: RiskContext): RuleResult {
    const pos = ctx.positions[`${order.symbol}_long`];
    const current = pos?.volume ?? 0;
    return (current + order.volume) <= this._maxShares
      ? RuleResult.ok()
      : RuleResult.reject(`Position ${current}+${order.volume} > max ${this._maxShares}`);
  }
}

export class MaxDrawdownRule implements RiskRule {
  readonly name = 'max_drawdown';
  constructor(private _maxDD: number = 0.20) {}
  check(_order: OrderData, ctx: RiskContext): RuleResult {
    if (ctx.peakEquity <= 0) return RuleResult.ok();
    const dd = (ctx.peakEquity - ctx.totalEquity) / ctx.peakEquity;
    return dd <= this._maxDD
      ? RuleResult.ok()
      : RuleResult.reject(`Drawdown ${(dd * 100).toFixed(1)}% > max ${(this._maxDD * 100).toFixed(0)}%`);
  }
}

export class RiskManager {
  private _rules: RiskRule[] = [];
  private _context = new RiskContext();
  private _circuitBreaker = false;

  addRule(rule: RiskRule): void { this._rules.push(rule); }
  get circuitBreaker(): boolean { return this._circuitBreaker; }
  enableCircuitBreaker(): void { this._circuitBreaker = true; }

  checkOrder(order: OrderData): boolean {
    if (this._circuitBreaker) return false;
    for (const rule of this._rules) {
      const result = rule.check(order, this._context);
      if (!result.passed) return false;
    }
    return true;
  }

  updateContext(positions: any, totalEquity: number, priceMap: Record<string, number>): void {
    this._context.positions = positions;
    this._context.totalEquity = totalEquity;
    this._context.peakEquity = Math.max(this._context.peakEquity, totalEquity);
    this._context.currentPrice = priceMap;
  }
}
