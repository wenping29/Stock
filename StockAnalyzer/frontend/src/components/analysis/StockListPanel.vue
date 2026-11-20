<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { StockInfo, StockListType } from '../../types/analysis'
import { analysisErrorMessage, fetchStockList } from '../../api/analysis'
import { useStockSelectionStore } from '../../stores/stockSelection'

const STOCK_TYPES: { value: StockListType; label: string }[] = [
  { value: 'all', label: '全部' },
  { value: 'a', label: 'A股' },
  { value: 'b', label: 'B股' },
  { value: 'sh', label: '上交所' },
  { value: 'sz', label: '深交所' },
  { value: 'bj', label: '北交所' },
  { value: 'chinext', label: '创业板' },
  { value: 'star', label: '科创板' },
  { value: 'neeq', label: '新三板' },
]

const PAGE_SIZE = 20
const store = useStockSelectionStore()

const stocks = ref<StockInfo[]>([])
const loading = ref(false)
const error = ref('')
const query = ref('')
const type = ref<StockListType>('all')
const page = ref(1)
const collapsed = ref(false)

const filtered = computed(() => {
  const q = query.value.trim().toLowerCase()
  if (!q) return stocks.value
  return stocks.value.filter((s) => s.code.toLowerCase().includes(q) || s.name.toLowerCase().includes(q))
})
const totalPages = computed(() => Math.max(1, Math.ceil(filtered.value.length / PAGE_SIZE)))
const pageItems = computed(() => {
  const p = Math.min(page.value, totalPages.value)
  return filtered.value.slice((p - 1) * PAGE_SIZE, p * PAGE_SIZE)
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    stocks.value = await fetchStockList(type.value)
    page.value = 1
  } catch (e) {
    error.value = analysisErrorMessage(e)
  } finally {
    loading.value = false
  }
}

function changeType(t: StockListType) {
  type.value = t
  page.value = 1
  load()
}

function changeQuery() {
  page.value = 1
}

function pick(s: StockInfo) {
  store.selectStock(s)
}

onMounted(load)
</script>

<template>
  <aside class="stock-panel qa-stock-list" :class="{ collapsed }">
    <template v-if="collapsed">
      <button class="panel-expand" title="展开股票列表" @click="collapsed = false">«</button>
    </template>
    <template v-else>
      <div class="panel-head">
        <h4>股票列表 <span class="qa-muted">{{ stocks.length }} 只</span></h4>
        <button class="panel-collapse" title="收缩面板" @click="collapsed = true">»</button>
      </div>
      <div class="panel-filter">
        <div class="type-grid">
          <button
            v-for="t in STOCK_TYPES"
            :key="t.value"
            :class="{ active: type === t.value }"
            @click="changeType(t.value)"
          >{{ t.label }}</button>
        </div>
        <el-input v-model="query" size="small" placeholder="搜索代码 / 名称" clearable @input="changeQuery" />
      </div>
      <div class="panel-body">
        <div v-if="loading" class="qa-empty">加载中...</div>
        <div v-else-if="error" class="panel-error">{{ error }}</div>
        <template v-else>
          <div
            v-for="s in pageItems"
            :key="s.code"
            class="stock-item"
            :class="{ active: store.stock?.code === s.code }"
            @click="pick(s)"
          >
            <span class="stock-name">{{ s.name }}</span>
            <span class="stock-code">{{ s.code }}</span>
          </div>
          <div v-if="!pageItems.length" class="qa-empty">无匹配股票</div>
        </template>
      </div>
      <div class="panel-foot">
        <el-button size="small" :disabled="page <= 1" @click="page = Math.max(1, page - 1)">上一页</el-button>
        <span class="qa-muted">{{ Math.min(page, totalPages) }} / {{ totalPages }}</span>
        <el-button size="small" :disabled="page >= totalPages" @click="page = Math.min(totalPages, page + 1)">下一页</el-button>
      </div>
    </template>
  </aside>
</template>

<style scoped>
.stock-panel {
  background: #fff;
  border-radius: 8px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.06);
  display: flex;
  flex-direction: column;
  max-height: calc(100vh - 100px);
  position: sticky;
  top: 0;
  transition: width 0.2s, flex-basis 0.2s;
}
.stock-panel.collapsed {
  width: 40px !important;
  flex-basis: 40px !important;
  align-items: center;
}
.panel-expand,
.panel-collapse {
  width: 24px;
  height: 24px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  color: #909399;
  background: #f5f7fa;
  border: 1px solid #ebeef5;
  border-radius: 4px;
  cursor: pointer;
}
.panel-expand { margin-top: 8px; }
.panel-collapse:hover,
.panel-expand:hover { color: #409eff; border-color: #c6e2ff; }
.panel-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 12px;
  border-bottom: 1px solid #ebeef5;
  flex-shrink: 0;
}
.panel-head h4 { font-size: 14px; }
.panel-filter {
  padding: 10px 12px;
  border-bottom: 1px solid #ebeef5;
  display: flex;
  flex-direction: column;
  gap: 8px;
  flex-shrink: 0;
}
.type-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.type-grid button {
  padding: 2px 8px;
  font-size: 11px;
  color: #606266;
  background: #f5f7fa;
  border: 1px solid #dcdfe6;
  border-radius: 3px;
  cursor: pointer;
}
.type-grid button.active {
  background: #409eff;
  border-color: #409eff;
  color: #fff;
}
.panel-body {
  flex: 1;
  overflow-y: auto;
  min-height: 120px;
}
.stock-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 6px 12px;
  font-size: 13px;
  cursor: pointer;
  border-bottom: 1px solid #f5f7fa;
}
.stock-item:hover { background: #f5f7fa; }
.stock-item.active { background: #ecf5ff; }
.stock-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: #303133;
}
.stock-code {
  font-family: 'SFMono-Regular', Consolas, monospace;
  color: #909399;
  font-size: 12px;
}
.panel-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  border-top: 1px solid #ebeef5;
  flex-shrink: 0;
}
.panel-error { color: #f56c6c; font-size: 12px; padding: 12px; }
</style>