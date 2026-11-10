import { Strategy } from '../src/strategy/base';
import { BarData, OrderType, Exchange } from '../src/common/models';
import { TradingEngine } from '../src/trading/engine';
import { SimGateway } from '../src/trading/sim_gateway';
import { AccountManager } from '../src/trading/account';
import { logger } from '../src/common/logger';

class DemoStrategy extends Strategy {
  onBar(bar: BarData): void {
    if (bar.close > 10 && this.ctx.cash > bar.close * 100) {
      this.buy(bar.symbol, bar.exchange, bar.close, 100);
      logger.info(`Buy ${bar.symbol} 100@${bar.close}`);
    }
    const pos = this.ctx.getPosition(bar.symbol);
    if (pos && pos.volume > 0 && bar.close > pos.price * 1.05) {
      this.sell(bar.symbol, bar.exchange, bar.close, pos.volume);
      logger.info(`Sell ${bar.symbol} ${pos.volume}@${bar.close} (+5%)`);
    }
  }
}

function main() {
  const gateway = new SimGateway();
  const engine = new TradingEngine(gateway, new AccountManager(100_000));
  engine.addStrategy(new DemoStrategy());
  engine.subscribe('000001');
  engine.start();

  const prices = [10.0, 10.2, 10.5, 10.8, 11.0, 10.9, 10.7, 10.3, 9.8, 9.5];
  for (let i = 0; i < prices.length; i++) {
    const bar: BarData = {
      symbol: '000001',
      exchange: Exchange.SHZ,
      datetime: new Date(2024, 0, i + 1),
      open: prices[i] - 0.1,
      high: prices[i] + 0.2,
      low: prices[i] - 0.2,
      close: prices[i],
      volume: 1_000_000,
    };
    gateway.simulateBar(bar);
  }

  console.log('\nFinal:', JSON.stringify(engine.account.summary(), null, 2));
  engine.stop();
}

main();
