<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref, watch } from 'vue'
import * as echarts from 'echarts'
import type { SectorFlowItem } from '../types'

const props = defineProps<{ items: SectorFlowItem[] }>()

const el = ref<HTMLElement>()
let chart: echarts.ECharts | null = null

/** 按净流入方向与规模取色：红=流入，绿=流出，规模越大颜色越深 */
function styleOf(item: SectorFlowItem, maxAbs: number) {
  const ratio = maxAbs > 0 ? Math.abs(item.mainNet) / maxAbs : 0
  const strong = ratio >= 0.6
  const mid = ratio >= 0.25
  if (item.mainNet >= 0) {
    return strong
      ? { bg: '#a93226', fg: '#ffffff' }
      : mid
        ? { bg: '#cd6155', fg: '#ffffff' }
        : { bg: '#e6b0aa', fg: '#7b241c' }
  }
  return strong
    ? { bg: '#1e8449', fg: '#ffffff' }
    : mid
      ? { bg: '#52be80', fg: '#ffffff' }
      : { bg: '#a9dfbf', fg: '#1e8449' }
}

function fmtYi(v: number) {
  return `${v >= 0 ? '+' : ''}${v.toFixed(2)} 亿`
}

function render() {
  if (!chart || !props.items.length) return
  const maxAbs = Math.max(...props.items.map((i) => Math.abs(i.mainNet)), 0.01)

  const data = [...props.items]
    .sort((a, b) => Math.abs(b.mainNet) - Math.abs(a.mainNet))
    .map((i) => {
      const s = styleOf(i, maxAbs)
      return {
        name: i.name,
        // 面积 = 主力净流入绝对值；无资金量时给最小权重保证可见
        value: Math.max(Math.abs(i.mainNet), 0.01),
        code: i.code,
        mainNet: i.mainNet,
        amount: i.amount,
        changePct: i.changePct,
        itemStyle: { color: s.bg, borderColor: '#ffffff', borderWidth: 1 },
        label: { color: s.fg },
      }
    })

  chart.setOption({
    animation: false,
    tooltip: {
      backgroundColor: 'rgba(255,255,255,0.96)',
      borderColor: '#ddd',
      textStyle: { color: '#303133', fontSize: 12 },
      formatter: (info: any) => {
        const d = info.data
        if (!d || d.mainNet == null) return `<b>${info.name}</b>`
        const color = d.mainNet >= 0 ? '#ef232a' : '#14b143'
        const pctColor = d.changePct >= 0 ? '#ef232a' : '#14b143'
        return (
          `<b>${d.name}（${d.code}）</b><br/>` +
          `主力净流入：<span style="color:${color};font-weight:600">${fmtYi(d.mainNet)}</span><br/>` +
          `成交额：${d.amount.toFixed(2)} 亿<br/>` +
          `涨跌幅：<span style="color:${pctColor}">${d.changePct >= 0 ? '+' : ''}${d.changePct.toFixed(2)}%</span>`
        )
      },
    },
    series: [
      {
        type: 'treemap',
        name: '行业板块资金流',
        roam: false,
        nodeClick: false,
        // 面积过小的板块不绘制，避免数百个小色块互相挤压
        visibleMin: 200,
        breadcrumb: { show: false },
        width: '100%',
        height: '100%',
        label: {
          show: true,
          fontSize: 12,
          lineHeight: 16,
          formatter: (p: any) =>
            p.data?.mainNet != null ? `${p.name}\n${fmtYi(p.data.mainNet)}` : p.name,
        },
        itemStyle: { borderColor: '#ffffff' },
        data,
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

watch(() => props.items, render, { deep: false })
</script>

<template>
  <div ref="el" class="chart-box-sector" />
</template>

<style scoped>
.chart-box-sector { width: 100%; height: 420px; }
@media (max-width: 768px) {
  .chart-box-sector { height: 320px; }
}
</style>
