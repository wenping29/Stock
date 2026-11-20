<script setup lang="ts">
import type { IndexItem } from '../types'
import SparkLine from './SparkLine.vue'

defineProps<{ title: string; items: IndexItem[]; selectedCode?: string }>()
const emit = defineEmits<{ (e: 'select', item: IndexItem): void }>()

function pctClass(v: number) {
  return v >= 0 ? 'up' : 'down'
}
function fmtPct(v: number) {
  return `${v >= 0 ? '+' : ''}${v.toFixed(2)}%`
}
</script>

<template>
  <div class="group-title">{{ title }}</div>
  <div class="index-grid">
    <div
      v-for="idx in items" :key="idx.code"
      class="index-cell" :class="{ active: selectedCode === idx.code }"
      @click="emit('select', idx)"
    >
      <div class="cell-name" :title="idx.name">{{ idx.name }}</div>
      <div class="cell-price" :class="pctClass(idx.changePct)">{{ idx.price.toFixed(2) }}</div>
      <div class="cell-pct" :class="pctClass(idx.changePct)">{{ fmtPct(idx.changePct) }}</div>
      <SparkLine :data="idx.trend" :up="idx.changePct >= 0" :height="26" />
    </div>
  </div>
</template>

<style scoped>
.group-title { font-size: 14px; font-weight: 600; margin-bottom: 8px; color: #606266; }
.index-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(86px, 1fr)); gap: 8px; }
.index-cell {
  border: 1px solid #ebeef5; border-radius: 6px; padding: 6px 7px; cursor: pointer;
  transition: all 0.15s;
}
.index-cell:hover { border-color: #409eff; }
.index-cell.active { border-color: #409eff; background: #ecf5ff; }
.cell-name { font-size: 11px; color: #606266; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.cell-price { font-size: 13px; font-weight: 600; line-height: 1.25; }
.cell-pct { font-size: 11px; font-weight: 600; line-height: 1.3; }
</style>
