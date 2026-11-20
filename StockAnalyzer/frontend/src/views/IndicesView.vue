<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import type { IndexItem, IndexTrendData, KLinePoint } from '../types'
import { fetchIndices, fetchIndexTrend } from '../api/quote'
import { fetchIndexKline, fetchUsRateHistory, computeIndicators } from '../api/analysis'
import type { IndicatorResult, KlineData, KlinePeriod } from '../types/analysis'
import type { CollectCategory } from '../api/collect'
import RefreshButton from '../components/RefreshButton.vue'
import IndexGroupCard from '../components/IndexGroupCard.vue'
import StockChart from '../components/analysis/StockChart.vue'
import IndicatorChart from '../components/analysis/IndicatorChart.vue'
import KLineChart from '../components/KLineChart.vue'
import MarketMatrixChart from '../components/MarketMatrixChart.vue'
import IntradayTrendChart from '../components/IntradayTrendChart.vue'

const route = useRoute()

const items = ref<IndexItem[]>([])
const loading = ref(true)

const selected = ref<IndexItem | null>(null)
type PeriodKey = '60min' | 'day' | 'week' | 'month'
const period = ref<PeriodKey>('day')

const PERIOD_MAP: Record<PeriodKey, KlinePeriod> = {
  '60min': '60min',
  day: 'daily',
  week: 'weekly',
  month: 'monthly',
}
const PERIOD_LABELS: Record<PeriodKey, string> = {
  '60min': '60分',
  day: '日',
  week: '周',
  month: '月',
}
const PERIOD_KEYS = Object.keys(PERIOD_LABELS) as PeriodKey[]

/** 量化接口 KlineData（date/amount）转指数页图表点（time/changePct）。 */
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

// 详情区 Tab：行情K线 / 日K均线 / 大数据K线 / 股市矩阵 / 日内走势
type TabKey = 'quote' | 'kline' | 'bigkline' | 'matrix' | 'intraday'
const tabs: { key: TabKey; label: string }[] = [
  { key: 'quote', label: '行情K线' },
  { key: 'kline', label: '日K · 均线' },
  { key: 'bigkline', label: '大数据K线' },
  { key: 'matrix', label: '股市矩阵' },
  { key: 'intraday', label: '日内走势' },
]
const tab = ref<TabKey>('quote')

const points = ref<KLinePoint[]>([])
const bigPoints = ref<KLinePoint[]>([])
const trend = ref<IndexTrendData | null>(null)
const klineLoading = ref(false)
const trendLoading = ref(false)

// ---- 行情K线 Tab：区间查询（预设一年/全部、自定义日期）+ 60分/日/周/月 + 美债叠加 ----
const quotePeriod = ref<PeriodKey>('day')
const klineData = ref<KlineData[]>([])
const usRate = ref<{ date: string; rate: number }[]>([])
const rateMaturity = ref('10Y')
const dateRange = ref((() => {
  const end = new Date()
  const start = new Date(end)
  start.setFullYear(end.getFullYear() - 1)
  return { start: start.toISOString().slice(0, 10), end: end.toISOString().slice(0, 10) }
})())

let quoteSeq = 0
async function loadQuote() {
  if (!selected.value) return
  const seq = ++quoteSeq
  klineLoading.value = true
  try {
    const rows = await fetchIndexKline(
      selected.value.code, dateRange.value.start, dateRange.value.end, PERIOD_MAP[quotePeriod.value])
    if (seq !== quoteSeq) return
    klineData.value = rows
  } catch {
    if (seq === quoteSeq) klineData.value = []
  } finally {
    if (seq === quoteSeq) klineLoading.value = false
  }
}

function onQuoteRangeChange(range: { start: string; end: string; months: number | 'all' }) {
  dateRange.value = range.months === 'all'
    ? { start: '2000-01-01', end: range.end }
    : { start: range.start, end: range.end }
  loadQuote()
}

function pickQuotePeriod(p: PeriodKey) {
  if (quotePeriod.value === p) return
  quotePeriod.value = p
  if (tab.value === 'quote') loadQuote()
}

async function loadUsRate() {
  try { usRate.value = await fetchUsRateHistory(rateMaturity.value) } catch { /* 忽略 */ }
}

// ---- 行情K线技术指标按钮：成交量 / MA / BOLL / MACD / RSI ----
type IndKey = 'VOL' | 'MA' | 'BOLL' | 'MACD' | 'RSI'
const IND_BUTTONS: { key: IndKey; label: string }[] = [
  { key: 'VOL', label: '成交量' },
  { key: 'MA', label: 'MA' },
  { key: 'BOLL', label: 'BOLL' },
  { key: 'MACD', label: 'MACD' },
  { key: 'RSI', label: 'RSI' },
]
const activeInds = ref<Set<IndKey>>(new Set())
const overlayInds = ref<IndicatorResult[]>([])
const paneInds = ref<IndicatorResult[]>([])
let indSeq = 0

/** MA/BOLL 叠加在主图，MACD/RSI 在下方独立副图；成交量由 StockChart 直接绘制 */
async function refreshIndicators() {
  const data = klineData.value
  const defs: { name: string; params: Record<string, number> }[] = []
  if (data.length) {
    if (activeInds.value.has('MA'))
      for (const p of [5, 10, 20, 30]) defs.push({ name: 'MA', params: { period: p } })
    if (activeInds.value.has('BOLL'))
      defs.push({ name: 'BOLL', params: { period: 20, stddev: 2 } })
    if (activeInds.value.has('MACD')) defs.push({ name: 'MACD', params: {} })
    if (activeInds.value.has('RSI')) defs.push({ name: 'RSI', params: { period: 14 } })
  }
  if (!defs.length) {
    indSeq++
    overlayInds.value = []
    paneInds.value = []
    return
  }
  const seq = ++indSeq
  try {
    const res = await computeIndicators(data, defs)
    if (seq !== indSeq) return
    overlayInds.value = res.filter((r) => r.name === 'MA' || r.name === 'BOLL')
    paneInds.value = res.filter((r) => r.name === 'MACD' || r.name === 'RSI')
  } catch {
    if (seq !== indSeq) return
    overlayInds.value = []
    paneInds.value = []
  }
}

function toggleInd(k: IndKey) {
  const next = new Set(activeInds.value)
  if (next.has(k)) next.delete(k)
  else next.add(k)
  activeInds.value = next
  refreshIndicators()
}

watch(klineData, () => { if (activeInds.value.size) refreshIndicators() })

// 股市矩阵排除“辅助指标”（深证综指，仅用于两市成交量统计）
const matrixItems = computed(() => items.value.filter((i) => i.group !== '辅助指标'))

const groups = computed(() => {
  const map = new Map<string, IndexItem[]>()
  // "辅助指标"分组（如深证综指）用于成交量统计，不在指数页展示
  for (const it of items.value.filter((i) => i.group !== '辅助指标')) {
    if (!map.has(it.group)) map.set(it.group, [])
    map.get(it.group)!.push(it)
  }
  return [...map.entries()].map(([group, list]) => ({ group, list }))
})

// A股宽基置顶（在指数详情卡上方），其余分组排在详情下方
const topGroups = computed(() => groups.value.filter((g) => g.group === 'A股宽基'))
const restGroups = computed(() => groups.value.filter((g) => g.group !== 'A股宽基'))

/** 当前 Tab 是否无数据可展示 */
const showEmpty = computed(() => {
  if (tab.value === 'quote') return !klineData.value.length
  if (tab.value === 'kline') return !points.value.length
  if (tab.value === 'bigkline') return !bigPoints.value.length
  if (tab.value === 'matrix') return !matrixItems.value.length
  return !trend.value
})

async function load() {
  loading.value = true
  try {
    items.value = await fetchIndices()
    if (!selected.value && items.value.length) {
      // 支持首页跳转带入 ?code= 直达指定指数
      const code = route.query.code as string
      select(items.value.find((i) => i.code === code) ?? items.value.find((i) => i.code === '000300') ?? items.value[0])
    }
  } finally {
    loading.value = false
  }
}

function select(item: IndexItem) {
  if (selected.value?.code === item.code) return
  selected.value = item
  loadTabData()
}

function pickPeriod(p: PeriodKey) {
  if (period.value === p) return
  period.value = p
  loadTabData()
}

function pickTab(t: TabKey) {
  if (tab.value === t) return
  tab.value = t
  loadTabData()
}

/** 按当前 Tab 加载数据（股市矩阵直接用已加载的指数清单，无需请求） */
async function loadTabData() {
  if (!selected.value) return
  if (tab.value === 'quote') {
    await loadQuote()
  } else if (tab.value === 'kline' || tab.value === 'bigkline') {
    klineLoading.value = true
    try {
      const limit = tab.value === 'bigkline' ? 500 : 120
      const rows = await fetchIndexKline(selected.value.code, '', '', PERIOD_MAP[period.value], limit)
      const mapped = toKlinePoints(rows)
      if (tab.value === 'bigkline') bigPoints.value = mapped
      else points.value = mapped
    } finally {
      klineLoading.value = false
    }
  } else if (tab.value === 'intraday') {
    await loadTrend()
  }
}

async function loadTrend() {
  if (!selected.value) return
  trendLoading.value = true
  try {
    trend.value = await fetchIndexTrend(selected.value.code)
  } catch {
    trend.value = null
  } finally {
    trendLoading.value = false
  }
}

// 日内走势 Tab 每 60 秒自动刷新（与后端缓存周期一致）；离开 Tab 停止
let trendTimer: number | undefined
watch(tab, (t) => {
  if (trendTimer) {
    clearInterval(trendTimer)
    trendTimer = undefined
  }
  if (t === 'intraday') trendTimer = window.setInterval(loadTrend, 60_000)
})
onUnmounted(() => {
  if (trendTimer) clearInterval(trendTimer)
})

function pctClass(v: number) {
  return v >= 0 ? 'up' : 'down'
}
function fmtPct(v: number) {
  return `${v >= 0 ? '+' : ''}${v.toFixed(2)}%`
}

// 顶部"刷新"：按当前 Tab 触发对应分类采集，后台完成后重载数据
const refreshCategory = computed<CollectCategory>(() =>
  tab.value === 'intraday' ? 'intraday' : 'indices'
)
async function onCollected() {
  if (tab.value === 'intraday') {
    await loadTrend()
  } else {
    await load()
    await loadTabData()
  }
}

onMounted(() => {
  load()
  loadUsRate()
})
</script>

<template>
  <div v-loading="loading">
    <!-- 数据尚未采集完成的空状态 -->
    <div class="page-card" v-if="!items.length">
      <el-empty description="指数数据尚未采集完成（首次启动或东财接口限流时约需几分钟到一小时），稍后点击刷新；也可在管理后台手动触发“同步指数”">
        <el-button :loading="loading" @click="load">刷新</el-button>
      </el-empty>
    </div>

    <!-- A股宽基（置顶，先看指数概览再看详情） -->
    <div class="page-card" v-for="g in topGroups" :key="`top-${g.group}`">
      <IndexGroupCard :title="g.group" :items="g.list"
        :selected-code="selected?.code" @select="select" />
    </div>

    <!-- 选中指数详情：Tab 切换 行情K线 / 日K均线 / 大数据K线 / 股市矩阵 / 日内走势 -->
    <div class="page-card" v-if="selected">
      <div class="kline-head">
        <h3>{{ selected.name }} <span class="code">{{ selected.code }}</span></h3>
        <span class="price" :class="pctClass(selected.changePct)">{{ selected.price.toFixed(2) }}</span>
        <span :class="pctClass(selected.changePct)">{{ fmtPct(selected.changePct) }}</span>
        <span v-if="selected.amount" class="amount">成交 {{ selected.amount >= 10000 ? (selected.amount / 10000).toFixed(2) + ' 万亿' : selected.amount.toFixed(0) + ' 亿' }}</span>
        <span v-if="tab === 'quote'" class="range">{{ dateRange.start }} ~ {{ dateRange.end }}</span>
        <div style="flex: 1" />
        <RefreshButton :category="refreshCategory" label="刷新数据" @done="onCollected" />
        <div class="detail-tabs">
          <span v-for="t in tabs" :key="t.key"
            class="detail-tab" :class="{ active: tab === t.key }" @click="pickTab(t.key)">
            {{ t.label }}
          </span>
        </div>
      </div>

      <!-- 行情K线 Tab：区间预设/自定义日期、K线·折线、美债叠加由图表工具栏提供 -->
      <div class="chart-toolbar" v-if="tab === 'quote'">
        <div class="period-tabs">
          <span v-for="p in PERIOD_KEYS" :key="p"
            class="period-item" :class="{ active: quotePeriod === p }" @click="pickQuotePeriod(p)">
            {{ PERIOD_LABELS[p] }}
          </span>
        </div>
        <span class="chart-title">预设 1月/3月/6月/1年/全部 · 自定义起止日期 · K线 / 折线切换 · 美债叠加（图表右上角可全屏）</span>
      </div>

      <!-- K 线类 Tab 的周期切换 -->
      <div class="chart-toolbar" v-else-if="tab === 'kline' || tab === 'bigkline'">
        <div class="period-tabs">
          <span v-for="p in PERIOD_KEYS" :key="p"
            class="period-item" :class="{ active: period === p }" @click="pickPeriod(p)">
            {{ PERIOD_LABELS[p] }}
          </span>
        </div>
        <span class="chart-title">
          {{
            tab === 'kline'
              ? 'K 线 + MA5 · MA10 · MA20 · MA30（涨红跌绿，下方为成交量，滚轮/滑块可缩放）'
              : '最近 500 根 K 线（大数据量），拖动底部滑块或滚轮缩放查看历史区间'
          }}
        </span>
      </div>
      <div class="chart-toolbar" v-else-if="tab === 'matrix'">
        <span class="chart-title">色块大小 = 当日成交额，颜色 = 涨跌幅（红涨绿跌），点击色块可切换指数</span>
      </div>
      <div class="chart-toolbar" v-else-if="tab === 'intraday'">
        <span class="chart-title">
          分时走势（{{ trend?.date || '最近交易日' }}，每 60 秒自动刷新）· 昨收不在当日波动区间时价格轴自动断轴压缩空档
        </span>
      </div>

      <div v-loading="tab === 'intraday' ? trendLoading : klineLoading">
        <StockChart
          v-if="tab === 'quote' && klineData.length"
          :kline-data="klineData" :indicators="overlayInds"
          :show-volume="activeInds.has('VOL')"
          :us-rate-data="usRate" :us-rate-label="rateMaturity"
          @range-change="onQuoteRangeChange">
          <template #toolbar>
            <span v-for="b in IND_BUTTONS" :key="b.key"
              class="ind-chip" :class="{ active: activeInds.has(b.key) }" @click="toggleInd(b.key)">
              {{ b.label }}
            </span>
          </template>
        </StockChart>
        <template v-if="tab === 'quote'">
          <IndicatorChart
            v-for="ind in paneInds" :key="ind.name"
            :kline-data="klineData" :indicator="ind" />
        </template>
        <KLineChart
          v-if="tab === 'kline' && points.length"
          :key="`kline-${selected.code}-${period}`"
          :points="points" :ma-periods="[5, 10, 20, 30]" />
        <KLineChart
          v-if="tab === 'bigkline' && bigPoints.length"
          :key="`bigkline-${selected.code}-${period}`"
          :points="bigPoints" :ma-periods="[5, 10, 20, 30]" />
        <MarketMatrixChart
          v-if="tab === 'matrix' && matrixItems.length"
          :key="`matrix-${matrixItems.length}`"
          :items="matrixItems" @select="select" />
        <IntradayTrendChart
          v-if="tab === 'intraday' && trend"
          :key="`trend-${selected.code}-${trend.date}`"
          :data="trend" />
        <el-empty v-if="showEmpty" :image-size="60"
          :description="tab === 'intraday'
            ? '暂无分时数据（东财实时接口暂不可用或该指数不支持）'
            : tab === 'quote'
              ? '暂无行情数据（可调整区间或点击“刷新数据”）'
              : '暂无数据'" />
      </div>
    </div>

    <!-- 其余指数分组 -->
    <div class="page-card" v-for="g in restGroups" :key="g.group">
      <IndexGroupCard :title="g.group" :items="g.list"
        :selected-code="selected?.code" @select="select" />
    </div>
  </div>
</template>

<style scoped>
.kline-head { display: flex; align-items: baseline; gap: 12px; margin-bottom: 12px; flex-wrap: wrap; }
.kline-head h3 { font-size: 17px; }
.code { color: #909399; font-size: 13px; font-weight: normal; }
.price { font-size: 22px; font-weight: 600; }
.amount { color: #909399; font-size: 13px; }
.range { color: #909399; font-size: 13px; }

/* 详情 Tab（下划线风格） */
.detail-tabs { display: flex; gap: 4px; border-bottom: 1px solid #ebeef5; }
.detail-tab {
  padding: 6px 14px; cursor: pointer; font-size: 14px; color: #606266;
  border-bottom: 2px solid transparent; margin-bottom: -1px; user-select: none;
  white-space: nowrap;
}
.detail-tab:hover { color: #409eff; }
.detail-tab.active { color: #409eff; border-bottom-color: #409eff; font-weight: 600; }

.chart-toolbar { display: flex; align-items: center; gap: 16px; margin-bottom: 10px; flex-wrap: wrap; }
.chart-title { color: #606266; font-size: 13px; }

/* 技术指标开关（成交量/MA/BOLL/MACD/RSI），渲染在 StockChart 工具栏美债按钮之后 */
.ind-chip {
  padding: 3px 10px; font-size: 12px; color: #606266; background: #f5f7fa;
  border: 1px solid #dcdfe6; border-radius: 4px; cursor: pointer; user-select: none;
}
.ind-chip:hover { color: #409eff; border-color: #a0cfff; }
.ind-chip.active { background: #409eff; border-color: #409eff; color: #fff; }
.period-tabs { display: flex; border: 1px solid #dcdfe6; border-radius: 6px; overflow: hidden; }
.period-item { padding: 4px 16px; cursor: pointer; font-size: 13px; color: #606266; background: #fff; user-select: none; }
.period-item + .period-item { border-left: 1px solid #dcdfe6; }
.period-item.active { background: #409eff; color: #fff; }
</style>
