<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import type { IndicatorResult, KlineData, KlinePeriod, StockInfo } from '../../types/analysis'
import { analysisErrorMessage, computeIndicators, fetchKline, searchStock } from '../../api/analysis'
import { useStockSelectionStore } from '../../stores/stockSelection'
import CollapsibleSidebar from '../../components/analysis/CollapsibleSidebar.vue'
import StockChart from '../../components/analysis/StockChart.vue'
import VolumeChart from '../../components/analysis/VolumeChart.vue'
import IndicatorChart from '../../components/analysis/IndicatorChart.vue'
import SignalBadge from '../../components/analysis/SignalBadge.vue'
import StockListPanel from '../../components/analysis/StockListPanel.vue'

const INDICATOR_LIST = [
  { name: 'MA', label: 'MA 移动平均线', defaultParams: { period: 20 } },
  { name: 'EMA', label: 'EMA 指数均线', defaultParams: { period: 20 } },
  { name: 'MACD', label: 'MACD', defaultParams: { fast: 12, slow: 26, signal: 9 } },
  { name: 'BOLL', label: '布林带', defaultParams: { period: 20, stddev: 2 } },
  { name: 'RSI', label: 'RSI 相对强弱', defaultParams: { period: 14 } },
  { name: 'KDJ', label: 'KDJ 随机指标', defaultParams: { n: 9, k: 3, d: 3 } },
  { name: 'WR', label: 'WR 威廉指标', defaultParams: { period: 14 } },
  { name: 'VOL', label: '成交量均线', defaultParams: { period: 5 } },
  { name: 'OBV', label: 'OBV 能量潮', defaultParams: { maPeriod: 20 } },
  { name: 'VolumeRatio', label: '量比', defaultParams: { period: 5 } },
  { name: 'ATR', label: 'ATR 平均真实波幅', defaultParams: { period: 14 } },
]

const PERIODS: { value: KlinePeriod; label: string }[] = [
  { value: '60min', label: '60分' },
  { value: 'daily', label: '日线' },
  { value: 'weekly', label: '周线' },
  { value: 'monthly', label: '月线' },
]

const store = useStockSelectionStore()

const searchQuery = ref('')
const searchResults = ref<StockInfo[]>([])
const selectedStock = ref<StockInfo | null>(null)
const klineData = ref<KlineData[]>([])
const indicatorResults = ref<IndicatorResult[]>([])
const selectedIndicators = ref<string[]>(['MA', 'MACD', 'VOL', 'RSI'])
const loading = ref(false)
const error = ref('')
const period = ref<KlinePeriod>('daily')
const dateRange = ref({ start: '2024-01-01', end: new Date().toISOString().slice(0, 10) })

const overlayIndicators = computed(() =>
  indicatorResults.value.filter((r) => ['MA', 'EMA', 'BOLL'].includes(r.name)),
)
const subIndicators = computed(() =>
  indicatorResults.value.filter((r) => !['MA', 'EMA', 'BOLL'].includes(r.name)),
)
const hasVol = computed(() => indicatorResults.value.some((r) => r.name === 'VOL'))

async function doSearch() {
  const q = searchQuery.value.trim()
  if (!q) return
  try {
    searchResults.value = await searchStock(q)
  } catch (e) {
    error.value = analysisErrorMessage(e)
  }
}

async function loadStock(stock: StockInfo) {
  selectedStock.value = stock
  searchResults.value = []
  searchQuery.value = ''
  loading.value = true
  error.value = ''
  try {
    const data = await fetchKline(stock.code, dateRange.value.start, dateRange.value.end, period.value)
    klineData.value = data
    const reqs = selectedIndicators.value.map((name) => ({
      name,
      params: INDICATOR_LIST.find((i) => i.name === name)?.defaultParams ?? {},
    }))
    indicatorResults.value = reqs.length ? await computeIndicators(data, reqs) : []
  } catch (e) {
    error.value = analysisErrorMessage(e)
    klineData.value = []
    indicatorResults.value = []
  } finally {
    loading.value = false
  }
}

async function refreshChart() {
  if (!selectedStock.value) return
  await loadStock(selectedStock.value)
}

watch(
  () => store.seq,
  () => {
    if (store.stock) loadStock(store.stock)
  },
)

onMounted(() => {
  if (store.stock) {
    selectedStock.value = store.stock
    loadStock(store.stock)
  }
})
</script>

<template>
  <div class="qa-page">
    <CollapsibleSidebar :width="280">
      <div class="qa-field">
        <span class="qa-label">股票搜索</span>
        <div class="search-row">
          <el-input
            v-model="searchQuery"
            size="small"
            placeholder="代码 / 名称"
            clearable
            @keyup.enter="doSearch"
          />
          <el-button size="small" type="primary" @click="doSearch">搜索</el-button>
        </div>
        <div v-if="searchResults.length" class="qa-result-dropdown">
          <div
            v-for="s in searchResults"
            :key="s.code"
            class="qa-result-item"
            @click="loadStock(s)"
          >
            <span>{{ s.name }}</span>
            <span class="code">{{ s.code }}</span>
          </div>
        </div>
      </div>

      <div v-if="selectedStock" class="selected-stock">
        <div class="selected-name">{{ selectedStock.name }}</div>
        <div class="qa-muted">{{ selectedStock.code }}</div>
      </div>

      <div class="qa-field">
        <span class="qa-label">K线周期</span>
        <div class="qa-seg period-grid">
          <button
            v-for="p in PERIODS"
            :key="p.value"
            :class="{ active: period === p.value }"
            @click="period = p.value"
          >{{ p.label }}</button>
        </div>
      </div>

      <div class="qa-field">
        <span class="qa-label">日期范围</span>
        <el-date-picker
          v-model="dateRange.start"
          type="date"
          size="small"
          value-format="YYYY-MM-DD"
          placeholder="开始日期"
          style="width: 100%; margin-bottom: 6px"
        />
        <el-date-picker
          v-model="dateRange.end"
          type="date"
          size="small"
          value-format="YYYY-MM-DD"
          placeholder="结束日期"
          style="width: 100%"
        />
      </div>

      <div class="qa-field">
        <span class="qa-label">技术指标</span>
        <el-checkbox-group v-model="selectedIndicators" class="indicator-group">
          <el-checkbox
            v-for="ind in INDICATOR_LIST"
            :key="ind.name"
            :value="ind.name"
          >{{ ind.label }}</el-checkbox>
        </el-checkbox-group>
        <el-button
          type="primary"
          size="small"
          style="margin-top: 10px"
          :disabled="!selectedStock"
          :loading="loading"
          @click="refreshChart"
        >更新图表</el-button>
      </div>

      <p v-if="error" class="error-text">{{ error }}</p>
    </CollapsibleSidebar>

    <div class="qa-main">
      <div v-if="!selectedStock" class="qa-card qa-empty">
        请搜索并选择一只股票开始分析
      </div>
      <div v-else-if="loading" class="qa-card qa-empty">加载中...</div>
      <template v-else>
        <SignalBadge :indicators="indicatorResults" />
        <div v-if="klineData.length" class="qa-card">
          <StockChart :kline-data="klineData" :indicators="overlayIndicators" />
          <VolumeChart v-if="hasVol" :kline-data="klineData" :indicators="indicatorResults" />
        </div>
        <div v-else class="qa-card qa-empty">暂无行情数据</div>

        <div v-for="ind in subIndicators" :key="ind.name" class="qa-card">
          <IndicatorChart v-if="klineData.length" :kline-data="klineData" :indicator="ind" />
        </div>
      </template>
    </div>

    <StockListPanel />
  </div>
</template>

<style scoped>
.search-row {
  display: flex;
  gap: 6px;
}
.selected-stock {
  background: #f5f7fa;
  border-radius: 6px;
  padding: 10px 12px;
}
.selected-name { font-size: 14px; font-weight: 600; color: #303133; }
.period-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
}
.period-grid button { width: 100%; }
.indicator-group {
  display: flex;
  flex-direction: column;
  gap: 2px;
  align-items: flex-start;
}
.error-text { color: #f56c6c; font-size: 12px; }
</style>