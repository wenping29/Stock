<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts'
import type { IndexTrendPoint } from '../types'

// 分时成交量柱状图（关联 ETF 弹窗右侧）：按分钟量红涨绿跌着色
const props = defineProps<{
  points: IndexTrendPoint[]
  prevClose?: number | null
  height?: number
}>()

const el = ref<HTMLElement>()
let chart: echarts.ECharts | null = null

const UP = '#ef232a'
const DOWN = '#14b143'
const TICKS = new Set(['09:30', '10:30', '11:30', '13:00', '14:00', '15:00'])

function render() {
  if (!chart) return
  const pts = props.points
  if (!pts.length) {
    chart.clear()
    return
  }
  const prev = props.prevClose ?? 0
  const useTicks = pts.some((p) => TICKS.has(p.time))
  chart.setOption({
    animation: false,
    grid: { left: 46, right: 8, top: 8, bottom: 20 },
    tooltip: {
      trigger: 'axis',
      confine: true,
      axisPointer: { type: 'shadow' },
      backgroundColor: 'rgba(255,255,255,0.96)',
      borderColor: '#ddd',
      textStyle: { color: '#303133', fontSize: 12 },
      formatter: (ps: any[]) => {
        const p = ps[0]
        const pt = p ? pts[p.dataIndex] : undefined
        if (!pt) return ''
        return `<b>${pt.time}</b><br/>成交量：${(pt.volume / 1e4).toFixed(2)} 万手`
      },
    },
    xAxis: {
      type: 'category',
      data: pts.map((p) => p.time),
      boundaryGap: true,
      axisTick: { show: false },
      axisLine: { lineStyle: { color: '#c0c4cc' } },
      axisLabel: {
        fontSize: 10,
        color: '#909399',
        interval: useTicks ? 0 : 'auto',
        formatter: (v: string) => (useTicks ? (TICKS.has(v) ? v : '') : v),
      },
    },
    yAxis: {
      type: 'value',
      axisLine: { show: false },
      axisTick: { show: false },
      splitLine: { lineStyle: { color: '#f0f2f5' } },
      axisLabel: {
        fontSize: 10,
        color: '#909399',
        formatter: (v: number) => (v >= 1e4 ? `${(v / 1e4).toFixed(0)}万` : `${v}`),
      },
    },
    series: [
      {
        type: 'bar',
        data: pts.map((p) => ({
          value: p.volume,
          itemStyle: { color: prev > 0 && p.price < prev ? DOWN : UP },
        })),
        barCategoryGap: '10%',
        barMaxWidth: 16,
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

watch(() => props.points, render)
</script>

<template>
  <div ref="el" :style="{ width: '100%', height: (height ?? 180) + 'px' }" />
</template>
