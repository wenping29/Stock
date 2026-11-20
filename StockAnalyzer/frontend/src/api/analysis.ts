import axios from 'axios'
import type {
  BacktestConfig,
  BacktestResult,
  IndicatorResult,
  KlineData,
  KlinePeriod,
  MacroIndicator,
  MonitorJob,
  MonitorJobSummary,
  OptimizationConfig,
  OptimizationResult,
  ParamRange,
  PerformanceMetrics,
  PresetInfo,
  PriceSeriesPoint,
  RealtimeQuote,
  RuleGroup,
  ScreeningResult,
  StockInfo,
  StockListType,
  Trade,
  WalkForwardConfig,
  WalkForwardResult,
  WebhookType,
} from '../types/analysis'

declare global {
  interface Window {
    __APP_CONFIG__?: { apiBase?: string; analysisApiBase?: string }
  }
}

// 量化分析服务（stock-analyzer/server）：
// - 默认走同源 /analysis-api，由 vite / nginx 代理到分析服务
// - 运行时可用 config.js 的 analysisApiBase 直连服务地址
const ANALYSIS_API_BASE = window.__APP_CONFIG__?.analysisApiBase
  ? `${window.__APP_CONFIG__.analysisApiBase.replace(/\/+$/, '')}/api`
  : '/analysis-api'

const analysisHttp = axios.create({ baseURL: ANALYSIS_API_BASE, timeout: 120000 })

export function analysisErrorMessage(e: unknown): string {
  const err = e as { response?: { data?: { error?: string } }; message?: string }
  return err?.response?.data?.error || err?.message || '请求失败'
}

// ---- 股票 ----

export async function fetchStockList(type: StockListType = 'all'): Promise<StockInfo[]> {
  const { data } = await analysisHttp.get('/stock/list', { params: { type } })
  return data.data
}

export async function searchStock(q: string): Promise<StockInfo[]> {
  const { data } = await analysisHttp.get('/stock/search', { params: { q } })
  return data.data
}

export async function fetchStockDetail(code: string): Promise<{ info: StockInfo; quote: RealtimeQuote | null }> {
  const { data } = await analysisHttp.get(`/stock/${code}`)
  return data.data
}

// ---- 行情 ----

export async function fetchMarketIndexes(): Promise<RealtimeQuote[]> {
  const { data } = await analysisHttp.get('/market/indexes')
  return data.data
}

export async function fetchMacroIndicators(): Promise<MacroIndicator[]> {
  const { data } = await analysisHttp.get('/market/macro')
  return data.data
}

export async function fetchUsRateHistory(
  maturity: string,
  start?: string,
  end?: string,
): Promise<{ date: string; rate: number }[]> {
  const { data } = await analysisHttp.get(`/market/us-rates/${maturity}`, { params: { start, end } })
  return data.data
}

export async function fetchCnRateHistory(
  type: string,
  start?: string,
  end?: string,
): Promise<{ date: string; rate: number }[]> {
  const { data } = await analysisHttp.get(`/market/cn-rates/${type}`, { params: { start, end } })
  return data.data
}

export async function fetchIndexKline(
  code: string,
  start: string,
  end: string,
  period: KlinePeriod = 'daily',
): Promise<KlineData[]> {
  const { data } = await analysisHttp.get(`/market/indexes/${code}/kline`, { params: { start, end, period } })
  return data.data
}

export async function fetchKline(
  code: string,
  start: string,
  end: string,
  period: KlinePeriod = 'daily',
  adjust = 'qfq',
): Promise<KlineData[]> {
  const { data } = await analysisHttp.get(`/stock/${code}/kline`, { params: { start, end, period, adjust } })
  return data.data
}

export async function fetchIndexPeriodData(
  period: string,
  codes?: string[],
  start?: string,
  end?: string,
): Promise<Record<string, { name: string; data: PriceSeriesPoint[] }>> {
  const { data } = await analysisHttp.get(`/market/index-data/${period}`, {
    params: { codes: codes?.join(','), start, end },
  })
  return data.data
}

export async function fetchFxCnyUsd(period: string, start?: string, end?: string): Promise<PriceSeriesPoint[]> {
  const { data } = await analysisHttp.get(`/market/fx-cny-usd/${period}`, { params: { start, end } })
  return data.data
}

export async function fetchGoldPrice(period: string, start?: string, end?: string): Promise<PriceSeriesPoint[]> {
  const { data } = await analysisHttp.get(`/market/gold-price/${period}`, { params: { start, end } })
  return data.data
}

export async function fetchCrudeOil(period: string, start?: string, end?: string): Promise<PriceSeriesPoint[]> {
  const { data } = await analysisHttp.get(`/market/crude-oil/${period}`, { params: { start, end } })
  return data.data
}

export async function fetchCn10y(period: string, start?: string, end?: string): Promise<PriceSeriesPoint[]> {
  const { data } = await analysisHttp.get(`/market/cn10y/${period}`, { params: { start, end } })
  return data.data
}

// ---- 指标 / 选股 ----

export async function computeIndicators(
  klineData: KlineData[],
  indicators: { name: string; params: Record<string, number> }[],
): Promise<IndicatorResult[]> {
  const { data } = await analysisHttp.post('/indicators/compute', { klineData, indicators })
  return data.data
}

export async function runScreening(params: {
  stockPool: string
  rules: RuleGroup
  startDate: string
  endDate: string
  period?: KlinePeriod
}): Promise<{ data: ScreeningResult[]; meta: { total: number; matched: number } }> {
  // 全市场筛选耗时随股票池增长，不设超时（与源分析服务一致）
  const resp = await analysisHttp.post('/screening/run', params, { timeout: 0 })
  return { data: resp.data.data, meta: resp.data.meta }
}

export async function fetchPresets(): Promise<PresetInfo[]> {
  const { data } = await analysisHttp.get('/screening/presets')
  return data.data
}

export async function loadPreset(name: string): Promise<RuleGroup> {
  const { data } = await analysisHttp.get(`/screening/presets/${name}`)
  return data.data.rules
}

// ---- 回测 ----

export async function runBacktest(config: BacktestConfig): Promise<BacktestResult & { id?: string }> {
  const { data } = await analysisHttp.post('/backtesting/run', config, { timeout: 0 })
  return data.data
}

export async function getBacktestResults(): Promise<{ id: string; name: string; created_at: string }[]> {
  const { data } = await analysisHttp.get('/backtesting/results')
  return data.data
}

export async function getBacktestResult(id: string): Promise<{
  id: string
  name: string
  config: BacktestConfig
  metrics: PerformanceMetrics
  equityCurve: { date: string; value: number }[]
  trades: Trade[]
}> {
  const { data } = await analysisHttp.get(`/backtesting/results/${id}`)
  return data.data
}

// ---- 参数优化 ----

export async function runGridSearch(
  config: OptimizationConfig,
): Promise<{ data: OptimizationResult[]; meta: unknown }> {
  const resp = await analysisHttp.post('/optimization/grid-search', config, { timeout: 0 })
  return { data: resp.data.data, meta: resp.data.meta }
}

export async function runWalkForward(
  config: WalkForwardConfig,
): Promise<{ data: WalkForwardResult[]; meta: unknown }> {
  const resp = await analysisHttp.post('/optimization/walk-forward', config, { timeout: 0 })
  return { data: resp.data.data, meta: resp.data.meta }
}

// ---- 监控 ----

export async function listMonitorJobs(): Promise<MonitorJobSummary[]> {
  const { data } = await analysisHttp.get('/monitoring/jobs')
  if (!data.success) throw new Error(data.error || '获取监控任务失败')
  return data.data
}

export async function createMonitorJob(job: MonitorJob): Promise<void> {
  const { data } = await analysisHttp.post('/monitoring/jobs', job)
  if (!data.success) throw new Error(data.error || '创建失败')
}

export async function deleteMonitorJob(id: string): Promise<void> {
  const { data } = await analysisHttp.delete(`/monitoring/jobs/${id}`)
  if (!data.success) throw new Error(data.error || '删除失败')
}

export async function triggerMonitorJob(id: string): Promise<void> {
  const { data } = await analysisHttp.post(`/monitoring/jobs/${id}/run`)
  if (!data.success) throw new Error(data.error || '触发失败')
}

export async function testAnalysisWebhook(url: string, type: WebhookType): Promise<void> {
  const { data } = await analysisHttp.post('/monitoring/test-webhook', { url, type })
  if (!data.success) throw new Error(data.error || '发送失败')
}

export type { ParamRange }
export default analysisHttp