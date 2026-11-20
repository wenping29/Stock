<script setup lang="ts">
import { computed, ref } from 'vue'
import type { EChartsOption } from 'echarts'
import { CHART_PALETTE, GRID_COLOR, baseTooltip, useEChart } from '../../composables/useEChart'

const props = withDefaults(defineProps<{
  dates: string[]
  series: { name: string; data: (number | null)[]; color?: string; dashed?: boolean; area?: boolean }[]
  height?: number
  yName?: string
  valueSuffix?: string
  showLegend?: boolean
  zoomStart?: number
  showZoom?: boolean
}>(), {
  height: 220,
  yName: '',
  valueSuffix: '',
  showLegend: false,
  zoomStart: 0,
  showZoom: true,
})

const el = ref<HTMLElement>()
const legend = computed(() => props.showLegend || props.series.length > 1)

const option = computed<EChartsOption | null>(() => {
  if (!props.dates.length) return null
  return {
    animation: false,
    tooltip: { ...baseTooltip, valueFormatter: (v) => (v == null ? '-' : `${v}${props.valueSuffix}`) },
    legend: legend.value
      ? { top: 0, left: 8, itemWidth: 14, itemHeight: 2, textStyle: { fontSize: 11, color: '#606266' } }
      : { show: false },
    grid: { left: 64, right: 24, top: legend.value ? 36 : 18, bottom: props.showZoom ? 56 : 30 },
    xAxis: {
      type: 'category',
      data: props.dates,
      boundaryGap: false,
      axisLine: { lineStyle: { color: '#c0c4cc' } },
      axisLabel: { fontSize: 10, color: '#909399' },
    },
    yAxis: {
      type: 'value',
      name: props.yName,
      scale: true,
      nameTextStyle: { fontSize: 11, color: '#909399' },
      axisLabel: { fontSize: 10, color: '#909399', formatter: (v: number) => `${v}${props.valueSuffix}` },
      splitLine: { lineStyle: { color: GRID_COLOR } },
    },
    dataZoom: props.showZoom
      ? [
          { type: 'inside', start: props.zoomStart, end: 100 },
          { type: 'slider', bottom: 8, height: 16, start: props.zoomStart, end: 100 },
        ]
      : [],
    series: props.series.map((s, i) => {
      const color = s.color ?? CHART_PALETTE[i % CHART_PALETTE.length]
      return {
        name: s.name,
        type: 'line',
        data: s.data,
        showSymbol: false,
        connectNulls: true,
        lineStyle: { width: 1.6, color, type: s.dashed ? 'dashed' : 'solid' },
        itemStyle: { color },
        areaStyle: s.area ? { color, opacity: 0.12 } : undefined,
      }
    }),
  }
})

useEChart(el, () => option.value, () => [props.dates, props.series])
</script>

<template>
  <div ref="el" class="qa-chart" :style="{ height: `${height}px` }" />
</template>