<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { CustomIndexItem, CustomIndexPreset, IndexEtf, IndexItem, IndexTrendData, KLinePoint } from '../types'
import { fetchIndexEtfs, fetchIndices, fetchIndexTrend } from '../api/quote'
import {
  createCustomIndex, deleteCustomIndex, fetchCustomIndexPreset, fetchCustomIndexPresets,
  fetchCustomIndices, fetchCustomIndexKline, updateCustomIndex,
} from '../api/customIndex'
import { fetchMoneySupply } from '../api/liquidity'
import {
  computeIndicators, fetchCnRateHistory, fetchIndexKline, fetchStockList,
  fetchUsdRateHistory, fetchUsRateHistory,
} from '../api/analysis'
import type { IndicatorResult, KlineData, KlinePeriod, StockInfo } from '../types/analysis'
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

// ---- 自定义指数（用户按股票 + 权重合成，接口按当前用户隔离） ----
const customItems = ref<CustomIndexItem[]>([])
const selectedCustom = ref<CustomIndexItem | null>(null)
const customDialog = ref(false)
const customSaving = ref(false)
const editingId = ref<number | null>(null)
const formName = ref('')
const formBaseValue = ref(1000)
interface ComponentRow {
  symbol: string
  name: string
  weight: number
  /** 所属板块：优先行业板块，缺省时按代码推断上市板块 */
  board: string
}
const formRows = ref<ComponentRow[]>([])

// 已选成分股分页（预设可能一次导入数百只，避免一次渲染过多行）
const COMP_PAGE_SIZE = 50
const compPage = ref(1)
const pickedRows = computed(() => formRows.value.filter((r) => r.symbol))
const pagedPickedRows = computed(() =>
  pickedRows.value.slice((compPage.value - 1) * COMP_PAGE_SIZE, compPage.value * COMP_PAGE_SIZE))
const selectedSymbols = computed(() => new Set(pickedRows.value.map((r) => r.symbol)))

// 预设指数（官方成分股快速导入）
const presets = ref<CustomIndexPreset[]>([])
const presetKey = ref('')
const presetLoading = ref(false)

// 可选个股池：弹窗打开时加载一次，本地做市场/关键字筛选与分页
const stockPool = ref<StockInfo[]>([])
const stockPoolLoading = ref(false)
const stockKeyword = ref('')
type BoardFilter = 'all' | 'sh' | 'sz' | 'chinext' | 'star' | 'bj'
const stockBoard = ref<BoardFilter>('all')
const STOCK_PAGE_SIZE = 60
const stockPage = ref(1)

function matchBoard(code: string, filter: BoardFilter): boolean {
  switch (filter) {
    case 'sh': return code.startsWith('6')
    case 'sz': return code.startsWith('0') || code.startsWith('3')
    case 'chinext': return code.startsWith('30')
    case 'star': return code.startsWith('68')
    case 'bj': return code.startsWith('4') || code.startsWith('8') || code.startsWith('92')
    default: return true
  }
}

/** 代码推断上市板块（与后端 ScreeningService.BoardOf 保持一致）。 */
function boardOf(code: string): string {
  if (code.startsWith('688') || code.startsWith('689')) return '科创板'
  if (code.startsWith('300') || code.startsWith('301')) return '创业板'
  if (code.startsWith('6')) return '沪市A股'
  if (code.startsWith('0') || code.startsWith('3')) return '深市A股'
  if (code.startsWith('920') || code.startsWith('8')) return '北交所'
  if (code.startsWith('4')) return '新三板'
  return '其他'
}

/** 个股所属板块：优先行业板块，缺省时按代码推断上市板块。 */
function sectorOf(symbol: string, industry?: string): string {
  return industry?.trim() || boardOf(symbol)
}

const stockInfoMap = computed(() => new Map(stockPool.value.map((s) => [s.code, s])))

const filteredStocks = computed(() => {
  const kw = stockKeyword.value.trim().toLowerCase()
  return stockPool.value.filter((s) =>
    matchBoard(s.code, stockBoard.value) &&
    (!kw || s.code.toLowerCase().includes(kw) || s.name.toLowerCase().includes(kw)))
})
const pagedStocks = computed(() =>
  filteredStocks.value.slice((stockPage.value - 1) * STOCK_PAGE_SIZE, stockPage.value * STOCK_PAGE_SIZE))

watch([stockKeyword, stockBoard], () => { stockPage.value = 1 })

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
const ALL_TABS: { key: TabKey; label: string }[] = [
  { key: 'quote', label: '行情K线' },
  { key: 'kline', label: '日K · 均线' },
  { key: 'bigkline', label: '大数据K线' },
  { key: 'matrix', label: '股市矩阵' },
  { key: 'intraday', label: '日内走势' },
]
// 自定义指数没有采集分时数据，隐藏"日内走势"
const tabs = computed(() =>
  selectedCustom.value ? ALL_TABS.filter((t) => t.key !== 'intraday') : ALL_TABS)
const tab = ref<TabKey>('quote')

// 自定义指数不支持 60 分钟周期
const periodKeys = computed(() =>
  selectedCustom.value ? PERIOD_KEYS.filter((p) => p !== '60min') : PERIOD_KEYS)

const points = ref<KLinePoint[]>([])
const bigPoints = ref<KLinePoint[]>([])
const trend = ref<IndexTrendData | null>(null)
const klineLoading = ref(false)
const trendLoading = ref(false)

// ---- 行情K线 Tab：区间查询（预设一年/全部、自定义日期）+ 60分/日/周/月 + 美债叠加 ----
const quotePeriod = ref<PeriodKey>('day')
const klineData = ref<KlineData[]>([])
const usRate = ref<{ date: string; rate: number }[]>([])
const usdRate = ref<{ date: string; rate: number }[]>([])
const cnRateOverlay = ref<{ date: string; rate: number }[]>([])
const moneySupply = ref<{ month: string; m0: number; m1: number; m2: number }[]>([])
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
    const rows = selectedCustom.value
      ? await fetchCustomIndexKline(
          selectedCustom.value.id, dateRange.value.start, dateRange.value.end,
          PERIOD_MAP[quotePeriod.value])
      : await fetchIndexKline(
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

async function loadUsdRate() {
  try { usdRate.value = await fetchUsdRateHistory() } catch { /* 忽略 */ }
}

async function loadMoneySupply() {
  try {
    const res = await fetchMoneySupply(120)
    moneySupply.value = res.points.map((p) => ({ month: p.month, m0: p.m0, m1: p.m1, m2: p.m2 }))
  } catch { /* 忽略 */ }
}

async function loadCnRateOverlay() {
  try { cnRateOverlay.value = await fetchCnRateHistory('CN_10Y') } catch { /* 忽略 */ }
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
  // 自定义指数并入 A股宽基统一展示，卡片右上角打“自定义”标识
  if (customItems.value.length) {
    if (!map.has('A股宽基')) map.set('A股宽基', [])
    map.get('A股宽基')!.push(...customItems.value.map(customToIndexItem))
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

async function loadCustom() {
  try {
    customItems.value = await fetchCustomIndices()
  } catch {
    customItems.value = []
  }
  const current = selectedCustom.value
  if (!current) return
  const fresh = customItems.value.find((c) => c.id === current.id)
  if (!fresh) {
    // 已被删除：回到第一个内置指数
    selectedCustom.value = null
    selected.value = items.value[0] ?? null
    if (selected.value) await loadTabData()
    return
  }
  selectedCustom.value = fresh
  if (selected.value) {
    selected.value = {
      ...selected.value,
      name: fresh.name,
      price: fresh.price,
      changePct: fresh.changePct,
      trend: fresh.trend,
    }
  }
}

function select(item: IndexItem) {
  if (!selectedCustom.value && selected.value?.code === item.code) return
  selectedCustom.value = null
  selected.value = item
  loadEtfs(item.code)
  loadTabData()
}

// ---- 指数关联的场内 ETF（仅公开指数，后端按指数缓存 1 分钟） ----
const etfs = ref<IndexEtf[]>([])
let etfSeq = 0
async function loadEtfs(code: string) {
  const seq = ++etfSeq
  try {
    const rows = await fetchIndexEtfs(code)
    if (seq === etfSeq) etfs.value = rows
  } catch {
    if (seq === etfSeq) etfs.value = []
  }
}

/** 自定义指数映射为 IndexItem，与公开指数合并展示（custom 标记用于卡片右上角标识）。 */
function customToIndexItem(ci: CustomIndexItem): IndexItem {
  return {
    code: `CI${ci.id}`,
    name: ci.name,
    group: 'A股宽基',
    price: ci.price,
    changePct: ci.changePct,
    amount: 0,
    trend: ci.trend,
    custom: true,
  }
}

/** 选中自定义指数：映射为 IndexItem 复用卡片/详情结构。 */
function selectCustom(ci: CustomIndexItem) {
  selectedCustom.value = ci
  selected.value = customToIndexItem(ci)
  etfs.value = []
  etfSeq++
  if (tab.value === 'intraday') tab.value = 'quote'
  if (quotePeriod.value === '60min') quotePeriod.value = 'day'
  if (period.value === '60min') period.value = 'day'
  loadTabData()
}

/** 统一卡片点击：自定义指数走自定义详情，公开指数走内置详情。 */
function onSelectIndex(item: IndexItem) {
  if (item.custom) {
    const ci = customItems.value.find((c) => `CI${c.id}` === item.code)
    if (ci) selectCustom(ci)
    return
  }
  select(item)
}

function customByCode(code: string) {
  return customItems.value.find((c) => `CI${c.id}` === code) ?? null
}

function editCustomCard(item: IndexItem) {
  const ci = customByCode(item.code)
  if (ci) openEdit(ci)
}

function removeCustomCard(item: IndexItem) {
  const ci = customByCode(item.code)
  if (ci) removeCustom(ci)
}

// ---- 自定义指数：新建 / 编辑 / 删除 ----
async function loadStockPool() {
  if (stockPool.value.length || stockPoolLoading.value) return
  stockPoolLoading.value = true
  try {
    stockPool.value = await fetchStockList('all')
  } catch {
    stockPool.value = []
  } finally {
    stockPoolLoading.value = false
  }
}

async function loadPresets() {
  if (presets.value.length) return
  try {
    presets.value = await fetchCustomIndexPresets()
  } catch {
    presets.value = []
  }
}

function resetPickerState() {
  stockKeyword.value = ''
  stockBoard.value = 'all'
  stockPage.value = 1
  compPage.value = 1
  presetKey.value = ''
  loadStockPool()
  loadPresets()
}

function openCreate() {
  editingId.value = null
  formName.value = ''
  formBaseValue.value = 1000
  formRows.value = []
  resetPickerState()
  customDialog.value = true
}

function openEdit(ci: CustomIndexItem) {
  editingId.value = ci.id
  formName.value = ci.name
  formBaseValue.value = ci.baseValue
  formRows.value = ci.components.map((c) => ({
    symbol: c.symbol,
    name: c.name,
    weight: c.weight,
    board: sectorOf(c.symbol, stockInfoMap.value.get(c.symbol)?.industry),
  }))
  resetPickerState()
  customDialog.value = true
}

/** 点击个股：已选则移除，未选则追加为权重股。 */
function toggleStock(s: StockInfo) {
  const index = formRows.value.findIndex((r) => r.symbol === s.code)
  if (index >= 0) {
    formRows.value.splice(index, 1)
    clampCompPage()
    return
  }
  formRows.value.push({ symbol: s.code, name: s.name, weight: 10, board: sectorOf(s.code, s.industry) })
}

function removeComponent(row: ComponentRow) {
  const index = formRows.value.indexOf(row)
  if (index >= 0) formRows.value.splice(index, 1)
  clampCompPage()
}

function clearComponents() {
  formRows.value = []
  compPage.value = 1
}

/** 删除成分后页码越界时回退到最后一页。 */
function clampCompPage() {
  const maxPage = Math.max(1, Math.ceil(pickedRows.value.length / COMP_PAGE_SIZE))
  if (compPage.value > maxPage) compPage.value = maxPage
}

/** 导入预设指数成分股（已有成分股时确认后整体替换）。 */
async function importPreset() {
  if (!presetKey.value) return ElMessage.warning('请先选择预设指数')
  const preset = presets.value.find((p) => p.key === presetKey.value)
  if (pickedRows.value.length) {
    try {
      await ElMessageBox.confirm(
        `将用「${preset?.name ?? presetKey.value}」的成分股替换当前 ${pickedRows.value.length} 只成分股？`,
        '导入预设成分股',
        { type: 'warning', confirmButtonText: '替换', cancelButtonText: '取消' },
      )
    } catch {
      return
    }
  }

  presetLoading.value = true
  try {
    const detail = await fetchCustomIndexPreset(presetKey.value)
    formRows.value = detail.components.map((c) => ({
      symbol: c.symbol,
      name: c.name,
      weight: c.weight,
      board: sectorOf(c.symbol, stockInfoMap.value.get(c.symbol)?.industry),
    }))
    compPage.value = 1
    ElMessage.success(`已导入 ${detail.components.length} 只成分股`)
  } catch {
    ElMessage.error('获取预设成分股失败，请稍后重试')
  } finally {
    presetLoading.value = false
  }
}

/** 已选成分股等权分配。 */
function equalizeWeights() {
  const picked = pickedRows.value
  if (!picked.length) return
  const weight = Math.round((100 / picked.length) * 100) / 100
  picked.forEach((r) => { r.weight = weight })
}

async function saveCustom() {
  const name = formName.value.trim()
  const components = formRows.value.filter((r) => r.symbol)
  if (!name) return ElMessage.warning('请填写指数名称')
  if (!components.length) return ElMessage.warning('请至少添加一只成分股')
  if (components.some((r) => !(r.weight > 0))) return ElMessage.warning('权重必须大于 0')
  if (new Set(components.map((r) => r.symbol)).size !== components.length)
    return ElMessage.warning('成分股不能重复')

  customSaving.value = true
  try {
    const payload = {
      name,
      baseValue: formBaseValue.value || 1000,
      components: components.map((r) => ({ symbol: r.symbol, name: r.name, weight: r.weight })),
    }
    let createdId: number | null = null
    if (editingId.value) {
      await updateCustomIndex(editingId.value, payload)
    } else {
      createdId = (await createCustomIndex(payload)).id
    }
    customDialog.value = false
    ElMessage.success(editingId.value ? '已保存' : '已创建，成分股数据采集中，稍后可见完整走势')
    await loadCustom()
    const created = createdId ? customItems.value.find((c) => c.id === createdId) : null
    if (created) {
      tab.value = 'quote'
      selectCustom(created)
    } else {
      await loadTabData()
    }
  } finally {
    customSaving.value = false
  }
}

async function removeCustom(ci: CustomIndexItem) {
  try {
    await ElMessageBox.confirm(`确定删除自定义指数「${ci.name}」？`, '删除自定义指数', {
      type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消',
    })
  } catch {
    return
  }
  await deleteCustomIndex(ci.id)
  ElMessage.success('已删除')
  if (selectedCustom.value?.id === ci.id) {
    selectedCustom.value = null
    selected.value = items.value[0] ?? null
  }
  await loadCustom()
  if (selected.value) await loadTabData()
}

/** 自定义指数"重新计算"：重载清单与当前 Tab 数据。 */
async function refreshCustom() {
  await loadCustom()
  await loadTabData()
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
      const rows = selectedCustom.value
        ? await fetchCustomIndexKline(selectedCustom.value.id, '', '', PERIOD_MAP[period.value], limit)
        : await fetchIndexKline(selected.value.code, '', '', PERIOD_MAP[period.value], limit)
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
  if (selectedCustom.value) {
    await refreshCustom()
  } else if (tab.value === 'intraday') {
    await loadTrend()
  } else {
    await load()
    await loadTabData()
  }
}

onMounted(() => {
  load()
  loadCustom()
  loadUsRate()
  loadUsdRate()
  loadCnRateOverlay()
  loadMoneySupply()
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

    <!-- A股宽基（置顶，自定义指数合并展示：卡片右上角带“自定义”标识） -->
    <div class="page-card" v-for="g in topGroups" :key="`top-${g.group}`">
      <IndexGroupCard :title="g.group" :items="g.list"
        :selected-code="selected?.code" @select="onSelectIndex"
        @edit="editCustomCard" @remove="removeCustomCard">
        <template #extra>
          <el-button size="small" type="primary" @click="openCreate">新建自定义指数</el-button>
        </template>
      </IndexGroupCard>
    </div>

    <!-- 选中指数详情：Tab 切换 行情K线 / 日K均线 / 大数据K线 / 股市矩阵 / 日内走势 -->
    <div class="page-card" v-if="selected">
      <div class="kline-head">
        <h3>{{ selected.name }} <span class="code">{{ selectedCustom ? '自定义指数' : selected.code }}</span></h3>
        <span class="price" :class="pctClass(selected.changePct)">{{ selected.price.toFixed(2) }}</span>
        <span :class="pctClass(selected.changePct)">{{ fmtPct(selected.changePct) }}</span>
        <span v-if="selected.amount" class="amount">成交 {{ selected.amount >= 10000 ? (selected.amount / 10000).toFixed(2) + ' 万亿' : selected.amount.toFixed(0) + ' 亿' }}</span>
        <span v-if="tab === 'quote'" class="range">{{ dateRange.start }} ~ {{ dateRange.end }}</span>
        <div style="flex: 1" />
        <el-button v-if="selectedCustom" size="small" :loading="klineLoading" @click="refreshCustom">重新计算</el-button>
        <RefreshButton v-else :category="refreshCategory" label="刷新数据" @done="onCollected" />
        <div class="detail-tabs">
          <span v-for="t in tabs" :key="t.key"
            class="detail-tab" :class="{ active: tab === t.key }" @click="pickTab(t.key)">
            {{ t.label }}
          </span>
        </div>
      </div>

      <!-- 自定义指数成分股与权重 -->
      <div v-if="selectedCustom" class="ci-components">
        <span class="ci-label">成分股</span>
        <span v-for="c in selectedCustom.components" :key="c.symbol" class="ci-chip">
          {{ c.name || c.symbol }} <em>{{ c.weight }}%</em>
        </span>
        <span class="ci-label">基准 {{ selectedCustom.baseValue }} 点</span>
      </div>

      <!-- 指数关联的场内 ETF -->
      <div v-if="!selectedCustom && etfs.length" class="ci-components">
        <span class="ci-label">相关ETF</span>
        <span v-for="e in etfs" :key="e.symbol" class="etf-chip">
          <span class="etf-name">{{ e.name }}</span>
          <em>{{ e.symbol }}</em>
          <b v-if="e.price !== null">{{ e.price.toFixed(3) }}</b>
          <span v-if="e.changePct !== null" :class="pctClass(e.changePct)">{{ fmtPct(e.changePct) }}</span>
        </span>
      </div>

      <!-- 行情K线 Tab：区间预设/自定义日期、K线·折线、美债叠加由图表工具栏提供 -->
      <div class="chart-toolbar" v-if="tab === 'quote'">
        <div class="period-tabs">
          <span v-for="p in periodKeys" :key="p"
            class="period-item" :class="{ active: quotePeriod === p }" @click="pickQuotePeriod(p)">
            {{ PERIOD_LABELS[p] }}
          </span>
        </div>
        <span class="chart-title">预设 1月/3月/6月/1年/全部 · 自定义起止日期 · K线 / 折线切换 · 美债/美元利率/人民币利率叠加（图表右上角可全屏）</span>
      </div>

      <!-- K 线类 Tab 的周期切换 -->
      <div class="chart-toolbar" v-else-if="tab === 'kline' || tab === 'bigkline'">
        <div class="period-tabs">
          <span v-for="p in periodKeys" :key="p"
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
          :usd-rate-data="usdRate"
          :cn-rate-data="cnRateOverlay"
          :money-supply-data="moneySupply"
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
          :description="selectedCustom
            ? '暂无合成数据（成分股日线数据采集中，稍后点击“重新计算”）'
            : tab === 'intraday'
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

    <!-- 新建 / 编辑自定义指数 -->
    <el-dialog v-model="customDialog" :title="editingId ? '编辑自定义指数' : '新建自定义指数'"
      width="1120px" :close-on-click-modal="false" top="5vh">
      <div class="ci-layout">
      <div class="ci-main">
      <div class="ci-form-row">
        <span class="ci-form-label">指数名称</span>
        <el-input v-model="formName" maxlength="40" show-word-limit
          placeholder="如：我的核心资产组合" style="max-width: 340px" />
      </div>
      <div class="ci-form-row">
        <span class="ci-form-label">基准点位</span>
        <el-input-number v-model="formBaseValue" :min="1" :max="100000" :step="100" />
        <span class="ci-hint">首日点位，默认 1000</span>
      </div>

      <!-- 快速配置：按官方成分股导入 -->
      <div class="ci-form-row">
        <span class="ci-form-label">快速配置</span>
        <el-select v-model="presetKey" filterable placeholder="选择预设指数" style="width: 260px">
          <el-option v-for="p in presets" :key="p.key"
            :label="`${p.name}（${p.count} 只）`" :value="p.key" />
        </el-select>
        <el-button type="primary" plain :loading="presetLoading" :disabled="!presetKey"
          @click="importPreset">导入成分股</el-button>
        <span class="ci-hint">官方权重优先，无权重数据时等权</span>
      </div>

      <div class="ci-rows-head">
        <span class="ci-form-label">已选成分股（{{ pickedRows.length }} 只）</span>
        <div>
          <el-button size="small" text :disabled="!pickedRows.length"
            @click="equalizeWeights">等权分配</el-button>
          <el-button size="small" text type="danger" :disabled="!pickedRows.length"
            @click="clearComponents">清空</el-button>
        </div>
      </div>
      <el-table v-if="pickedRows.length" :data="pagedPickedRows" size="small" max-height="200">
        <el-table-column prop="symbol" label="代码" width="90" />
        <el-table-column prop="name" label="名称" min-width="140" />
        <el-table-column prop="board" label="板块" width="110" show-overflow-tooltip />
        <el-table-column label="权重(%)" width="140">
          <template #default="{ row }">
            <el-input-number v-model="row.weight" :min="0.0001" :max="10000" :precision="2"
              :controls="false" size="small" style="width: 100%" />
          </template>
        </el-table-column>
        <el-table-column label="操作" width="70">
          <template #default="{ row }">
            <el-button size="small" text type="danger" @click="removeComponent(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div v-else class="ci-hint">尚未选择成分股：可在右侧个股列表点击添加，或通过上方预设一键导入。</div>
      <div class="ci-pager" v-if="pickedRows.length > COMP_PAGE_SIZE">
        <el-pagination v-model:current-page="compPage" :page-size="COMP_PAGE_SIZE"
          :total="pickedRows.length" layout="prev, pager, next" small />
      </div>
      </div>

      <!-- 右侧独立选股区：点击加入 / 移除权重股 -->
      <aside class="ci-side">
        <div class="ci-side-head">
          <span class="ci-side-title">可选个股</span>
          <span class="ci-side-count">共 {{ filteredStocks.length }} 只</span>
        </div>
        <div class="ci-picker-tools">
          <el-select v-model="stockBoard" size="small" style="width: 120px">
            <el-option label="全部市场" value="all" />
            <el-option label="沪市" value="sh" />
            <el-option label="深市" value="sz" />
            <el-option label="创业板" value="chinext" />
            <el-option label="科创板" value="star" />
            <el-option label="北交所" value="bj" />
          </el-select>
          <el-input v-model="stockKeyword" size="small" clearable
            placeholder="筛选代码 / 名称" style="flex: 1" />
        </div>
        <div v-loading="stockPoolLoading" class="ci-stock-grid">
          <button v-for="s in pagedStocks" :key="s.code" type="button"
            class="ci-stock" :class="{ active: selectedSymbols.has(s.code) }"
            @click="toggleStock(s)">
            <span class="ci-stock-name">{{ s.name }}</span>
            <span class="ci-stock-code">{{ s.code }}</span>
          </button>
          <div v-if="!stockPoolLoading && !pagedStocks.length" class="ci-hint">没有匹配的个股</div>
        </div>
        <div class="ci-pager" v-if="filteredStocks.length > STOCK_PAGE_SIZE">
          <el-pagination v-model:current-page="stockPage" :page-size="STOCK_PAGE_SIZE"
            :total="filteredStocks.length" layout="prev, pager, next" small />
        </div>
      </aside>
      </div>
      <div class="ci-hint">
        权重按合计归一化；保存后自动采集成分股日线，成分股较多时首次合成需要一些时间。
      </div>
      <template #footer>
        <el-button @click="customDialog = false">取消</el-button>
        <el-button type="primary" :loading="customSaving" @click="saveCustom">保存</el-button>
      </template>
    </el-dialog>
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

/* 自定义指数：详情成分股与编辑弹窗 */
.ci-components { display: flex; align-items: center; flex-wrap: wrap; gap: 6px; margin: -2px 0 10px; }
.ci-label { color: #909399; font-size: 12px; }
.ci-chip {
  font-size: 12px; color: #606266; background: #f5f7fa;
  border: 1px solid #ebeef5; border-radius: 4px; padding: 1px 6px;
}
.ci-chip em { font-style: normal; color: #409eff; margin-left: 2px; }
.etf-chip {
  display: inline-flex; align-items: center; gap: 6px;
  font-size: 12px; color: #606266; background: #f5f7fa;
  border: 1px solid #ebeef5; border-radius: 4px; padding: 1px 6px;
}
.etf-chip em { font-style: normal; color: #909399; }
.etf-chip b { font-weight: 600; }
.ci-form-row { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; }
.ci-form-label { width: 80px; color: #606266; font-size: 13px; }
.ci-rows-head { display: flex; align-items: center; justify-content: space-between; margin: 4px 0 8px; }
.ci-hint { color: #909399; font-size: 12px; }

/* 弹窗左右两栏：左侧编辑，右侧独立选股区（点击加入 / 移除权重股） */
.ci-layout { display: flex; gap: 18px; align-items: stretch; }
.ci-main { flex: 1; min-width: 0; }
.ci-side {
  width: 360px; flex: none; display: flex; flex-direction: column;
  border-left: 1px solid #ebeef5; padding-left: 18px;
}
.ci-side-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px; }
.ci-side-title { font-size: 14px; font-weight: 600; color: #303133; }
.ci-side-count { font-size: 12px; color: #909399; }
.ci-picker-tools { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
.ci-stock-grid {
  display: grid; grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 6px; height: 420px; overflow-y: auto; padding: 4px;
  border: 1px solid #ebeef5; border-radius: 6px; align-content: start;
}
.ci-stock {
  display: flex; flex-direction: column; align-items: flex-start; gap: 1px;
  padding: 4px 8px; background: #fff; border: 1px solid #ebeef5;
  border-radius: 4px; cursor: pointer; text-align: left; font: inherit;
}
.ci-stock:hover { border-color: #a0cfff; }
.ci-stock.active { background: #ecf5ff; border-color: #409eff; }
.ci-stock-name {
  font-size: 12px; color: #303133; max-width: 100%;
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.ci-stock-code { font-size: 11px; color: #909399; }
.ci-stock.active .ci-stock-name { color: #409eff; font-weight: 600; }
.ci-pager { display: flex; justify-content: flex-end; margin-top: 8px; }
</style>
