import { useEffect, useState } from 'react';
import { ResponsiveContainer, AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip } from 'recharts';

export default function Monitor() {
  const [wsStatus, setWsStatus] = useState('disconnected');
  const [messages, setMessages] = useState<string[]>([]);
  const [equity, setEquity] = useState<any[]>([]);

  useEffect(() => {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${protocol}//${window.location.host}`);

    ws.onopen = () => { setWsStatus('connected'); };
    ws.onmessage = (e) => {
      const data = JSON.parse(e.data);
      setMessages(prev => [`[${data.type}] ${data.message}`, ...prev].slice(0, 50));
    };
    ws.onclose = () => { setWsStatus('disconnected'); };
    return () => ws.close();
  }, []);

  useEffect(() => {
    const data: any[] = [];
    let eq = 100000;
    for (let i = 0; i < 60; i++) {
      eq *= 1 + (Math.random() - 0.5) * 0.005;
      data.push({ time: `${i}s`, equity: Math.round(eq) });
    }
    setEquity(data);
    const interval = setInterval(() => {
      setEquity(prev => {
        const last = prev[prev.length - 1]?.equity ?? 100000;
        const newEq = Math.round(last * (1 + (Math.random() - 0.5) * 0.005));
        return [...prev.slice(-59), { time: `${parseInt(prev[prev.length - 1]?.time ?? '0') + 1}s`, equity: newEq }];
      });
    }, 1000);
    return () => clearInterval(interval);
  }, []);

  return (
    <div>
      <h1 style={{ fontSize: 24, marginBottom: 24 }}>Real-time Monitor</h1>

      <div style={{ display: 'flex', gap: 16, marginBottom: 24 }}>
        <StatusCard label="WebSocket" value={wsStatus} color={wsStatus === 'connected' ? '#00d4aa' : '#f97373'} />
        <StatusCard label="Current Equity" value={equity.length > 0 ? equity[equity.length - 1].equity.toLocaleString() : '--'} color="#4facfe" />
        <StatusCard label="Strategies" value="2 Active" color="#e8c76a" />
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: 20 }}>
        <div style={{ background: '#1a2332', borderRadius: 12, padding: 20, border: '1px solid #2a3a4a' }}>
          <h3 style={{ marginBottom: 16 }}>Live Equity</h3>
          <ResponsiveContainer width="100%" height={300}>
            <AreaChart data={equity}>
              <defs>
                <linearGradient id="liveEq" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#4facfe" stopOpacity={0.3} />
                  <stop offset="95%" stopColor="#4facfe" stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="#2a3a4a" />
              <XAxis dataKey="time" stroke="#667" fontSize={11} />
              <YAxis stroke="#667" fontSize={11} />
              <Tooltip contentStyle={{ background: '#1a2332', border: '1px solid #2a3a4a', borderRadius: 8 }} />
              <Area type="monotone" dataKey="equity" stroke="#4facfe" fill="url(#liveEq)" strokeWidth={2} />
            </AreaChart>
          </ResponsiveContainer>
        </div>

        <div style={{ background: '#1a2332', borderRadius: 12, padding: 20, border: '1px solid #2a3a4a' }}>
          <h3 style={{ marginBottom: 16 }}>Event Log</h3>
          <div style={{ height: 300, overflow: 'auto', fontSize: 12, fontFamily: 'monospace' }}>
            {messages.map((msg, i) => (
              <div key={i} style={{ padding: '4px 0', borderBottom: '1px solid #1e2d3d', color: '#889' }}>
                {msg}
              </div>
            ))}
            {messages.length === 0 && <div style={{ color: '#556' }}>Waiting for events...</div>}
          </div>
        </div>
      </div>
    </div>
  );
}

function StatusCard({ label, value, color }: { label: string; value: string; color: string }) {
  return (
    <div style={{ flex: 1, background: '#1a2332', borderRadius: 12, padding: 20, border: '1px solid #2a3a4a' }}>
      <div style={{ color: '#8899aa', fontSize: 13, marginBottom: 8 }}>{label}</div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 18, fontWeight: 600, color }}>
        <span style={{ width: 8, height: 8, borderRadius: '50%', background: color, display: 'inline-block' }} />
        {value}
      </div>
    </div>
  );
}
