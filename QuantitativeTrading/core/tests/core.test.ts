import { Strategy } from '../src/strategy/base';
import { BarData, Exchange, Direction } from '../src/common/models';
import { StrategyContext } from '../src/strategy/context';
import { SMAFactor, RSIFactor } from '../src/factors/index';
import { PortfolioAllocator, portfolioStats } from '../src/portfolio/index';

test('strategy buy sell', () => {
  const ctx = new StrategyContext(100000);
  const strategy = new (class extends Strategy {
    onBar(bar: BarData): void {
      this.buy(bar.symbol, bar.exchange, bar.close, 100);
    }
  })();
  strategy.ctx = ctx;
  strategy.onInit();

  const bar: BarData = { symbol: '000001', exchange: Exchange.SHZ, datetime: new Date(), open: 10, high: 11, low: 9, close: 10.5, volume: 1e6 };
  strategy.onBar(bar);
  const orders = ctx.popPendingOrders();
  expect(orders.length).toBe(1);
  expect(orders[0].direction).toBe(Direction.LONG);
});

test('backtest engine', async () => {
  const { BacktestEngine } = await import('../src/backtest/engine');
  const bars: BarData[] = [];
  for (let i = 0; i < 10; i++) {
    bars.push({ symbol: 'TEST', exchange: Exchange.SHZ, datetime: new Date(2024, 0, i + 1), open: 10 + i * 0.1, high: 11 + i * 0.1, low: 9 + i * 0.1, close: 10.5 + i * 0.1, volume: 1e6 });
  }

  const engine = new BacktestEngine({ initialCapital: 100000 });
  const result = await engine.run(class extends Strategy {
    onBar(bar: BarData): void {
      if (this.ctx.equityCurve.length === 0) this.buy(bar.symbol, bar.exchange, bar.close, 100);
      if (this.ctx.equityCurve.length === 9) {
        const pos = this.ctx.getPosition(bar.symbol);
        if (pos) this.sell(bar.symbol, bar.exchange, bar.close, pos.volume);
      }
    }
  }, bars);

  expect(result.trades.length).toBeGreaterThanOrEqual(2);
  expect(result.finalEquity).toBeGreaterThan(0);
  const stats = result.getStats();
  expect(stats.sharpeRatio).toBeDefined();
  expect(stats.maxDrawdown).toBeDefined();
});

test('factor computation', () => {
  const prices = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
  const sma = new SMAFactor(3);
  const result = sma.compute(prices);
  expect(result[2]).toBe(2);
  expect(result[3]).toBe(3);
});

test('portfolio allocation', () => {
  const w = PortfolioAllocator.allocate(['a', 'b', 'c'], 'equal');
  const sum = (Object.values(w) as number[]).reduce((a, b) => a + b, 0);
  expect(sum).toBeCloseTo(1);
  const stats = portfolioStats([0.01, -0.005, 0.02, 0.015, -0.01]);
  expect(stats.totalReturn).toBeDefined();
  expect(stats.sharpeRatio).toBeDefined();
});
