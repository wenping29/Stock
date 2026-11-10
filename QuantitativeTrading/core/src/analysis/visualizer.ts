import { BacktestResult } from '../backtest/result';

export class Visualizer {
  constructor(private _result?: BacktestResult) {}

  printStats(): void {
    if (!this._result) { console.log('No result'); return; }
    const s = this._result.getStats();
    console.log('='.repeat(50));
    console.log('            Performance Report');
    console.log('='.repeat(50));
    for (const [k, v] of Object.entries(s)) {
      const label = k.replace(/([A-Z])/g, ' $1').replace(/^./, c => c.toUpperCase());
      const val = typeof v === 'number'
        ? (k.includes('Return') || k.includes('Drawdown') ? `${(v * 100).toFixed(2)}%`
          : k.includes('Ratio') ? v.toFixed(2)
          : v.toLocaleString())
        : String(v);
      console.log(`${label.padEnd(25)} ${val.padStart(12)}`);
    }
    console.log('='.repeat(50));
  }
}
