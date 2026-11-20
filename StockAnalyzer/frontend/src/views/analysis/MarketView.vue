<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import type {
  KlineData,
  KlinePeriod,
  MacroIndicator,
  PriceSeriesPoint,
} from '../../types/analysis'
import type { IndexItem, IndexTrendData, KLinePoint } from '../../types'
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
  fetchUsRateHistory,
} from '../../api/analysis'
import { fetchIndices, fetchIndexTrend } from '../../api/quote'
import StockChart from '../../components/analysis/StockChart.vue'
import AnalysisLineChart from '../../components/analysis/AnalysisLineChart.vue'
import KLineChart from '../../components/KLineChart.vue'
import MarketMatrixChart from '../../components/MarketMatrixChart.vue'
import IntradayTrendChart from '../../components/IntradayTrendChart.vue'

const indexes = ref<IndexItem[]>([])
const selected = ref<IndexItem | null>(null)
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
// 卡片半宽后放不下“净流入”前缀，只显示带符号金额，完整说明放 title
function inflowText(v?: number | null) {
  if (v == null) return '--'
  return `${v >= 0 ? '+' : ''}${(v / 1e8).toFixed(2)}亿`
}
function inflowTitle(v?: number | null) {
  return v == null ? '宽基指数无板块资金流' : `主力净流入 ${v >= 0 ? '+' : ''}${(v / 1e8).toFixed(2)}亿`
}

async function loadIndexes() {
  loading.value = true
  error.value = ''
  try {
    const quotes = await fetchIndices('overview')
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

// ---- 图表 Tab：行情K线（StockChart）/ 日K均线 / 大数据K线 / 股市矩阵 / 日内走势 ----
type ChartTab = 'quote' | 'kline' | 'bigkline' | 'matrix' | 'intraday'
const CHART_TABS: { key: ChartTab; label: string }[] = [
  { key: 'quote', label: '行情K线' },
  { key: 'kline', label: '日K · 均线' },
  { key: 'bigkline', label: '大数据K线' },
  { key: 'matrix', label: '股市矩阵' },
  { key: 'intraday', label: '日内走势' },
]
const chartTab = ref<ChartTab>('quote')

const maPeriod = ref<'day' | 'week' | 'month'>('day')
const MA_PERIOD_MAP: Record<'day' | 'week' | 'month', KlinePeriod> = {
  day: 'daily',
  week: 'weekly',
  month: 'monthly',
}
const maPoints = ref<KLinePoint[]>([])
const bigPoints = ref<KLinePoint[]>([])
const trend = ref<IndexTrendData | null>(null)
const chartLoading = ref(false)
const trendLoading = ref(false)

/** 量化接口 KlineData（date 紧凑格式）转图表点。 */
function toKlinePoints(rows: KlineData[]): KLinePoint[] {
  return rows.map((r, i) => ({
    time: /^\d{8}$/.test(r.date)
      ? `${r.date.slice(0, 4)}-${r.date.slice(4, 6)}-${r.date.slice(6, 8)}`
      : r.date,
    open: r.open,
    high: r.high,
    low: r.low,
    close: r.close,
    volume: r.volume,
    changePct: i > 0 && rows[i - 1].close
      ? Math.round((r.close / rows[i - 1].close - 1) * 10000) / 100
      : 0,
  }))
}

let maSeq = 0
async function loadMaKline(big: boolean) {
  if (!selected.value) return
  const seq = ++maSeq
  chartLoading.value = true
  try {
    const rows = await fetchIndexKline(
      selected.value.code, '', '', MA_PERIOD_MAP[maPeriod.value], big ? 500 : 120)
    if (seq !== maSeq) return
    const mapped = toKlinePoints(rows)
    if (big) bigPoints.value = mapped
    else maPoints.value = mapped
  } catch (e) {
    if (seq !== maSeq) return
    error.value = analysisErrorMessage(e)
    if (big) bigPoints.value = []
    else maPoints.value = []
  } finally {
    if (seq === maSeq) chartLoading.value = false
  }
}

let trendSeq = 0
async function loadTrend() {
  if (!selected.value) return
  const seq = ++trendSeq
  trendLoading.value = true
  try {
    const data = await fetchIndexTrend(selected.value.code)
    if (seq === trendSeq) trend.value = data
  } catch {
    if (seq === trendSeq) trend.value = null
  } finally {
    if (seq === trendSeq) trendLoading.value = false
  }
}

function loadChartTab() {
  if (chartTab.value === 'kline') loadMaKline(false)
  else if (chartTab.value === 'bigkline') loadMaKline(true)
  else if (chartTab.value === 'intraday') loadTrend()
}

function pickChartTab(t: ChartTab) {
  if (chartTab.value === t) return
  chartTab.value = t
  loadChartTab()
}

function pickMaPeriod(p: 'day' | 'week' | 'month') {
  if (maPeriod.value === p) return
  maPeriod.value = p
  if (chartTab.value === 'kline' || chartTab.value === 'bigkline')
    loadMaKline(chartTab.value === 'bigkline')
}

const chartBusy = computed(() => {
  if (chartTab.value === 'quote') return klineLoading.value
  if (chartTab.value === 'intraday') return trendLoading.value
  return chartLoading.value
})

const chartEmpty = computed(() => {
  if (chartTab.value === 'quote') return !klineLoading.value && !klineData.value.length
  if (chartTab.value === 'kline') return !chartLoading.value && !maPoints.value.length
  if (chartTab.value === 'bigkline') return !chartLoading.value && !bigPoints.value.length
  if (chartTab.value === 'matrix') return !indexes.value.length
  return !trendLoading.value && !trend.value
})

// 日内走势 60 秒自动刷新，仅该 Tab 激活时运行
let trendTimer: number | undefined
watch(chartTab, (t) => {
  if (trendTimer) {
    clearInterval(trendTimer)
    trendTimer = undefined
  }
  if (t === 'intraday') trendTimer = window.setInterval(loadTrend, 60_000)
})
onUnmounted(() => {
  if (trendTimer) clearInterval(trendTimer)
})

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

watch(selected, () => {
  loadKline()
  loadChartTab()
})
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
          <div class="sub inflow" :class="pctClass(idx.mainInflow)"
            :title="inflowTitle(idx.mainInflow)">{{ inflowText(idx.mainInflow) }}</div>
        </button>
      </div>
    </div>

    <template v-if="selected">
      <div class="qa-card">
        <div class="qa-card-head">
          <h4>
            {{ selected.name }}
            <span class="qa-muted code">{{ selected.code }}</span>
            <span v-if="chartTab === 'quote'" class="qa-muted range">{{ dateRange.start }} ~ {{ dateRange.end }}</span>
          </h4>
          <div class="qa-tools" v-if="chartTab === 'quote'">
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

        <div class="chart-tabs">
          <span
            v-for="t in CHART_TABS" :key="t.key"
            class="chart-tab" :class="{ active: chartTab === t.key }"
            @click="pickChartTab(t.key)"
          >{{ t.label }}</span>
        </div>

        <div class="chart-toolbar" v-if="chartTab !== 'quote'">
          <div class="period-tabs" v-if="chartTab === 'kline' || chartTab === 'bigkline'">
            <span
              v-for="p in (['day', 'week', 'month'] as const)" :key="p"
              class="period-item" :class="{ active: maPeriod === p }"
              @click="pickMaPeriod(p)"
            >{{ { day: '日', week: '周', month: '月' }[p] }}</span>
          </div>
          <span class="chart-title">
            {{
              chartTab === 'kline'
                ? 'K 线 + MA5 · MA10 · MA20 · MA30（涨红跌绿，下方为成交量，滚轮/滑块可缩放）'
                : chartTab === 'bigkline'
                  ? '最近 500 根 K 线（大数据量），拖动底部滑块或滚轮缩放查看历史区间'
                  : chartTab === 'matrix'
                    ? '色块大小 = 当日成交额，颜色 = 涨跌幅（红涨绿跌），点击色块可切换指数'
                    : '分时走势（每 60 秒自动刷新）· 昨收不在当日波动区间时价格轴自动断轴压缩空档'
            }}
          </span>
        </div>

        <div v-loading="chartBusy">
          <StockChart
            v-if="chartTab === 'quote' && klineData.length"
            :kline-data="klineData"
            :indicators="[]"
            :us-rate-data="usRate"
            :us-rate-label="rateMaturity"
            @range-change="onKlineRangeChange"
          />
          <KLineChart
            v-if="chartTab === 'kline' && maPoints.length"
            :key="`ma-${selected.code}-${maPeriod}`"
            :points="maPoints" :ma-periods="[5, 10, 20, 30]" />
          <KLineChart
            v-if="chartTab === 'bigkline' && bigPoints.length"
            :key="`big-${selected.code}-${maPeriod}`"
            :points="bigPoints" :ma-periods="[5, 10, 20, 30]" />
          <MarketMatrixChart
            v-if="chartTab === 'matrix' && indexes.length"
            :key="`matrix-${indexes.length}`"
            :items="indexes" @select="selected = $event" />
          <IntradayTrendChart
            v-if="chartTab === 'intraday' && trend && trend.code === selected.code"
            :key="`trend-${selected.code}-${trend.date}`"
            :data="trend" />
          <div v-if="chartEmpty" class="qa-empty">
            {{ chartTab === 'intraday' ? '暂无分时数据（采集器尚未入库该指数分时）' : '暂无行情数据' }}
          </div>
        </div>

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

/* 图表 Tab（下划线风格，与指数页一致） */
.chart-tabs { display: flex; gap: 4px; border-bottom: 1px solid #ebeef5; margin: 4px 0 12px; }
.chart-tab {
  padding: 5px 12px; cursor: pointer; font-size: 13px; color: #606266;
  border-bottom: 2px solid transparent; margin-bottom: -1px; user-select: none;
  white-space: nowrap;
}
.chart-tab:hover { color: #409eff; }
.chart-tab.active { color: #409eff; border-bottom-color: #409eff; font-weight: 600; }

.chart-toolbar { display: flex; align-items: center; gap: 12px; margin-bottom: 10px; flex-wrap: wrap; }
.chart-title { color: #606266; font-size: 12px; }
.period-tabs { display: flex; border: 1px solid #dcdfe6; border-radius: 6px; overflow: hidden; }
.period-item { padding: 3px 14px; cursor: pointer; font-size: 12px; color: #606266; background: #fff; user-select: none; }
.period-item + .period-item { border-left: 1px solid #dcdfe6; }
.period-item.active { background: #409eff; color: #fff; }
</style>