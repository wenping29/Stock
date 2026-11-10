import { BarData, Exchange } from '../src/common/models';
import { BacktestEngine } from '../src/backtest/engine';
import { Visualizer } from '../src/analysis/visualizer';
import { MaCrossStrategy } from './ma_cross';

async function main() {
  // Generate mock data
  const bars: BarData[] = [];
  let price = 100;
  for (let i = 0; i < 252; i++) {
    price *= 1 + (Math.random() - 0.5) * 0.03;
    const d = new Date(2024, 0, 1);
    d.setDate(d.getDate() + i);
    bars.push({
      symbol: 'TEST',
      exchange: Exchange.SHZ,
      datetime: d,
      open: price * (1 - Math.random() * 0.01),
      high: price * (1 + Math.random() * 0.02),
      low: price * (1 - Math.random() * 0.02),
      close: price,
      volume: Math.floor(Math.random() * 10000000) + 1000000,
    });
  }

  const engine = new BacktestEngine({ initialCapital: 1_000_000 });
  const result = await engine.run(MaCrossStrategy, bars, 5, 20);

  const viz = new Visualizer(result);
  viz.printStats();
}

main().catch(console.error);
