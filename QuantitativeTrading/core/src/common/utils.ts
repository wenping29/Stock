export function parseStockCode(code: string): [string, string] {
  const upper = code.toUpperCase().replace(/\s/g, '');
  const match = upper.match(/^(\d{6})(?:\.([SZ][ZH]?|BJ))?$/);
  if (match) {
    const sym = match[1];
    const suffix = match[2];
    if (suffix === 'SH' || (!suffix && sym.startsWith('6'))) return [sym, 'SHH'];
    if (suffix === 'SZ' || (!suffix && /^[023]/.test(sym))) return [sym, 'SHZ'];
    if (suffix === 'BJ' || (!suffix && /^[48]/.test(sym))) return [sym, 'BJ'];
    return [sym, 'SHH'];
  }
  return [code, ''];
}

export function isTradeDate(date?: Date): boolean {
  const d = date ?? new Date();
  const day = d.getDay();
  return day !== 0 && day !== 6;
}

export function getTradeDates(start: Date, end: Date): Date[] {
  const dates: Date[] = [];
  for (let d = new Date(start); d <= end; d.setDate(d.getDate() + 1)) {
    if (isTradeDate(d)) dates.push(new Date(d));
  }
  return dates;
}

export function floorDT(dt: Date, minutes: number = 1): Date {
  const ts = Math.floor(dt.getTime() / 1000);
  return new Date((ts - (ts % (minutes * 60))) * 1000);
}

export function normalizeSymbol(symbol: string): string {
  let s = symbol.toUpperCase().replace(/\s/g, '');
  for (const suffix of ['.SH', '.SZ', '.BJ', '.SS', '.SHH']) {
    if (s.endsWith(suffix)) { s = s.slice(0, -suffix.length); break; }
  }
  return s;
}
