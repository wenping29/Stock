import { useEffect, useState } from 'react';
import { XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Area, AreaChart } from 'recharts';

export default function Dashboard() {
  const [stats, setStats] = useState<any>(null);
  const [equity, setEquity] = useState<any[]>([]);

  useEffect(() => {
    fetch('/api/backtest/history')
      .then(r => r.json())
      .then(d => {
        if (d.success && d.data.length > 0) setStats(d.data[0]);
      });
    // Generate demo equity data
    const data: any[] = [];
    let eq = 1000000;
    for (let i = 0; i < 252; i++) {
      eq *= 1 + (Math.random() - 0.5) * 0.01;
      data.push({ day: i + 1, equity: Math.round(eq) });
    }
    setEquity(data);
  }, []);

  const cards = [
    { label: 'Total Return', value: stats ? `${(stats.total_return * 100).toFixed(2)}%` : '--', color: '#00d4aa' },
    { label: 'Sharpe Ratio', value: stats ? stats.sharpe_ratio.toFixed(2) : '--', color: '#4facfe' },
    { label: 'Max Drawdown', value: stats ? `${(stats.max_drawdown * 100).toFixed(2)}%` : '--', color: '#f97373' },
    { label: 'Total Trades', value: stats ? stats.total_trades : '--', color: '#e8c76a' },
  ];

  return (
    <div>
      <h1 style={{ fontSize: 24, marginBottom: 24 }}>Dashboard</h1>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 16, marginBottom: 24 }}>
        {cards.map(c => (
          <div key={c.label} style={{ background: '#1a2332', borderRadius: 12, padding: 20, border: '1px solid #2a3a4a' }}>
            <div style={{ color: '#8899aa', fontSize: 13, marginBottom: 8 }}>{c.label}</div>
            <div style={{ fontSize: 28, fontWeight: 700, color: c.color }}>{c.value}</div>
          </div>
        ))}
      </div>

      <div style={{ background: '#1a2332', borderRadius: 12, padding: 20, border: '1px solid #2a3a4a' }}>
        <h3 style={{ marginBottom: 16 }}>Equity Curve</h3>
        <ResponsiveContainer width="100%" height={350}>
          <AreaChart data={equity}>
            <defs>
              <linearGradient id="colorEq" x1="0" y1="0" x2="0" y2="1">
                <stop offset="5%" stopColor="#00d4aa" stopOpacity={0.3} />
                <stop offset="95%" stopColor="#00d4aa" stopOpacity={0} />
              </linearGradient>
            </defs>
            <CartesianGrid strokeDasharray="3 3" stroke="#2a3a4a" />
            <XAxis dataKey="day" stroke="#667" fontSize={12} />
            <YAxis stroke="#667" fontSize={12} />
            <Tooltip contentStyle={{ background: '#1a2332', border: '1px solid #2a3a4a', borderRadius: 8 }} />
            <Area type="monotone" dataKey="equity" stroke="#00d4aa" fill="url(#colorEq)" strokeWidth={2} />
          </AreaChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}
