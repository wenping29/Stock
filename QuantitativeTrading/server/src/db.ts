import initSqlJs, { Database } from 'sql.js';
import fs from 'fs';
import path from 'path';

let db: Database;
const DB_PATH = path.resolve(__dirname, '../../core/data/quant.db');

export async function initDB(): Promise<void> {
  const SQL = await initSqlJs();
  
  if (fs.existsSync(DB_PATH)) {
    const buffer = fs.readFileSync(DB_PATH);
    db = new SQL.Database(buffer);
  } else {
    db = new SQL.Database();
    const dir = path.dirname(DB_PATH);
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
  }

  db.run(`
    CREATE TABLE IF NOT EXISTS strategies (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      name TEXT NOT NULL,
      code TEXT,
      created_at TEXT DEFAULT (datetime('now'))
    )
  `);
  
  db.run(`
    CREATE TABLE IF NOT EXISTS backtest_results (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      strategy_name TEXT,
      symbol TEXT,
      initial_capital REAL,
      final_equity REAL,
      total_return REAL,
      sharpe_ratio REAL,
      max_drawdown REAL,
      total_trades INTEGER,
      started_at TEXT,
      finished_at TEXT DEFAULT (datetime('now'))
    )
  `);

  db.run(`
    CREATE TABLE IF NOT EXISTS trades (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      result_id INTEGER,
      symbol TEXT,
      direction TEXT,
      price REAL,
      volume REAL,
      datetime TEXT,
      commission REAL
    )
  `);

  saveDB();
}

function saveDB(): void {
  const data = db.export();
  const buffer = Buffer.from(data);
  fs.writeFileSync(DB_PATH, buffer);
}

export function getDB(): Database {
  return db;
}

export function persist(): void {
  saveDB();
}
