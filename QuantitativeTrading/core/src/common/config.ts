import * as fs from 'fs';
import * as path from 'path';

export interface ConfigData {
  [key: string]: any;
}

export class Settings {
  private static _instance: Settings;
  private _data: ConfigData = {};
  private _configDir: string;

  private constructor() {
    this._configDir = path.resolve(__dirname, '../../config');
    const defaultPath = path.join(this._configDir, 'default.json');
    if (fs.existsSync(defaultPath)) {
      this.load(defaultPath);
    }
  }

  static get instance(): Settings {
    if (!Settings._instance) {
      Settings._instance = new Settings();
    }
    return Settings._instance;
  }

  load(filePath: string): void {
    const raw = fs.readFileSync(filePath, 'utf-8');
    this._data = JSON.parse(raw);
  }

  get(key: string, defaultValue?: any): any {
    const keys = key.split('.');
    let val: any = this._data;
    for (const k of keys) {
      if (val && typeof val === 'object' && k in val) {
        val = val[k];
      } else {
        return defaultValue;
      }
    }
    return val;
  }

  set(key: string, value: any): void {
    const keys = key.split('.');
    let target = this._data;
    for (let i = 0; i < keys.length - 1; i++) {
      if (!(keys[i] in target)) target[keys[i]] = {};
      target = target[keys[i]];
    }
    target[keys[keys.length - 1]] = value;
  }

  get dataDir(): string { return this.get('data.dir', 'data'); }
  get logLevel(): string { return this.get('log.level', 'info'); }
  get timezone(): string { return this.get('system.timezone', 'Asia/Shanghai'); }

  toJSON(): ConfigData {
    return { ...this._data };
  }
}

export const settings = Settings.instance;
