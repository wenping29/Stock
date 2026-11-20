<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref, watch } from 'vue'
import * as echarts from 'echarts'
import type { MarketVolumeCurvePoint } from '../types'

// 今日两市成交额分时累计曲线（单位：亿元）
const props = defineProps<{ points: MarketVolumeCurvePoint[] }>()

const el = ref<HTMLElement>()
let chart: echarts.ECharts | null = null

function render() {
  if (!chart) return
  chart.setOption({
    animation: false,
    tooltip: {
      trigger: 'axis',
      valueFormatter: (v: number) => `${v.toFixed(0)} 亿元`,
    },
    grid: { left: 64, right: 20, top: 20, bottom: 30 },
    xAxis: {
      type: 'category',
      boundaryGap: false,
      data: props.points.map((p) => p.time),
      axisLine: { lineStyle: { color: '#c0c4cc' } },
    },
    yAxis: {
      type: 'value',
      axisLabel: { formatter: (v: number) => `${(v / 10000).toFixed(1)}万亿` },
      splitLine: { lineStyle: { color: '#f0f0f0' } },
    },
    series: [
      {
        name: '两市成交额',
        type: 'line',
        smooth: true,
        symbol: 'none',
        lineStyle: { color: '#c23531', width: 2 },
        areaStyle: {
          color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
            { offset: 0, color: 'rgba(194, 53, 49, 0.28)' },
            { offset: 1, color: 'rgba(194, 53, 49, 0.02)' },
          ]),
        },
        data: props.points.map((p) => p.amount),
      },
    ],
  }, true)
}

onMounted(() => {
  chart = echarts.init(el.value!)
  render()
  window.addEventListener('resize', resize)
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', resize)
  chart?.dispose()
  chart = null
})

function resize() {
  chart?.resize()
}

watch(() => props.points, render)
</script>

<template>
  <div ref="el" style="width: 100%; height: 300px" />
</template>
