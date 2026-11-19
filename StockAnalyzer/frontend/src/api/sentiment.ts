import http from './http'
import type { SentimentOverview, SentimentHistory, SentimentStructure } from '../types'

export async function fetchSentimentOverview(): Promise<SentimentOverview> {
  const { data } = await http.get('/sentiment/overview')
  return data
}

export async function fetchSentimentHistory(days = 60): Promise<SentimentHistory> {
  const { data } = await http.get('/sentiment/history', { params: { days } })
  return data
}

export async function fetchSentimentStructure(): Promise<SentimentStructure> {
  const { data } = await http.get('/sentiment/structure')
  return data
}
