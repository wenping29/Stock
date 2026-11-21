<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts'
import type { IndexEtf } from '../types'

// ETF 资金热力图：色块面积 = 当日成交额，颜色深浅 = 涨跌幅（红涨绿跌）
const props = defineProps<{ items: IndexEtf[]; height?: number }>()
const emit = defineEmits<{ (e: 'select', item: IndexEtf): void }>()

const el = ref<HTMLElement>()
let chart: echarts.ECharts | null = null

/** 涨跌幅 → 色块颜色与文字颜色（幅度越大颜色越饱和）。 */
function tileStyle(pct: number | null) {
  if (pct === null) return { color: 'rgba(144,147,153,0.3)', text: '#303133' }
  const strength = Math.min(Math.abs(pct) / 4, 1)
  const alpha = 0.22 + 0.68 * strength
  const rgb = pct >= 0 ? '239,35,42' : '20,177,67'
  return {
    color: `rgba(${rgb},${alpha.toFixed(2)})`,
    text: alpha > 0.55 ? '#fff' : '#303133',
  }
}

const fmtPct = (pct: number | null) =>
  pct === null || pct === undefined ? '--' : `${pct >= 0 ? '+' : ''}${pct.toFixed(2)}%`
const fmtYi = (v: number) => `${((v || 0) / 1e8).toFixed(2)} 亿`

function render() {
  if (!chart) return
  const items = props.items
  if (!items.length) {
    chart.clear()
    return
  }

  // 面积按成交额；若整批成交额缺失则等高，避免出现零面积色块
  const maxAmount = Math.max(...items.map((i) => i.amount || 0))
  const data = items.map((it) => {
    const style = tileStyle(it.changePct)
    return {
      name: it.name,
      value: maxAmount > 0 ? Math.max(it.amount || 0, maxAmount * 0.06) : 1,
      pct: it.changePct,
      symbol: it.symbol,
      amount: it.amount,
      itemStyle: { color: style.color, borderColor: '#fff', borderWidth: 2, gapWidth: 2 },
      label: { color: style.text },
    }
  })

  chart.setOption({
    animation: false,
    tooltip: {
      confine: true,
      backgroundColor: 'rgba(255,255,255,0.96)',
      borderColor: '#ddd',
      textStyle: { color: '#303133', fontSize: 12 },
      formatter: (p: any) => {
        const d = p.data
        if (!d) return ''
        return `<b>${d.name}</b><br/>代码：${d.symbol}<br/>成交额：${fmtYi(d.amount)}<br/>涨跌幅：${fmtPct(d.pct)}`
      },
    },
    series: [
      {
        type: 'treemap',
        roam: false,
        nodeClick: false,
        breadcrumb: { show: false },
        left: 0,
        right: 0,
        top: 0,
        bottom: 0,
        label: {
          show: true,
          fontSize: 12,
          lineHeight: 17,
          overflow: 'truncate',
          formatter: (p: any) => `${p.data.name}\n${fmtPct(p.data.pct)}`,
        },
        upperLabel: { show: false },
        data,
      },
    ],
  })
}

onMounted(() => {
  chart = echarts.init(el.value!)
  chart.on('click', (params: any) => {
    const item = props.items[params.dataIndex]
    if (item) emit('select', item)
  })
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

watch(() => props.items, render)
</script>

<template>
  <div ref="el" :style="{ width: '100%', height: (height ?? 240) + 'px' }" />
</template>
