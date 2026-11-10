export interface Factor {
  readonly name: string;
  compute(prices: number[]): (number | undefined)[];
}

export class SMAFactor implements Factor {
  readonly name = 'sma';
  constructor(private _window: number = 20) {}
  compute(prices: number[]): (number | undefined)[] {
    return prices.map((_, i) => {
      if (i < this._window - 1) return undefined;
      let sum = 0;
      for (let j = 0; j < this._window; j++) sum += prices[i - j];
      return sum / this._window;
    });
  }
}

export class RSIFactor implements Factor {
  readonly name = 'rsi';
  constructor(private _window: number = 14) {}
  compute(prices: number[]): (number | undefined)[] {
    const result: (number | undefined)[] = [];
    for (let i = 0; i < prices.length; i++) {
      if (i < this._window) { result.push(undefined); continue; }
      let gain = 0, loss = 0;
      for (let j = 0; j < this._window; j++) {
        const diff = prices[i - j] - prices[i - j - 1];
        if (diff > 0) gain += diff; else loss -= diff;
      }
      const avgGain = gain / this._window;
      const avgLoss = loss / this._window;
      if (avgLoss === 0) { result.push(100); continue; }
      const rs = avgGain / avgLoss;
      result.push(100 - 100 / (1 + rs));
    }
    return result;
  }
}

export class VolatilityFactor implements Factor {
  readonly name = 'volatility';
  constructor(private _window: number = 20) {}
  compute(prices: number[]): (number | undefined)[] {
    const result: (number | undefined)[] = [];
    for (let i = 0; i < prices.length; i++) {
      if (i < this._window) { result.push(undefined); continue; }
      const returns: number[] = [];
      for (let j = 0; j < this._window; j++) {
        returns.push(Math.log(prices[i - j] / prices[i - j - 1]));
      }
      const mean = returns.reduce((a, b) => a + b, 0) / this._window;
      const variance = returns.reduce((a, b) => a + (b - mean) ** 2, 0) / this._window;
      result.push(Math.sqrt(variance) * Math.sqrt(252));
    }
    return result;
  }
}

export class FactorRegistry {
  private static _factors: Map<string, Factor> = new Map();
  static register(f: Factor): void { this._factors.set(f.name, f); }
  static get(name: string): Factor | undefined { return this._factors.get(name); }
  static list(): string[] { return Array.from(this._factors.keys()); }
  static computeAll(prices: number[]): Record<string, (number | undefined)[]> {
    const result: Record<string, (number | undefined)[]> = {};
    this._factors.forEach((f, name) => { result[name] = f.compute(prices); });
    return result;
  }
  static clear(): void { this._factors.clear(); }
}

export function registerDefaultFactors(): void {
  FactorRegistry.clear();
  FactorRegistry.register(new SMAFactor(5));
  FactorRegistry.register(new SMAFactor(20));
  FactorRegistry.register(new RSIFactor(14));
  FactorRegistry.register(new VolatilityFactor(20));
}
