<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import type {
  KlineData,
  KlinePeriod,
  MacroIndicator,
  PriceSeriesPoint,
  RealtimeQuote,
} from '../../types/analysis'
import {
  analysisErrorMessage,
  fetchCn10y,
  fetchCnRateHistory,
  fetchCrudeOil,
  fetchFxCnyUsd,
  fetchGoldPrice,
  fetchIndexKline,
  fetchIndexPeriodData,
  fetchMacroIndicators,
  fetchMarketIndexes,
  fetchUsRateHistory,
} from '../../api/analysis'
import StockChart from '../../components/analysis/StockChart.vue'
import AnalysisLineChart from '../../components/analysis/AnalysisLineChart.vue'

const indexes = ref<RealtimeQuote[]>([])
const selected = ref<RealtimeQuote | null>(null)
const klineData = ref<KlineData[]>([])
const period = ref<KlinePeriod>('daily')
const loading = ref(true)
const klineLoading = ref(false)
const error = ref('')

const macro = ref<MacroIndicator[]>([])
const usRate = ref<{ date: string; rate: number }[]>([])
const rateMaturity = ref('10Y')
const cnRate = ref<{ date: string; rate: number }[]>([])
const cnRateType = ref('SHIBOR_3M')

const unifiedPeriod = ref('daily')
const indexPeriodData = ref<Record<string, { name: string; data: PriceSeriesPoint[] }>>({})
const oilData = ref<PriceSeriesPoint[]>([])
const cn10yData = ref<PriceSeriesPoint[]>([])

const fxPeriod = ref('daily')
const fxData = ref<PriceSeriesPoint[]>([])
const goldPeriod = ref('daily')
const goldData = ref<PriceSeriesPoint[]>([])

const dateRange = ref((() => {
  const end = new Date()
  const start = new Date(end)
  start.setFullYear(end.getFullYear() - 1)
  return { start: start.toISOString().slice(0, 10), end: end.toISOString().slice(0, 10) }
})())

const KLINE_PERIODS: { value: KlinePeriod; label: string }[] = [
  { value: '60min', label: '60分' },
  { value: 'daily', label: '日线' },
  { value: 'weekly', label: '周线' },
  { value: 'monthly', label: '月线' },
]
const CN_RATE_TYPES = [
  { value: 'SHIBOR_3M', label: 'SHIBOR 3M' },
  { value: 'SHIBOR_6M', label: 'SHIBOR 6M' },
  { value: 'SHIBOR_1Y', label: 'SHIBOR 1Y' },
  { value: 'CN_10Y', label: '中债 10Y' },
]
const UNIFIED_PERIODS = [
  { value: 'daily', label: '日' },
  { value: 'weekly', label: '周' },
  { value: 'monthly', label: '月' },
  { value: 'quarterly', label: '季' },
  { value: 'yearly', label: '年' },
]
const RATE_MATURITIES = ['3M', '6M', '1Y', '10Y']
const PRICE_PERIODS = UNIFIED_PERIODS

function pctClass(v: number | null | undefined) {
  return (v ?? 0) >= 0 ? 'up' : 'down'
}
function signedPct(v: number) {
  return `${v >= 0 ? '+' : ''}${v.toFixed(2)}%`
}
function inflowText(v?: number) {
  if (v == null) return '净流入 --'
  return `净流入 ${v >= 0 ? '+' : ''}${(v / 1e8).toFixed(2)}亿`
}

async function loadIndexes() {
  loading.value = true
  error.value = ''
  try {
    const quotes = await fetchMarketIndexes()
    indexes.value = quotes
    if (quotes.length && !selected.value) selected.value = quotes[0]
    if (selected.value) {
      const still = quotes.find((q) => q.code === selected.value!.code)
      if (still) selected.value = still
    }
  } catch (e) {
    error.value = analysisErrorMessage(e)
  } finally {
    loading.value = false
  }
}

let klineSeq = 0

async function loadKline() {
  if (!selected.value) return
  const seq = ++klineSeq
  klineLoading.value = true
  error.value = ''
  try {
    const rows = await fetchIndexKline(
      selected.value.code, dateRange.value.start, dateRange.value.end, period.value)
    if (seq !== klineSeq) return
    klineData.value = rows
  } catch (e) {
    if (seq !== klineSeq) return
    error.value = analysisErrorMessage(e)
    klineData.value = []
  } finally {
    if (seq === klineSeq) klineLoading.value = false
  }
}

// K 线图预设区间（1月/3月/6月/1年/全部）→ 按新区间重新查询
function onKlineRangeChange(range: { start: string; end: string; months: number | 'all' }) {
  dateRange.value = range.months === 'all'
    ? { start: '2000-01-01', end: range.end }
    : { start: range.start, end: range.end }
  loadKline()
}

async function loadMacro() {
  try { macro.value = await fetchMacroIndicators() } catch { /* 忽略 */ }
}
async function loadUsRate() {
  try { usRate.value = await fetchUsRateHistory(rateMaturity.value) } catch { /* 忽略 */ }
}
async function loadCnRate() {
  try { cnRate.value = await fetchCnRateHistory(cnRateType.value) } catch { /* 忽略 */ }
}
async function loadUnified() {
  const [idx, oil, cn10] = await Promise.all([
    fetchIndexPeriodData(unifiedPeriod.value).catch(() => ({})),
    fetchCrudeOil(unifiedPeriod.value).catch(() => [] as PriceSeriesPoint[]),
    fetchCn10y(unifiedPeriod.value).catch(() => [] as PriceSeriesPoint[]),
  ])
  indexPeriodData.value = idx
  oilData.value = oil
  cn10yData.value = cn10
}
async function loadFx() {
  try { fxData.value = await fetchFxCnyUsd(fxPeriod.value) } catch { /* 忽略 */ }
}
async function loadGold() {
  try { goldData.value = await fetchGoldPrice(goldPeriod.value) } catch { /* 忽略 */ }
}

function reloadAll() {
  loadIndexes()
  loadMacro()
  loadUsRate()
  loadCnRate()
  loadUnified()
  loadFx()
  loadGold()
  loadKline()
}

onMounted(() => {
  loadIndexes()
  loadMacro()
  loadUsRate()
  loadCnRate()
  loadUnified()
  loadFx()
  loadGold()
})

watch(selected, loadKline)
watch(period, loadKline)
watch(rateMaturity, loadUsRate)
watch(cnRateType, loadCnRate)
watch(unifiedPeriod, loadUnified)
watch(fxPeriod, loadFx)
watch(goldPeriod, loadGold)

// ---- 图表数据 ----

const cnRateChart = computed(() => ({
  dates: cnRate.value.map((d) => d.date),
  series: [{ name: `${cnRateType.value} 利率`, data: cnRate.value.map((d) => d.rate), color: '#e6a23c' }],
}))

const normalized = computed(() => {
  const entries: { name: string; color: string; map: Map<string, number> }[] = []

  Object.entries(indexPeriodData.value).forEach(([, info], i) => {
    if (!info.data.length) return
    const base = info.data[0].close || 1
    const map = new Map<string, number>()
    info.data.forEach((d) => map.set(d.date, ((d.close / base) - 1) * 100))
    entries.push({ name: info.name, color: ['#409eff', '#67c23a', '#e6a23c', '#909399', '#7e57c2'][i % 5], map })
  })

  if (usRate.value.length) {
    const base = usRate.value[0].rate
    const map = new Map<string, number>()
    usRate.value.forEach((d) => map.set(d.date, ((d.rate / base) - 1) * 100))
    entries.push({ name: `美债 ${rateMaturity.value}`, color: '#1e88e5', map })
  }
  if (cn10yData.value.length) {
    const base = cn10yData.value[0].close
    const map = new Map<string, number>()
    cn10yData.value.forEach((d) => map.set(d.date, ((d.close / base) - 1) * 100))
    entries.push({ name: '中债10Y', color: '#ef232a', map })
  }
  if (oilData.value.length) {
    const base = oilData.value[0].close
    const map = new Map<string, number>()
    oilData.value.forEach((d) => map.set(d.date, ((d.close / base) - 1) * 100))
    entries.push({ name: '原油期货', color: '#14b143', map })
  }

  const dateSet = new Set<string>()
  entries.forEach((e) => e.map.forEach((_, d) => dateSet.add(d)))
  const dates = [...dateSet].sort()
  return {
    dates,
    series: entries.map((e) => ({
      name: e.name,
      color: e.color,
      data: dates.map((d) => (e.map.has(d) ? e.map.get(d)! : null)),
    })),
  }
})

const fxChart = computed(() => ({
  dates: fxData.value.map((d) => d.date),
  series: [{ name: 'USD/CNY', data: fxData.value.map((d) => d.close), color: '#14b143' }],
}))

const goldChart = computed(() => ({
  dates: goldData.value.map((d) => d.date),
  series: [{ name: 'COMEX 黄金', data: goldData.value.map((d) => d.close), color: '#e6a23c' }],
}))
</script>

<template>
  <div>
    <div class="qa-card-head page-title">
      <h3>大盘行情</h3>
      <el-button size="small" :loading="loading" @click="reloadAll">刷新全部</el-button>
    </div>

    <p v-if="error" class="error-text">{{ error }}</p>

    <div v-loading="loading">
      <div class="qa-index-grid">
        <button
          v-for="idx in indexes"
          :key="idx.code"
          class="qa-index-card"
          :class="{ active: selected?.code === idx.code }"
          @click="selected = idx"
        >
          <div class="name">{{ idx.name }}</div>
          <div class="price" :class="pctClass(idx.changePct)">{{ Number(idx.price).toFixed(2) }}</div>
          <div class="sub" :class="pctClass(idx.changePct)">{{ signedPct(idx.changePct) }}</div>
          <div class="sub inflow" :class="pctClass(idx.mainInflow)">{{ inflowText(idx.mainInflow) }}</div>
        </button>
      </div>
    </div>

    <template v-if="selected">
      <div class="qa-card" v-loading="klineLoading">
        <div class="qa-card-head">
          <h4>
            {{ selected.name }}
            <span class="qa-muted code">{{ selected.code }}</span>
            <span class="qa-muted range">{{ dateRange.start }} ~ {{ dateRange.end }}</span>
          </h4>
          <div class="qa-tools">
            <div class="qa-seg">
              <button
                v-for="p in KLINE_PERIODS"
                :key="p.value"
                :class="{ active: period === p.value }"
                @click="period = p.value"
              >{{ p.label }}</button>
            </div>
          </div>
        </div>

        <StockChart
          v-if="klineData.length"
          :kline-data="klineData"
          :indicators="[]"
          :us-rate-data="usRate"
          :us-rate-label="rateMaturity"
          @range-change="onKlineRangeChange"
        />
        <div v-else-if="!klineLoading" class="qa-empty">暂无行情数据</div>

        <div v-if="macro.length" class="qa-tag-row">
          <div v-for="m in macro" :key="m.key" class="qa-macro-tag">
            <span class="k">{{ m.name }}</span>
            <span class="v" :class="pctClass(m.changePct)">
              {{ m.value.toFixed(m.precision) }}{{ m.unit }}
            </span>
            <span :class="pctClass(m.changePct)">{{ m.changePct >= 0 ? '+' : '' }}{{ m.changePct.toFixed(2) }}%</span>
          </div>
        </div>
      </div>

      <div v-if="cnRateChart.dates.length" class="qa-card">
        <div class="qa-card-head">
          <h4>人民币利率历史</h4>
          <div class="qa-tools">
            <div class="qa-seg">
              <button
                v-for="t in CN_RATE_TYPES"
                :key="t.value"
                :class="{ active: cnRateType === t.value }"
                @click="cnRateType = t.value"
              >{{ t.label }}</button>
            </div>
          </div>
        </div>
        <AnalysisLineChart
          :dates="cnRateChart.dates"
          :series="cnRateChart.series"
          :height="200"
          value-suffix="%"
        />
      </div>

      <div v-if="normalized.dates.length" class="qa-card">
        <div class="qa-card-head">
          <h4>综合归一化对比 (%)</h4>
          <div class="qa-tools">
            <div class="qa-seg">
              <button
                v-for="p in UNIFIED_PERIODS"
                :key="p.value"
                :class="{ active: unifiedPeriod === p.value }"
                @click="unifiedPeriod = p.value"
              >{{ p.label }}</button>
            </div>
            <span class="qa-muted">美债期限:</span>
            <div class="qa-seg">
              <button
                v-for="m in RATE_MATURITIES"
                :key="m"
                :class="{ active: rateMaturity === m }"
                @click="rateMaturity = m"
              >{{ m }}</button>
            </div>
          </div>
        </div>
        <AnalysisLineChart
          :dates="normalized.dates"
          :series="normalized.series"
          :height="360"
          show-legend
          value-suffix="%"
        />
      </div>

      <div v-if="fxChart.dates.length" class="qa-card">
        <div class="qa-card-head">
          <h4>人民币/美元汇率 (USD/CNY)</h4>
          <div class="qa-tools">
            <div class="qa-seg">
              <button
                v-for="p in PRICE_PERIODS"
                :key="p.value"
                :class="{ active: fxPeriod === p.value }"
                @click="fxPeriod = p.value"
              >{{ p.label }}</button>
            </div>
          </div>
        </div>
        <AnalysisLineChart :dates="fxChart.dates" :series="fxChart.series" :height="200" />
      </div>

      <div v-if="goldChart.dates.length" class="qa-card">
        <div class="qa-card-head">
          <h4>黄金价格 (COMEX, USD/盎司)</h4>
          <div class="qa-tools">
            <div class="qa-seg">
              <button
                v-for="p in PRICE_PERIODS"
                :key="p.value"
                :class="{ active: goldPeriod === p.value }"
                @click="goldPeriod = p.value"
              >{{ p.label }}</button>
            </div>
          </div>
        </div>
        <AnalysisLineChart :dates="goldChart.dates" :series="goldChart.series" :height="200" />
      </div>
    </template>
  </div>
</template>

<style scoped>
.page-title {
  background: #fff;
  border-radius: 8px;
  padding: 12px 20px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.06);
}
.page-title h3 { font-size: 17px; }
.error-text { color: #f56c6c; font-size: 13px; margin-bottom: 12px; }
.inflow { font-size: 11px; }
.code { font-family: 'SFMono-Regular', Consolas, monospace; margin-left: 4px; }
.range { margin-left: 12px; font-weight: normal; }
</style>