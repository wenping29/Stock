import http from './http'
import type {
  SearchItem, KLineResponse, PerformanceData, MoneyFlowData, WatchlistItem, AdminUser,
  MarketIndex, MarketVolume, HsgtFlow, FxRate, Us10y, CnBond, IndexItem, IndexTrendData, SectorFlow,
  MarketVolumeToday, IndexEtf,
} from '../types'

export async function fetchMarketOverview(): Promise<MarketIndex[]> {
  const { data } = await http.get('/quote/market-overview')
  return data
}

export async function fetchMarketVolume(days = 30): Promise<MarketVolume> {
  const { data } = await http.get('/quote/market-volume', { params: { days } })
  return data
}

export async function fetchMarketVolumeSeries(
  granularity: 'day' | 'week' | 'month' | 'quarter' | 'year',
): Promise<MarketVolume> {
  const { data } = await http.get('/quote/market-volume-series', { params: { granularity } })
  return data
}

/** 今日两市成交额分时累计曲线（读库分钟成交额）。 */
export async function fetchMarketVolumeToday(): Promise<MarketVolumeToday> {
  const { data } = await http.get('/quote/market-volume-today')
  return data
}

export async function fetchHsgtFlow(days = 30): Promise<HsgtFlow> {
  const { data } = await http.get('/quote/hsgt-flow', { params: { days } })
  return data
}

/** 行业板块主力资金流（最新交易日快照，金额单位：亿元）。 */
export async function fetchSectorFlow(): Promise<SectorFlow> {
  const { data } = await http.get('/quote/sector-moneyflow')
  return data
}

export async function fetchFxRate(days = 90): Promise<FxRate> {
  const { data } = await http.get('/quote/fx-rate', { params: { days } })
  return data
}

export async function fetchUs10y(days = 90): Promise<Us10y> {
  const { data } = await http.get('/quote/us10y', { params: { days } })
  return data
}

export async function fetchCnBond(days = 90): Promise<CnBond> {
  const { data } = await http.get('/quote/cn-bond', { params: { days } })
  return data
}

/** 指数清单；scope='overview' 时只取大盘行情页的 12 个核心指数。 */
export async function fetchIndices(scope?: 'overview'): Promise<IndexItem[]> {
  const { data } = await http.get('/quote/indices', { params: scope ? { scope } : undefined })
  return data.items
}

/** 指数日内分时走势（东财实时，后端按指数缓存 1 分钟）。 */
export async function fetchIndexTrend(code: string): Promise<IndexTrendData> {
  const { data } = await http.get('/quote/index-trend', { params: { code } })
  return data
}

/** 指数关联的场内 ETF（后端按指数缓存 1 分钟）。 */
export async function fetchIndexEtfs(code: string): Promise<IndexEtf[]> {
  const { data } = await http.get('/quote/index-etfs', { params: { code } })
  return data
}

export async function searchSymbol(keyword: string): Promise<SearchItem[]> {
  const { data } = await http.get('/quote/search', { params: { keyword } })
  return data
}

export async function fetchKLine(symbol: string, period: 'hour' | 'day' | 'week' | 'month', limit = 120): Promise<KLineResponse> {
  const { data } = await http.get('/quote/kline', { params: { symbol, period, limit } })
  return data
}

export async function fetchPerformance(symbol: string): Promise<PerformanceData> {
  const { data } = await http.get('/quote/performance', { params: { symbol } })
  return data
}

export async function fetchMoneyFlow(symbol: string, days = 30): Promise<MoneyFlowData> {
  const { data } = await http.get('/quote/moneyflow', { params: { symbol, days } })
  return data
}

export async function fetchWatchlist(): Promise<WatchlistItem[]> {
  const { data } = await http.get('/watchlist')
  return data
}

export async function addToWatchlist(symbol: string): Promise<{ message: string }> {
  const { data } = await http.post('/watchlist', { symbol })
  return data
}

export async function removeFromWatchlist(symbol: string): Promise<void> {
  await http.delete(`/watchlist/${symbol}`)
}

// ---------- 管理后台 ----------
export async function fetchUsers(): Promise<AdminUser[]> {
  const { data } = await http.get('/admin/users')
  return data
}

export async function updateUserRole(id: string, role: string): Promise<void> {
  await http.put(`/admin/users/${id}/role`, { role })
}

export async function updateUserEnabled(id: string, enabled: boolean): Promise<void> {
  await http.put(`/admin/users/${id}/enabled`, { enabled })
}

export async function triggerCollect(what: 'initial' | 'daily' | 'minutes' | 'indices'): Promise<void> {
  await http.post(`/admin/collect/${what}`)
}
