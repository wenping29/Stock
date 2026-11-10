export class PortfolioAllocator {
  static equalWeight(n: number): number[] {
    return Array(n).fill(1 / n);
  }

  static riskParity(covMatrix: number[][]): number[] {
    const vols = covMatrix.map(row => Math.sqrt(row[0]));
    const total = vols.reduce((a, b) => a + b, 0);
    return vols.map(v => v / total);
  }

  static allocate(names: string[], method: 'equal' | 'risk_parity' = 'equal'): Record<string, number> {
    const n = names.length;
    const weights = method === 'equal' ? this.equalWeight(n) : this.riskParity(Array(n).fill(Array(n).fill(1)));
    const result: Record<string, number> = {};
    names.forEach((name, i) => { result[name] = weights[i]; });
    return result;
  }
}

export function portfolioStats(returns: number[]): Record<string, number> {
  const totalRet = (1 + returns.reduce((a, b) => a * (1 + b), 1)) - 1;
  const meanRet = returns.reduce((a, b) => a + b, 0) / returns.length;
  const stdRet = Math.sqrt(returns.reduce((a, b) => a + (b - meanRet) ** 2, 0) / returns.length);
  const annRet = Math.pow(1 + totalRet, 252 / returns.length) - 1;
  const annVol = stdRet * Math.sqrt(252);
  const sharpe = annVol > 0 ? (annRet - 0.03) / annVol : 0;

  let peak = 1;
  let maxDD = 0;
  let cum = 1;
  for (const r of returns) {
    cum *= (1 + r);
    if (cum > peak) peak = cum;
    const dd = (peak - cum) / peak;
    if (dd > maxDD) maxDD = dd;
  }

  return { totalReturn: totalRet, annualReturn: annRet, sharpeRatio: sharpe, maxDrawdown: maxDD };
}
