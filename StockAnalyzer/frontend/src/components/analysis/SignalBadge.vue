<script setup lang="ts">
import { computed } from 'vue'
import type { IndicatorResult } from '../../types/analysis'

const props = defineProps<{ indicators: IndicatorResult[] }>()

const allSignals = computed(() =>
  props.indicators.flatMap((ind) =>
    ind.signals.slice(-3).map((s) => ({ ...s, indicator: ind.name })),
  ),
)
</script>

<template>
  <div class="signal-row">
    <span v-if="!allSignals.length" class="qa-muted">近期无信号</span>
    <span
      v-for="(s, i) in allSignals"
      :key="i"
      class="signal-badge"
      :class="s.type === 'buy' ? 'buy' : 'sell'"
    >[{{ s.indicator }}] {{ s.description }}</span>
  </div>
</template>

<style scoped>
.signal-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 12px;
}
.signal-badge {
  font-size: 12px;
  padding: 3px 8px;
  border-radius: 4px;
  border: 1px solid;
}
.signal-badge.buy {
  color: #ef232a;
  background: #fef0f0;
  border-color: #fbc4c4;
}
.signal-badge.sell {
  color: #14b143;
  background: #f0f9eb;
  border-color: #c2e7b0;
}
</style>