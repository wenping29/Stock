<script setup lang="ts">
import { computed, ref } from 'vue'
import type { EChartsOption } from 'echarts'
import type { Trade } from '../../types/analysis'
import { DOWN_COLOR, UP_COLOR, baseTooltip, useEChart } from '../../composables/useEChart'
import FullscreenChart from './FullscreenChart.vue'

const props = withDefaults(defineProps<{
  equityCurve: { date: string; value: number }[]
  trades: Trade[]
  initialCapital: number
  height?: number
}>(), { height: 360 })

const el = ref<HTMLElement>()

const option = computed<EChartsOption | null>(() => {
  const curve = props.equityCurve
  if (curve.length < 2) return null
  const dates = curve.map((p) => p.date)
  const values = curve.map((p) => p.value)

  const drawdown: number[] = []
  let peak = props.initialCapital
  for (const v of values) {
    if (v > peak) peak = v
    drawdown.push(-((peak - v) / peak) * 100)
  }

  const buyX: string[] = []
  const buyY: number[] = []
  const sellX: string[] = []
  const sellY: number[] = []
  const indexOf = new Map(dates.map((d, i) => [d, i]))
  for (const t of props.trades) {
    const ei = indexOf.get(t.entryDate)
    const xi = indexOf.get(t.exitDate)
    if (ei != null) { buyX.push(t.entryDate); buyY.push(values[ei]) }
    if (xi != null) { sellX.push(t.exitDate); sellY.push(values[xi]) }
  }

  return {
    animation: false,
    tooltip: baseTooltip,
    legend: {
      top: 0,
      left: 8,
      itemWidth: 14,
      itemHeight: 2,
      textStyle: { fontSize: 11, color: '#606266' },
    },
    grid: { left: 70, right: 64, top: 34, bottom: 46 },
    xAxis: {
      type: 'category',
      data: dates,
      boundaryGap: false,
      axisLine: { lineStyle: { color: '#c0c4cc' } },
      axisLabel: { fontSize: 10, color: '#909399' },
    },
    yAxis: [
      {
        type: 'value',
        name: '资金(元)',
        scale: true,
        nameTextStyle: { fontSize: 11, color: '#909399' },
        axisLabel: { fontSize: 10, color: '#909399' },
        splitLine: { lineStyle: { color: '#f0f0f0' } },
      },
      {
        type: 'value',
        name: '回撤(%)',
        scale: true,
        nameTextStyle: { fontSize: 11, color: '#f56c6c' },
        axisLabel: { fontSize: 10, color: '#f56c6c' },
        splitLine: { show: false },
      },
    ],
    dataZoom: [{ type: 'inside', start: 0, end: 100 }],
    series: [
      {
        name: '资金曲线',
        type: 'line',
        data: values,
        showSymbol: false,
        lineStyle: { width: 2, color: '#409eff' },
        itemStyle: { color: '#409eff' },
        z: 4,
      },
      {
        name: '回撤 (%)',
        type: 'line',
        yAxisIndex: 1,
        data: drawdown,
        showSymbol: false,
        lineStyle: { width: 1, color: '#f56c6c' },
        itemStyle: { color: '#f56c6c' },
        areaStyle: { color: 'rgba(245,108,108,0.12)' },
        z: 2,
      },
      {
        name: '买入',
        type: 'scatter',
        data: buyX.map((x, i) => [x, buyY[i]]),
        symbol: 'triangle',
        symbolSize: 9,
        itemStyle: { color: UP_COLOR },
        z: 5,
      },
      {
        name: '卖出',
        type: 'scatter',
        data: sellX.map((x, i) => [x, sellY[i]]),
        symbol: 'triangle',
        symbolRotate: 180,
        symbolSize: 9,
        itemStyle: { color: DOWN_COLOR },
        z: 5,
      },
    ],
  }
})

useEChart(el, () => option.value, () => [props.equityCurve, props.trades, props.initialCapital])
</script>

<template>
  <div v-if="equityCurve.length < 2" class="qa-empty">无资金曲线数据</div>
  <FullscreenChart v-else title="资金曲线" v-slot="{ isFullscreen }">
    <div
      ref="el"
      class="qa-chart"
      :style="{ height: isFullscreen ? 'calc(100vh - 110px)' : `${height}px` }"
    />
  </FullscreenChart>
</template>