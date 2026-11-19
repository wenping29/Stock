<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts'
import type { SentimentHistoryPoint } from '../types'

// 情绪历史：上涨/下跌家数用零轴上下对称的柱形（红上绿下），情绪分用右轴折线
const props = defineProps<{ points: SentimentHistoryPoint[] }>()

const el = ref<HTMLElement>()
let chart: echarts.ECharts | null = null

function render() {
  if (!chart) return
  const dates = props.points.map((p) => p.date.slice(5))
  const up = props.points.map((p) => p.upCount)
  const down = props.points.map((p) => -p.downCount)
  const score = props.points.map((p) => p.sentimentScore)

  chart.setOption({
    animation: false,
    tooltip: {
      trigger: 'axis',
      formatter: (items: any[]) => {
        const i = items[0]?.dataIndex ?? 0
        const p = props.points[i]
        if (!p) return ''
        const cls = (v: number) => (v > 0 ? 'color:#ef232a' : v < 0 ? 'color:#14b143' : '')
        return [
          `<b>${p.date}</b>`,
          `上涨 <b style="${cls(p.upCount)}">${p.upCount}</b> · 下跌 <b style="${cls(-p.downCount)}">${p.downCount}</b> · 平盘 ${p.flatCount}`,
          `涨停 ${p.limitUpCount} · 跌停 ${p.limitDownCount} · 炸板 ${p.brokenBoardCount}`,
          `情绪分 <b>${p.sentimentScore.toFixed(1)}</b> · 成交 ${(p.totalAmount / 10000).toFixed(2)} 万亿`,
        ].join('<br/>')
      },
    },
    legend: { top: 0, data: ['上涨家数', '下跌家数', '情绪分'] },
    grid: { left: 56, right: 52, top: 34, bottom: 48 },
    xAxis: {
      type: 'category',
      data: dates,
      boundaryGap: true,
      axisLine: { lineStyle: { color: '#c0c4cc' } },
    },
    yAxis: [
      {
        type: 'value',
        name: '家数',
        splitLine: { lineStyle: { color: '#f0f0f0' } },
        axisLabel: { formatter: (v: number) => `${Math.abs(v)}` },
      },
      {
        type: 'value',
        name: '情绪分',
        min: 0,
        max: 100,
        splitLine: { show: false },
      },
    ],
    dataZoom: [{ type: 'inside', start: 0, end: 100 }],
    series: [
      {
        name: '上涨家数',
        type: 'bar',
        stack: 'breadth',
        barMaxWidth: 16,
        itemStyle: { color: '#ef232a' },
        data: up,
      },
      {
        name: '下跌家数',
        type: 'bar',
        stack: 'breadth',
        barMaxWidth: 16,
        itemStyle: { color: '#14b143' },
        data: down,
        tooltip: { valueFormatter: (v: number) => `${Math.abs(v)}` },
      },
      {
        name: '情绪分',
        type: 'line',
        yAxisIndex: 1,
        showSymbol: false,
        smooth: true,
        lineStyle: { color: '#409eff', width: 2 },
        itemStyle: { color: '#409eff' },
        data: score,
        markLine: {
          silent: true,
          symbol: 'none',
          label: { formatter: (p: any) => p.name, fontSize: 10, color: '#909399' },
          lineStyle: { type: 'dashed', color: '#dcdfe6' },
          data: [
            { yAxis: 20, name: '冰点' },
            { yAxis: 40, name: '低迷' },
            { yAxis: 60, name: '活跃' },
            { yAxis: 80, name: '亢奋' },
          ],
        },
      },
    ],
  })
}

onMounted(() => {
  chart = echarts.init(el.value!)
  render()
  window.addEventListener('resize', resize)
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', resize)
  chart?.dispose()
})

function resize() {
  chart?.resize()
}

watch(() => props.points, render, { deep: true })
</script>

<template>
  <div ref="el" class="chart-box-sm" />
</template>
