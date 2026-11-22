import http from './http'
import type { StockListResponse, BoardStat } from '../types'

export async function fetchStocks(params: {
  board?: string
  keyword?: string
  industry?: string
  sort?: string
  desc?: boolean
  page?: number
  pageSize?: number
  /** 数值区间筛选（weightMin/weightMax、peMin/peMax 等），仅传已填写的项 */
  filters?: Record<string, number>
}): Promise<StockListResponse> {
  const { filters, ...rest } = params
  const { data } = await http.get('/stocks', { params: { ...rest, ...(filters ?? {}) } })
  return data
}

export async function fetchBoardStats(): Promise<BoardStat[]> {
  const { data } = await http.get('/stocks/boards')
  return data
}
