<script setup lang="ts">
import type { IndexItem } from '../types'
import SparkLine from './SparkLine.vue'

defineProps<{ title: string; items: IndexItem[]; selectedCode?: string }>()
const emit = defineEmits<{
  (e: 'select', item: IndexItem): void
  (e: 'edit', item: IndexItem): void
  (e: 'remove', item: IndexItem): void
}>()

function pctClass(v: number) {
  return v >= 0 ? 'up' : 'down'
}
function fmtPct(v: number) {
  return `${v >= 0 ? '+' : ''}${v.toFixed(2)}%`
}
</script>

<template>
  <div class="group-head">
    <span class="group-title">{{ title }}</span>
    <slot name="extra" />
  </div>
  <div class="index-grid">
    <div
      v-for="idx in items" :key="idx.code"
      class="index-cell" :class="{ active: selectedCode === idx.code, custom: idx.custom }"
      @click="emit('select', idx)"
    >
      <span v-if="idx.custom" class="cell-tag">自定义</span>
      <div class="cell-name" :title="idx.name">{{ idx.name }}</div>
      <div class="cell-price" :class="pctClass(idx.changePct)">{{ idx.price.toFixed(2) }}</div>
      <div class="cell-pct" :class="pctClass(idx.changePct)">{{ fmtPct(idx.changePct) }}</div>
      <SparkLine :data="idx.trend" :up="idx.changePct >= 0" :height="26" />
      <div v-if="idx.custom" class="cell-actions">
        <span @click.stop="emit('edit', idx)">编辑</span>
        <span @click.stop="emit('remove', idx)">删除</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.group-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px; }
.group-title { font-size: 14px; font-weight: 600; color: #606266; }
.index-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(86px, 1fr)); gap: 8px; }
.index-cell {
  position: relative; border: 1px solid #ebeef5; border-radius: 6px; padding: 6px 7px; cursor: pointer;
  transition: all 0.15s;
}
.index-cell:hover { border-color: #409eff; }
.index-cell.active { border-color: #409eff; background: #ecf5ff; }
/* 自定义指数：宽度为普通卡片的一倍，右上角标识，悬停时切换为编辑/删除 */
.index-cell.custom { grid-column: span 2; }
.index-cell.custom .cell-name { padding-right: 34px; }
.cell-tag {
  position: absolute; top: 2px; right: 4px; padding: 0 3px; border-radius: 3px;
  font-size: 9px; line-height: 13px; color: #409eff; background: #ecf5ff;
}
.cell-actions {
  display: none; position: absolute; top: 1px; right: 3px; gap: 5px; padding: 0 3px;
  font-size: 10px; line-height: 15px; border-radius: 3px; background: rgba(255, 255, 255, 0.95);
}
.cell-actions span { color: #409eff; cursor: pointer; }
.cell-actions span:hover { text-decoration: underline; }
.index-cell.custom:hover .cell-tag { display: none; }
.index-cell.custom:hover .cell-actions { display: flex; }
.cell-name { font-size: 11px; color: #606266; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.cell-price { font-size: 13px; font-weight: 600; line-height: 1.25; }
.cell-pct { font-size: 11px; font-weight: 600; line-height: 1.3; }
</style>
