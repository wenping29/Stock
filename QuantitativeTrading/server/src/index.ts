import express from 'express';
import cors from 'cors';
import http from 'http';
import { WebSocketServer, WebSocket } from 'ws';
import path from 'path';
import { initDB } from './db';
import { backtestRouter } from './routes/backtest';

const app = express();
app.use(cors());
app.use(express.json());

// API routes
app.use('/api/backtest', backtestRouter);

// Serve frontend
const clientDist = path.resolve(__dirname, '../../client/dist');
app.use(express.static(clientDist));
app.get('*', (_req, res) => {
  res.sendFile(path.join(clientDist, 'index.html'));
});

// HTTP server
const server = http.createServer(app);

// WebSocket for real-time updates
const wss = new WebSocketServer({ server });
wss.on('connection', (ws: WebSocket) => {
  console.log('Client connected');
  ws.send(JSON.stringify({ type: 'connected', message: 'QuantServer ready' }));
  ws.on('close', () => console.log('Client disconnected'));
});

// Broadcast helper
export function broadcast(data: any): void {
  wss.clients.forEach(client => {
    if (client.readyState === WebSocket.OPEN) {
      client.send(JSON.stringify(data));
    }
  });
}

// Init DB and start
initDB();

const PORT = process.env.PORT || 3000;
server.listen(PORT, () => {
  console.log(`QuantServer running at http://localhost:${PORT}`);
});
