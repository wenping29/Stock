<script setup lang="ts">
import { computed, ref } from 'vue'
import type { EChartsOption } from 'echarts'
import { useEChart } from '../../composables/useEChart'
import FullscreenChart from './FullscreenChart.vue'

const props = withDefaults(defineProps<{
  equityCurve: { date: string; value: number }[]
  height?: number
}>(), { height: 260 })

const el = ref<HTMLElement>()
const MONTHS = ['1月', '2月', '3月', '4月', '5月', '6月', '7月', '8月', '9月', '10月', '11月', '12月']

const matrix = computed(() => {
  const curve = props.equityCurve
  if (curve.length < 2) return null

  const monthly = new Map<string, number>()
  let prevValue = curve[0].value
  let prevMonth = curve[0].date.slice(0, 7)
  for (let i = 1; i < curve.length; i++) {
    const currentMonth = curve[i].date.slice(0, 7)
    if (currentMonth !== prevMonth) {
      monthly.set(prevMonth, ((curve[i - 1].value - prevValue) / prevValue) * 100)
      prevValue = curve[i - 1].value
      prevMonth = currentMonth
    }
  }
  const last = curve[curve.length - 1].value
  monthly.set(prevMonth, ((last - prevValue) / prevValue) * 100)

  const keys = [...monthly.keys()].sort()
  if (!keys.length) return null
  const years = [...new Set(keys.map((k) => Number(k.slice(0, 4))))].sort()
  const items: { value: [number, number, number]; text: string }[] = []
  let maxAbs = 0
  for (const year of years) {
    const yi = years.indexOf(year)
    for (let m = 1; m <= 12; m++) {
      const key = `${year}-${String(m).padStart(2, '0')}`
      const ret = monthly.get(key)
      if (ret == null) continue
      const rounded = Math.round(ret * 100) / 100
      maxAbs = Math.max(maxAbs, Math.abs(rounded))
      items.push({ value: [m - 1, yi, rounded], text: `${year}年${m}月: ${ret.toFixed(2)}%` })
    }
  }
  return { years, items, maxAbs: Math.max(maxAbs, 1) }
})

const option = computed<EChartsOption | null>(() => {
  const m = matrix.value
  if (!m) return null
  return {
    animation: false,
    tooltip: {
      formatter: (p: any) => p.data?.text ?? '',
      backgroundColor: 'rgba(255,255,255,0.96)',
      borderColor: '#ddd',
      textStyle: { color: '#303133', fontSize: 12 },
    },
    grid: { left: 60, right: 20, top: 40, bottom: 30 },
    xAxis: {
      type: 'category',
      data: MONTHS,
      position: 'top',
      axisLine: { lineStyle: { color: '#c0c4cc' } },
      axisLabel: { fontSize: 10, color: '#909399' },
      splitArea: { show: false },
    },
    yAxis: {
      type: 'category',
      data: m.years.map(String),
      inverse: true,
      axisLine: { lineStyle: { color: '#c0c4cc' } },
      axisLabel: { fontSize: 11, color: '#909399' },
    },
    visualMap: {
      min: -m.maxAbs,
      max: m.maxAbs,
      calculable: false,
      orient: 'horizontal',
      right: 10,
      top: 4,
      itemWidth: 10,
      itemHeight: 60,
      textStyle: { fontSize: 10, color: '#909399' },
      inRange: { color: ['#14b143', '#f5f7fa', '#ef232a'] },
    },
    series: [{
      type: 'heatmap',
      data: m.items,
      label: { show: false },
      itemStyle: { borderColor: '#fff', borderWidth: 1 },
    }],
  }
})

useEChart(el, () => option.value, () => props.equityCurve)
</script>

<template>
  <div v-if="!matrix" class="qa-empty">数据不足，无法生成热力图</div>
  <FullscreenChart v-else title="月度收益热力图" v-slot="{ isFullscreen }">
    <div
      ref="el"
      class="qa-chart"
      :style="{ height: isFullscreen ? 'calc(100vh - 110px)' : `${height}px` }"
    />
  </FullscreenChart>
</template>