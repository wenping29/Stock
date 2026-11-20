<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { EChartsOption } from 'echarts'
import type { IndicatorResult, KlineData } from '../../types/analysis'
import { DOWN_COLOR, UP_COLOR, baseTooltip, useEChart } from '../../composables/useEChart'
import FullscreenChart from './FullscreenChart.vue'

const props = withDefaults(defineProps<{
  klineData: KlineData[]
  indicators: IndicatorResult[]
  usRateData?: { date: string; rate: number }[]
  usRateLabel?: string
  /** 美元利率（SOFR）叠加 */
  usdRateData?: { date: string; rate: number }[]
  /** 主图下方叠加成交量柱状副图 */
  showVolume?: boolean
  height?: number
}>(), {
  usRateData: () => [],
  usRateLabel: '',
  usdRateData: () => [],
  showVolume: false,
  height: 460,
})

// 预设区间需要父组件重新查询时监听（不监听则仅本地过滤）
const emit = defineEmits<{
  (e: 'range-change', range: { start: string; end: string; months: number | 'all' }): void
}>()

/** 后端日线/周线日期形如 20261120、2026-11、2026-W47，统一成可比较/可填入 date 控件的值 */
function toIsoDate(d: string): string {
  if (/^\d{8}$/.test(d)) return `${d.slice(0, 4)}-${d.slice(4, 6)}-${d.slice(6, 8)}`
  if (/^\d{4}-\d{2}$/.test(d)) return `${d}-01`
  if (/^\d{4}-\d{2}-\d{2} /.test(d)) return d.slice(0, 10)
  return d
}

function toMs(d: string): number {
  if (/^\d{8}$/.test(d)) return Date.UTC(+d.slice(0, 4), +d.slice(4, 6) - 1, +d.slice(6, 8))
  if (/^\d{4}-\d{2}$/.test(d)) return Date.UTC(+d.slice(0, 4), +d.slice(5, 7) - 1, 1)
  if (/^\d{4}-W\d{2}$/.test(d)) {
    const year = +d.slice(0, 4)
    const week = +d.slice(6)
    const jan4 = Date.UTC(year, 0, 4)
    const dow = (new Date(jan4).getUTCDay() + 6) % 7
    return jan4 - dow * 86400000 + (week - 1) * 7 * 86400000
  }
  if (/^\d{4}-Q\d$/.test(d)) return Date.UTC(+d.slice(0, 4), (+d.slice(6) - 1) * 3, 1)
  if (/^\d{4}$/.test(d)) return Date.UTC(+d, 0, 1)
  const t = Date.parse(d.replace(' ', 'T'))
  return Number.isNaN(t) ? 0 : t
}

function fmtDate(ms: number): string {
  return new Date(ms).toISOString().slice(0, 10)
}

const el = ref<HTMLElement>()
const chartType = ref<'candlestick' | 'line'>('candlestick')
const showUsRate = ref(false)
const showUsdRate = ref(false)
const dateStart = ref('')
const dateEnd = ref('')

watch(
  () => props.klineData,
  (data) => {
    if (!data.length) return
    dateStart.value = toIsoDate(data[0].date)
    dateEnd.value = toIsoDate(data[data.length - 1].date)
  },
  { immediate: true },
)

const filtered = computed(() => {
  const data = props.klineData
  if (!data.length) return { kline: [] as KlineData[], indicators: [] as IndicatorResult[] }
  const dates = data.map((d) => d.date)
  let startIdx = 0
  let endIdx = dates.length - 1
  if (dateStart.value) {
    const from = toMs(dateStart.value)
    const i = dates.findIndex((d) => toMs(d) >= from)
    if (i >= 0) startIdx = i
  }
  if (dateEnd.value) {
    const to = toMs(dateEnd.value)
    const i = dates.findIndex((d) => toMs(d) > to)
    endIdx = i >= 0 ? i - 1 : dates.length - 1
  }
  if (startIdx > endIdx) {
    startIdx = 0
    endIdx = dates.length - 1
  }
  return {
    kline: data.slice(startIdx, endIdx + 1),
    indicators: props.indicators.map((ind) => ({
      ...ind,
      values: ind.values.map((arr) => arr.slice(startIdx, endIdx + 1)),
    })),
  }
})

function getCol(ind: IndicatorResult, colName: string): number[] | undefined {
  const idx = ind.columns.indexOf(colName)
  return idx >= 0 ? ind.values[idx] : undefined
}

function setPreset(months: number | 'all') {
  const data = props.klineData
  if (!data.length) return
  const lastMs = toMs(data[data.length - 1].date)
  if (months === 'all') {
    dateStart.value = toIsoDate(data[0].date)
    dateEnd.value = toIsoDate(data[data.length - 1].date)
  } else {
    const start = new Date(lastMs)
    start.setUTCMonth(start.getUTCMonth() - months)
    start.setUTCDate(start.getUTCDate() + 1)
    dateStart.value = fmtDate(start.getTime())
    dateEnd.value = fmtDate(lastMs)
  }
  emit('range-change', { start: dateStart.value, end: dateEnd.value, months })
}

const hasUsRate = computed(() => props.usRateData.length > 0)
const hasUsdRate = computed(() => props.usdRateData.length > 0)

/** 利率序列按 K 线日期对齐（取不晚于该日的最近一个值）。 */
function alignRate(kline: KlineData[], series: { date: string; rate: number }[]): (number | null)[] {
  const sorted = [...series].sort((a, b) => a.date.localeCompare(b.date))
  const aligned: (number | null)[] = []
  let idx = 0
  for (const k of kline) {
    const kd = toIsoDate(k.date)
    while (idx < sorted.length - 1 && sorted[idx + 1].date <= kd) idx++
    aligned.push(sorted[idx] && sorted[idx].date <= kd ? sorted[idx].rate : null)
  }
  return aligned
}

const option = computed<EChartsOption | null>(() => {
  const kline = filtered.value.kline
  if (!kline.length) return null
  const dates = kline.map((d) => toIsoDate(d.date))
  const series: any[] = []
  const legendData: string[] = []

  if (chartType.value === 'candlestick') {
    series.push({
      name: 'K线',
      type: 'candlestick',
      data: kline.map((d) => [d.open, d.close, d.low, d.high]),
      itemStyle: { color: UP_COLOR, color0: DOWN_COLOR, borderColor: UP_COLOR, borderColor0: DOWN_COLOR },
      z: 3,
    })
    legendData.push('K线')
  } else {
    series.push(
      {
        name: '最高价',
        type: 'line',
        data: kline.map((d) => d.high),
        showSymbol: false,
        lineStyle: { width: 1, color: '#f56c6c', type: 'dotted' },
        itemStyle: { color: '#f56c6c' },
      },
      {
        name: '收盘价',
        type: 'line',
        data: kline.map((d) => d.close),
        showSymbol: false,
        lineStyle: { width: 1.6, color: '#e6a23c' },
        itemStyle: { color: '#e6a23c' },
      },
      {
        name: '最低价',
        type: 'line',
        data: kline.map((d) => d.low),
        showSymbol: false,
        lineStyle: { width: 1, color: '#67c23a', type: 'dotted' },
        itemStyle: { color: '#67c23a' },
      },
    )
    legendData.push('最高价', '收盘价', '最低价')
  }

  const overlayColors = ['#409eff', '#e6a23c', '#7e57c2', '#00897b', '#f56c6c']
  let colorIdx = 0
  for (const ind of filtered.value.indicators) {
    if (ind.name === 'MA') {
      const period = ind.params.period || 20
      const vals = getCol(ind, `MA_${period}`)
      if (vals) {
        series.push({
          name: `MA${period}`,
          type: 'line',
          data: vals,
          showSymbol: false,
          lineStyle: { width: 1.2, color: overlayColors[colorIdx++ % overlayColors.length] },
        })
        legendData.push(`MA${period}`)
      }
    }
    if (ind.name === 'EMA') {
      const period = ind.params.period || 20
      const vals = getCol(ind, `EMA_${period}`)
      if (vals) {
        series.push({
          name: `EMA${period}`,
          type: 'line',
          data: vals,
          showSymbol: false,
          lineStyle: { width: 1.2, type: 'dashed', color: overlayColors[colorIdx++ % overlayColors.length] },
        })
        legendData.push(`EMA${period}`)
      }
    }
    if (ind.name === 'BOLL') {
      const mid = getCol(ind, 'MID')
      const upper = getCol(ind, 'UPPER')
      const lower = getCol(ind, 'LOWER')
      if (mid && upper && lower) {
        series.push(
          { name: 'BOLL上轨', type: 'line', data: upper, showSymbol: false, lineStyle: { width: 0.8, color: '#909399' } },
          { name: 'BOLL中轨', type: 'line', data: mid, showSymbol: false, lineStyle: { width: 0.8, color: '#e6a23c' } },
          { name: 'BOLL下轨', type: 'line', data: lower, showSymbol: false, lineStyle: { width: 0.8, color: '#909399' } },
        )
        legendData.push('BOLL上轨', 'BOLL中轨', 'BOLL下轨')
      }
    }
  }

  const showTreasury = showUsRate.value && hasUsRate.value
  const showUsd = showUsdRate.value && hasUsdRate.value
  const hasRateAxis = showTreasury || showUsd

  if (showTreasury) {
    series.push({
      name: `美债${props.usRateLabel}`,
      type: 'line',
      yAxisIndex: 1,
      data: alignRate(kline, props.usRateData),
      showSymbol: false,
      connectNulls: true,
      lineStyle: { width: 1.4, type: 'dashed', color: '#409eff' },
      itemStyle: { color: '#409eff' },
    })
    legendData.push(`美债${props.usRateLabel}`)
  }

  if (showUsd) {
    series.push({
      name: '美元利率',
      type: 'line',
      yAxisIndex: 1,
      data: alignRate(kline, props.usdRateData),
      showSymbol: false,
      connectNulls: true,
      lineStyle: { width: 1.4, type: 'dashed', color: '#00897b' },
      itemStyle: { color: '#00897b' },
    })
    legendData.push('美元利率')
  }

  if (props.showVolume) {
    series.push({
      name: '成交量',
      type: 'bar',
      xAxisIndex: 1,
      yAxisIndex: hasRateAxis ? 2 : 1,
      data: kline.map((d, i) => ({
        value: d.volume,
        itemStyle: { color: d.close >= (kline[i - 1]?.close ?? d.open) ? UP_COLOR : DOWN_COLOR },
      })),
    })
  }

  const right = hasRateAxis ? 64 : 24

  return {
    animation: false,
    tooltip: { ...baseTooltip, axisPointer: { type: 'cross' } },
    legend: {
      top: 0,
      left: 8,
      itemWidth: 14,
      itemHeight: 2,
      itemGap: 10,
      textStyle: { fontSize: 11, color: '#606266' },
      data: legendData,
    },
    grid: props.showVolume
      ? [
          { left: 64, right, top: 36, height: '55%' },
          { left: 64, right, top: '66%', height: '13%' },
        ]
      : { left: 64, right, top: 36, bottom: 64 },
    xAxis: props.showVolume
      ? [
          {
            type: 'category',
            data: dates,
            boundaryGap: true,
            axisLine: { lineStyle: { color: '#c0c4cc' } },
            axisLabel: { fontSize: 10, color: '#909399' },
          },
          {
            type: 'category',
            gridIndex: 1,
            data: dates,
            boundaryGap: true,
            axisLabel: { show: false },
            axisTick: { show: false },
            axisLine: { lineStyle: { color: '#e4e7ed' } },
          },
        ]
      : {
          type: 'category',
          data: dates,
          boundaryGap: true,
          axisLine: { lineStyle: { color: '#c0c4cc' } },
          axisLabel: { fontSize: 10, color: '#909399' },
        },
    yAxis: [
      {
        type: 'value',
        scale: true,
        axisLabel: { fontSize: 10, color: '#909399' },
        splitLine: { lineStyle: { color: '#f0f0f0' } },
      },
      ...(hasRateAxis
        ? [{
            type: 'value' as const,
            scale: true,
            name: showTreasury && showUsd ? '利率 %' : showTreasury ? '美债利率 %' : '美元利率 %',
            nameTextStyle: { fontSize: 10, color: showTreasury ? '#409eff' : '#00897b' },
            axisLabel: { fontSize: 10, color: showTreasury ? '#409eff' : '#00897b' },
            splitLine: { show: false },
          }]
        : []),
      ...(props.showVolume
        ? [{
            type: 'value' as const,
            gridIndex: 1,
            axisLabel: { show: false },
            splitLine: { show: false },
          }]
        : []),
    ],
    dataZoom: [
      { type: 'inside', xAxisIndex: props.showVolume ? [0, 1] : [0], start: 0, end: 100 },
      { type: 'slider', xAxisIndex: props.showVolume ? [0, 1] : [0], bottom: 10, height: 20, start: 0, end: 100 },
    ],
    series,
  }
})

useEChart(el, () => option.value, () => [filtered.value, chartType.value, showUsRate.value, showUsdRate.value, props.showVolume])

const presets: { label: string; val: number | 'all' }[] = [
  { label: '1月', val: 1 },
  { label: '3月', val: 3 },
  { label: '6月', val: 6 },
  { label: '1年', val: 12 },
  { label: '全部', val: 'all' },
]
</script>

<template>
  <FullscreenChart title="K线图" v-slot="{ isFullscreen }">
    <div class="chart-toolbar">
      <div class="qa-seg">
        <button v-for="p in presets" :key="p.label" @click="setPreset(p.val)">{{ p.label }}</button>
      </div>
      <input v-model="dateStart" type="date" class="date-input" />
      <span class="range-sep">~</span>
      <input v-model="dateEnd" type="date" class="date-input" />
      <div class="qa-seg">
        <button :class="{ active: chartType === 'candlestick' }" @click="chartType = 'candlestick'">K线</button>
        <button :class="{ active: chartType === 'line' }" @click="chartType = 'line'">折线</button>
      </div>
      <button
        v-if="hasUsRate"
        class="us-toggle"
        :class="{ active: showUsRate }"
        @click="showUsRate = !showUsRate"
      >美债{{ usRateLabel }}</button>
      <button
        v-if="hasUsdRate"
        class="us-toggle"
        :class="{ active: showUsdRate }"
        @click="showUsdRate = !showUsdRate"
      >美元利率</button>
      <!-- 附加工具（如技术指标开关），排在美债/美元利率按钮之后 -->
      <slot name="toolbar" />
    </div>
    <div
      ref="el"
      class="qa-chart"
      :style="{ height: isFullscreen ? 'calc(100vh - 150px)' : `${height}px` }"
    />
  </FullscreenChart>
</template>

<style scoped>
.chart-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  padding: 2px 30px 8px 0;
}
.date-input {
  font-size: 12px;
  color: #606266;
  background: #fff;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  padding: 3px 6px;
  outline: none;
}
.date-input:focus { border-color: #409eff; }
.range-sep { color: #c0c4cc; font-size: 12px; }
.us-toggle {
  padding: 3px 10px;
  font-size: 12px;
  color: #606266;
  background: #f5f7fa;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  cursor: pointer;
}
.us-toggle.active { background: #409eff; border-color: #409eff; color: #fff; }
</style>