<script setup lang="ts">
import { computed, ref } from 'vue'
import type { EChartsOption } from 'echarts'
import type { IndicatorResult, KlineData } from '../../types/analysis'
import { DOWN_COLOR, UP_COLOR, baseTooltip, useEChart } from '../../composables/useEChart'
import FullscreenChart from './FullscreenChart.vue'

const props = withDefaults(defineProps<{
  klineData: KlineData[]
  indicators: IndicatorResult[]
  height?: number
}>(), { height: 220 })

const el = ref<HTMLElement>()

const option = computed<EChartsOption | null>(() => {
  const data = props.klineData
  if (!data.length) return null
  const dates = data.map((d) => d.date)
  const bars = data.map((d, i) => ({
    value: d.volume,
    itemStyle: { color: i > 0 ? (d.close >= data[i - 1].close ? UP_COLOR : DOWN_COLOR) : '#909399' },
  }))

  const series: any[] = [{ name: '成交量', type: 'bar', data: bars, barMaxWidth: 12 }]
  const legendData = ['成交量']

  const volInd = props.indicators.find((r) => r.name === 'VOL')
  if (volInd) {
    const p = volInd.params.period || 5
    const idx = volInd.columns.indexOf(`VOL_MA_${p}`)
    const vals = idx >= 0 ? volInd.values[idx] : undefined
    if (vals) {
      series.push({
        name: `均量${p}日`,
        type: 'line',
        data: vals,
        showSymbol: false,
        lineStyle: { width: 1.2, color: '#e6a23c' },
        itemStyle: { color: '#e6a23c' },
      })
      legendData.push(`均量${p}日`)
    }
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
      data: legendData,
    },
    grid: { left: 64, right: 24, top: 34, bottom: 46 },
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

useEChart(el, () => option.value, () => props.klineData)
</script>

<template>
  <FullscreenChart title="成交量" v-slot="{ isFullscreen }">
    <div
      ref="el"
      class="qa-chart"
      :style="{ height: isFullscreen ? 'calc(100vh - 110px)' : `${height}px` }"
    />
  </FullscreenChart>
</template>