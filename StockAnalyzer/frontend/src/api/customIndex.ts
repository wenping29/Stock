import http from './http'
import type { CustomIndexItem, CustomIndexPreset, CustomIndexPresetDetail } from '../types'
import type { KlineData, KlinePeriod } from '../types/analysis'

export interface CustomIndexPayload {
  name: string
  baseValue?: number
  components: { symbol: string; name: string; weight: number }[]
}

/** 当前用户的自定义指数清单（含最新点位与近 30 日走势）。 */
export async function fetchCustomIndices(): Promise<CustomIndexItem[]> {
  const { data } = await http.get('/custom-index')
  return data
}

export async function createCustomIndex(payload: CustomIndexPayload): Promise<{ id: number }> {
  const { data } = await http.post('/custom-index', payload)
  return data
}

export async function updateCustomIndex(id: number, payload: CustomIndexPayload): Promise<void> {
  await http.put(`/custom-index/${id}`, payload)
}

export async function deleteCustomIndex(id: number): Promise<void> {
  await http.delete(`/custom-index/${id}`)
}

/** 可快速导入的预设指数列表。 */
export async function fetchCustomIndexPresets(): Promise<CustomIndexPreset[]> {
  const { data } = await http.get('/custom-index/presets')
  return data
}

/** 预设指数成分股与权重。 */
export async function fetchCustomIndexPreset(key: string): Promise<CustomIndexPresetDetail> {
  const { data } = await http.get(`/custom-index/presets/${key}`)
  return data
}

/** 自定义指数合成 K 线（日/周/月/季/年，60 分钟不支持）。 */
export async function fetchCustomIndexKline(
  id: number,
  start: string,
  end: string,
  period: KlinePeriod = 'daily',
  limit?: number,
): Promise<KlineData[]> {
  const params: Record<string, string | number> = { period }
  if (start) params.start = start
  if (end) params.end = end
  if (limit) params.limit = limit
  const { data } = await http.get(`/custom-index/${id}/kline`, { params })
  return data
}
