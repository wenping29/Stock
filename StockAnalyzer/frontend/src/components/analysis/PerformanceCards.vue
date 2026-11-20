<script setup lang="ts">
import { computed } from 'vue'
import type { PerformanceMetrics } from '../../types/analysis'

const props = defineProps<{ metrics: PerformanceMetrics | null }>()

const METRIC_CONFIG: { key: keyof PerformanceMetrics; label: string; unit: string }[] = [
  { key: 'totalReturn', label: '总收益率', unit: '%' },
  { key: 'annualizedReturn', label: '年化收益', unit: '%' },
  { key: 'maxDrawdown', label: '最大回撤', unit: '%' },
  { key: 'sharpeRatio', label: '夏普比率', unit: '' },
  { key: 'winRate', label: '胜率', unit: '%' },
  { key: 'profitFactor', label: '盈亏比', unit: '' },
  { key: 'totalTrades', label: '交易次数', unit: '' },
  { key: 'calmarRatio', label: '卡玛比率', unit: '' },
]

const cards = computed(() => {
  if (!props.metrics) return []
  return METRIC_CONFIG.map(({ key, label, unit }) => {
    const value = Number(props.metrics![key])
    let cls = ''
    if (key === 'maxDrawdown') {
      cls = value <= 20 ? 'down' : 'up'
    } else if (key !== 'totalTrades') {
      cls = value > 0 ? 'up' : value < 0 ? 'down' : ''
    }
    return { key, label, text: `${value.toFixed(2)}${unit}`, cls }
  })
})
</script>

<template>
  <div v-if="!metrics" class="qa-empty">暂无绩效数据</div>
  <div v-else class="qa-stat-grid">
    <div v-for="c in cards" :key="c.key" class="qa-stat">
      <div class="label">{{ c.label }}</div>
      <div class="value" :class="c.cls">{{ c.text }}</div>
    </div>
  </div>
</template>