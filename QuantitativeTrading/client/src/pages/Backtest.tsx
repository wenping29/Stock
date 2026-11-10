import { useState } from 'react';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';

export default function Backtest() {
  const [running, setRunning] = useState(false);
  const [result, setResult] = useState<any>(null);
  const [params, setParams] = useState({ initialCapital: 1000000, fastPeriod: 5, slowPeriod: 20 });

  const run = async () => {
    setRunning(true);
    setResult(null);
    try {
      const res = await fetch('/api/backtest/run', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(params),
      });
      const data = await res.json();
      if (data.success) setResult(data);
    } catch (err: any) {
      alert('Error: ' + err.message);
    } finally {
      setRunning(false);
    }
  };

  return (
    <div>
      <h1 style={{ fontSize: 24, marginBottom: 24 }}>Backtest</h1>

      <div style={{ display: 'flex', gap: 16, marginBottom: 24, flexWrap: 'wrap' }}>
        <ParamInput label="Initial Capital" value={params.initialCapital} onChange={v => setParams(p => ({ ...p, initialCapital: v }))} />
        <ParamInput label="Fast MA" value={params.fastPeriod} onChange={v => setParams(p => ({ ...p, fastPeriod: v }))} />
        <ParamInput label="Slow MA" value={params.slowPeriod} onChange={v => setParams(p => ({ ...p, slowPeriod: v }))} />
        <button onClick={run} disabled={running}
          style={{ padding: '10px 28px', background: '#00d4aa', color: '#000', border: 'none', borderRadius: 8, fontSize: 15, fontWeight: 600, cursor: 'pointer', alignSelf: 'flex-end' }}>
          {running ? 'Running...' : '▶ Run'}
        </button>
      </div>

      {result && (
        <>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 16, marginBottom: 24 }}>
            <StatCard label="Total Return" value={`${(result.stats.totalReturn * 100).toFixed(2)}%`} color="#00d4aa" />
            <StatCard label="Sharpe" value={result.stats.sharpeRatio.toFixed(2)} color="#4facfe" />
            <StatCard label="Max DD" value={`${(result.stats.maxDrawdown * 100).toFixed(2)}%`} color="#f97373" />
            <StatCard label="Trades" value={result.stats.totalTrades} color="#e8c76a" />
          </div>

          <div style={{ background: '#1a2332', borderRadius: 12, padding: 20, border: '1px solid #2a3a4a', marginBottom: 20 }}>
            <h3 style={{ marginBottom: 16 }}>Equity Curve</h3>
            <ResponsiveContainer width="100%" height={300}>
              <LineChart data={result.equityCurve.map((e: any, i: number) => ({ day: i + 1, equity: e.equity }))}>
                <CartesianGrid strokeDasharray="3 3" stroke="#2a3a4a" />
                <XAxis dataKey="day" stroke="#667" fontSize={12} />
                <YAxis stroke="#667" fontSize={12} />
                <Tooltip contentStyle={{ background: '#1a2332', border: '1px solid #2a3a4a', borderRadius: 8 }} />
                <Line type="monotone" dataKey="equity" stroke="#00d4aa" strokeWidth={2} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>

          <div style={{ background: '#1a2332', borderRadius: 12, padding: 20, border: '1px solid #2a3a4a' }}>
            <h3 style={{ marginBottom: 16 }}>Trades</h3>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
              <thead>
                <tr style={{ borderBottom: '1px solid #2a3a4a' }}>
                  <th style={{ padding: 8, textAlign: 'left' }}>Symbol</th>
                  <th style={{ padding: 8, textAlign: 'left' }}>Direction</th>
                  <th style={{ padding: 8, textAlign: 'right' }}>Price</th>
                  <th style={{ padding: 8, textAlign: 'right' }}>Volume</th>
                  <th style={{ padding: 8, textAlign: 'right' }}>Commission</th>
                </tr>
              </thead>
              <tbody>
                {result.trades.map((t: any, i: number) => (
                  <tr key={i} style={{ borderBottom: '1px solid #1e2d3d' }}>
                    <td style={{ padding: 8 }}>{t.symbol}</td>
                    <td style={{ padding: 8, color: t.direction === 'long' ? '#00d4aa' : '#f97373' }}>{t.direction}</td>
                    <td style={{ padding: 8, textAlign: 'right' }}>{t.price.toFixed(2)}</td>
                    <td style={{ padding: 8, textAlign: 'right' }}>{t.volume}</td>
                    <td style={{ padding: 8, textAlign: 'right' }}>{(t.commission ?? 0).toFixed(4)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}

function ParamInput({ label, value, onChange }: { label: string; value: number; onChange: (v: number) => void }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
      <label style={{ fontSize: 12, color: '#889' }}>{label}</label>
      <input type="number" value={value} onChange={e => onChange(Number(e.target.value))}
        style={{ padding: '8px 12px', background: '#1a2332', border: '1px solid #2a3a4a', borderRadius: 6, color: '#e0e0e0', fontSize: 14, width: 140 }} />
    </div>
  );
}

function StatCard({ label, value, color }: { label: string; value: string; color: string }) {
  return (
    <div style={{ background: '#1a2332', borderRadius: 12, padding: 20, border: '1px solid #2a3a4a' }}>
      <div style={{ color: '#8899aa', fontSize: 13, marginBottom: 8 }}>{label}</div>
      <div style={{ fontSize: 24, fontWeight: 700, color }}>{value}</div>
    </div>
  );
}
