<script setup lang="ts">
import { computed, ref } from 'vue'
import type { EChartsOption } from 'echarts'
import type { IndicatorResult, KlineData } from '../../types/analysis'
import { CHART_PALETTE, baseTooltip, useEChart } from '../../composables/useEChart'
import FullscreenChart from './FullscreenChart.vue'

const props = withDefaults(defineProps<{
  klineData: KlineData[]
  indicator: IndicatorResult
  height?: number
}>(), { height: 220 })

const el = ref<HTMLElement>()

const paramText = computed(() =>
  Object.entries(props.indicator.params).map(([k, v]) => `${k}=${v}`).join(', '),
)

const option = computed<EChartsOption | null>(() => {
  const dates = props.klineData.map((d) => d.date)
  if (!dates.length) return null
  const { name, columns, values } = props.indicator

  const guideLines: number[] = []
  if (name === 'RSI' || name === 'WR') guideLines.push(70, 30)
  if (name === 'KDJ') guideLines.push(80, 20)

  const series: any[] = []
  columns.forEach((col, i) => {
    const vals = values[i]
    if (!vals) return
    if (name === 'MACD' && col === 'BAR') {
      series.push({
        name: col,
        type: 'bar',
        data: vals.map((v) => ({
          value: v,
          itemStyle: { color: v >= 0 ? '#ef232a' : '#14b143' },
        })),
        barMaxWidth: 8,
      })
      return
    }
    series.push({
      name: col,
      type: 'line',
      data: vals,
      showSymbol: false,
      lineStyle: { width: 1.2, color: CHART_PALETTE[i % CHART_PALETTE.length] },
      itemStyle: { color: CHART_PALETTE[i % CHART_PALETTE.length] },
      ...(i === 0 && guideLines.length
        ? {
            markLine: {
              symbol: 'none',
              silent: true,
              lineStyle: { type: 'dashed', color: '#c0c4cc', width: 1 },
              label: { show: false },
              data: guideLines.map((y) => ({ yAxis: y })),
            },
          }
        : {}),
    })
  })

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
    grid: { left: 64, right: 24, top: 32, bottom: 28 },
    xAxis: {
      type: 'category',
      data: dates,
      axisLine: { lineStyle: { color: '#c0c4cc' } },
      axisLabel: { fontSize: 10, color: '#909399' },
    },
    yAxis: {
      type: 'value',
      scale: true,
      axisLabel: { fontSize: 10, color: '#909399' },
      splitLine: { lineStyle: { color: '#f0f0f0' } },
    },
    dataZoom: [{ type: 'inside', start: 0, end: 100 }],
    series,
  }
})

useEChart(el, () => option.value, () => [props.klineData, props.indicator])
</script>

<template>
  <FullscreenChart :title="indicator.name" v-slot="{ isFullscreen }">
    <div class="ind-head">
      <b>{{ indicator.name }}</b>
      <span v-if="paramText" class="qa-muted">（{{ paramText }}）</span>
    </div>
    <div
      ref="el"
      class="qa-chart"
      :style="{ height: isFullscreen ? 'calc(100vh - 150px)' : `${height}px` }"
    />
  </FullscreenChart>
</template>

<style scoped>
.ind-head {
  font-size: 13px;
  color: #606266;
  padding: 0 30px 6px 0;
}
.ind-head b { color: #303133; }
</style>