import { BrowserRouter, Routes, Route, NavLink } from 'react-router-dom';
import Dashboard from './pages/Dashboard';
import Backtest from './pages/Backtest';
import Monitor from './pages/Monitor';

function App() {
  return (
    <BrowserRouter>
      <div style={{ display: 'flex', minHeight: '100vh', background: '#0f1923', color: '#e0e0e0' }}>
        <Sidebar />
        <main style={{ flex: 1, padding: 24, overflow: 'auto' }}>
          <Routes>
            <Route path="/" element={<Dashboard />} />
            <Route path="/backtest" element={<Backtest />} />
            <Route path="/monitor" element={<Monitor />} />
          </Routes>
        </main>
      </div>
    </BrowserRouter>
  );
}

function Sidebar() {
  const links = [
    { to: '/', label: '📊 Dashboard', icon: '📊' },
    { to: '/backtest', label: '🔬 Backtest', icon: '🔬' },
    { to: '/monitor', label: '📡 Monitor', icon: '📡' },
  ];
  return (
    <nav style={{ width: 200, background: '#1a2332', padding: 20, borderRight: '1px solid #2a3a4a' }}>
      <h2 style={{ color: '#00d4aa', fontSize: 18, marginBottom: 30 }}>QuantPlatform</h2>
      {links.map(l => (
        <NavLink
          key={l.to}
          to={l.to}
          style={({ isActive }) => ({
            display: 'block', padding: '10px 14px', marginBottom: 8,
            borderRadius: 8, textDecoration: 'none', color: '#c0d0e0',
            background: isActive ? '#00d4aa22' : 'transparent',
            borderLeft: isActive ? '3px solid #00d4aa' : '3px solid transparent',
          })}
        >
          {l.icon} {l.label}
        </NavLink>
      ))}
    </nav>
  );
}

export default App;
