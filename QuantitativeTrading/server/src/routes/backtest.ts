import { Router, Request, Response } from 'express';
import { getDB, persist } from '../db';
import { BacktestEngine } from '../../../core/src/backtest/engine';
import { MaCrossStrategy } from '../../../core/examples/ma_cross';
import { BarData, Exchange } from '../../../core/src/common/models';

export const backtestRouter = Router();

// POST /api/backtest/run
backtestRouter.post('/run', async (req: Request, res: Response) => {
  try {
    const { symbol = 'TEST', exchange = 'SHZ', initialCapital = 1000000, fastPeriod = 5, slowPeriod = 20 } = req.body;

    const bars: BarData[] = [];
    let price = 100;
    for (let i = 0; i < 252; i++) {
      price *= 1 + (Math.random() - 0.5) * 0.03;
      const d = new Date(2024, 0, 1);
      d.setDate(d.getDate() + i);
      bars.push({
        symbol, exchange: exchange as Exchange, datetime: d,
        open: price * (1 - Math.random() * 0.01),
        high: price * (1 + Math.random() * 0.02),
        low: price * (1 - Math.random() * 0.02),
        close: price,
        volume: Math.floor(Math.random() * 1e7) + 1e6,
      });
    }

    const engine = new BacktestEngine({ initialCapital, commissionRate: 0.0003, slippageRate: 0.0001 });
    const result = await engine.run(MaCrossStrategy, bars, fastPeriod, slowPeriod);
    const stats = result.getStats();

    const db = getDB();
    db.run(`INSERT INTO backtest_results (strategy_name, symbol, initial_capital, final_equity, total_return, sharpe_ratio, max_drawdown, total_trades, started_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, datetime('now'))`,
      ['MA Cross', symbol, initialCapital, stats.finalEquity, stats.totalReturn, stats.sharpeRatio, stats.maxDrawdown, stats.totalTrades]);

    for (const trade of result.trades) {
      db.run(`INSERT INTO trades (result_id, symbol, direction, price, volume, datetime, commission) VALUES (?, ?, ?, ?, ?, ?, ?)`,
        [db.exec("SELECT last_insert_rowid()")[0]?.values[0][0] ?? 0, trade.symbol, trade.direction, trade.price, trade.volume, new Date(trade.datetime).toISOString(), trade.commission ?? 0]);
    }
    persist();

    res.json({ success: true, stats, trades: result.trades, equityCurve: result.equityCurve });
  } catch (err: any) {
    res.status(500).json({ success: false, error: err.message });
  }
});

// GET /api/backtest/history
backtestRouter.get('/history', (_req: Request, res: Response) => {
  const db = getDB();
  const results = db.exec(`SELECT * FROM backtest_results ORDER BY finished_at DESC LIMIT 20`);
  const rows = results[0]?.values.map(row => {
    const obj: Record<string, any> = {};
    results[0].columns.forEach((col, i) => { obj[col] = row[i]; });
    return obj;
  }) ?? [];
  res.json({ success: true, data: rows });
});
