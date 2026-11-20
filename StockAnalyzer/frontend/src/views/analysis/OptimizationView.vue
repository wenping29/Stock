<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import type {
  BacktestConfig,
  KlinePeriod,
  OptimizationConfig,
  OptimizationResult,
  ParamRange,
  PerformanceMetrics,
  RuleGroup,
  StockInfo,
  WalkForwardConfig,
  WalkForwardResult,
} from '../../types/analysis'
import { analysisErrorMessage, runGridSearch, runWalkForward, searchStock } from '../../api/analysis'
import { useStockSelectionStore } from '../../stores/stockSelection'
import CollapsibleSidebar from '../../components/analysis/CollapsibleSidebar.vue'
import RuleBuilder from '../../components/analysis/RuleBuilder.vue'
import ParamRangeBuilder from '../../components/analysis/ParamRangeBuilder.vue'

const store = useStockSelectionStore()

const DEFAULT_ENTRY_RULES: RuleGroup = {
  logic: 'AND',
  conditions: [{ indicator: 'RSI', operator: '<', value: 30, params: { period: 14 } }],
}

type MetricKey = keyof PerformanceMetrics

const METRIC_OPTIONS: { value: MetricKey; label: string }[] = [
  { value: 'sharpeRatio', label: '夏普比率' },
  { value: 'annualizedReturn', label: '年化收益' },
  { value: 'profitFactor', label: '盈亏比' },
  { value: 'winRate', label: '胜率' },
  { value: 'calmarRatio', label: '卡玛比率' },
]

const query = ref('')
const searchResults = ref<StockInfo[]>([])
const selectedStock = ref<StockInfo | null>(null)
const startDate = ref('2022-01-01')
const endDate = ref('2024-12-31')
const period = ref<KlinePeriod>('daily')
const entryRules = ref<RuleGroup>(DEFAULT_ENTRY_RULES)
const paramRanges = ref<ParamRange[]>([])
const metric = ref<MetricKey>('sharpeRatio')
const topN = ref(10)
const mode = ref<'grid' | 'walkforward'>('grid')
const insampleDays = ref(504)
const outsampleDays = ref(252)

const loading = ref(false)
const error = ref('')
const results = ref<OptimizationResult[] | null>(null)
const wfResults = ref<WalkForwardResult[] | null>(null)

const paramKeys = computed(() =>
  results.value?.length ? Object.keys(results.value[0].params) : [],
)

let timer: ReturnType<typeof setTimeout> | undefined
watch(query, (q) => {
  clearTimeout(timer)
  if (q.length < 2) {
    searchResults.value = []
    return
  }
  timer = setTimeout(async () => {
    try {
      const r = await searchStock(q)
      searchResults.value = r.slice(0, 10)
    } catch {
      searchResults.value = []
    }
  }, 300)
})

watch(
  () => store.seq,
  () => {
    if (store.stock) pickStock(store.stock)
  },
)

onMounted(() => {
  if (store.stock) pickStock(store.stock)
})

function pickStock(s: StockInfo) {
  selectedStock.value = s
  query.value = s.name
  searchResults.value = []
}

function paramText(params: Record<string, number>) {
  return Object.entries(params).map(([k, v]) => `${k}=${v}`).join(', ')
}

async function run() {
  if (!selectedStock.value || paramRanges.value.length === 0) return
  loading.value = true
  error.value = ''
  results.value = null
  wfResults.value = null

  const baseConfig: Omit<BacktestConfig, 'code' | 'name' | 'startDate' | 'endDate' | 'period'> = {
    initialCapital: 100000,
    entryRules: entryRules.value,
    exitRules: { stopLossPct: 5, takeProfitPct: 15 },
    positionMethod: 'fixed',
    positionSizing: 0.1,
    commission: 0.0003,
    slippage: 0.001,
  }
  const common = {
    code: selectedStock.value.code,
    name: selectedStock.value.name,
    startDate: startDate.value,
    endDate: endDate.value,
    period: period.value,
    baseConfig,
    paramRanges: paramRanges.value,
    metric: metric.value,
    topN: topN.value,
  }

  try {
    if (mode.value === 'grid') {
      const res = await runGridSearch(common as OptimizationConfig)
      results.value = res.data
      if (!res.data.length) ElMessage.warning('当前参数组合下无有效结果')
    } else {
      const cfg: WalkForwardConfig = {
        ...common,
        insampleDays: insampleDays.value,
        outsampleDays: outsampleDays.value,
      }
      const res = await runWalkForward(cfg)
      wfResults.value = res.data
      if (!res.data.length) ElMessage.warning('样本区间不足，未生成滚动窗口')
    }
  } catch (e) {
    error.value = analysisErrorMessage(e)
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="qa-page">
    <CollapsibleSidebar :width="320">
      <h3 class="side-title">参数优化</h3>

      <div class="qa-field">
        <span class="qa-label">股票</span>
        <el-input v-model="query" size="small" placeholder="输入代码或名称..." clearable />
        <div v-if="searchResults.length" class="qa-result-dropdown">
          <div v-for="s in searchResults" :key="s.code" class="qa-result-item" @click="pickStock(s)">
            <span>{{ s.name }}</span>
            <span class="code">{{ s.code }}</span>
          </div>
        </div>
        <p v-if="selectedStock" class="qa-muted picked">已选：{{ selectedStock.code }} {{ selectedStock.name }}</p>
      </div>

      <div class="qa-grid-2">
        <div class="qa-field">
          <span class="qa-label">开始日期</span>
          <el-date-picker v-model="startDate" type="date" size="small" value-format="YYYY-MM-DD" style="width: 100%" />
        </div>
        <div class="qa-field">
          <span class="qa-label">结束日期</span>
          <el-date-picker v-model="endDate" type="date" size="small" value-format="YYYY-MM-DD" style="width: 100%" />
        </div>
      </div>

      <div class="qa-field">
        <span class="qa-label">模式</span>
        <div class="qa-seg">
          <button :class="{ active: mode === 'grid' }" @click="mode = 'grid'">网格搜索</button>
          <button :class="{ active: mode === 'walkforward' }" @click="mode = 'walkforward'">滚动优化</button>
        </div>
      </div>

      <div v-if="mode === 'walkforward'" class="qa-grid-2">
        <div class="qa-field">
          <span class="qa-label">样本内（天）</span>
          <el-input-number v-model="insampleDays" size="small" :min="20" :step="21" controls-position="right" style="width: 100%" />
        </div>
        <div class="qa-field">
          <span class="qa-label">样本外（天）</span>
          <el-input-number v-model="outsampleDays" size="small" :min="10" :step="21" controls-position="right" style="width: 100%" />
        </div>
      </div>

      <div class="qa-field">
        <span class="qa-label">优化指标</span>
        <el-select v-model="metric" size="small" style="width: 100%">
          <el-option v-for="m in METRIC_OPTIONS" :key="m.value" :label="m.label" :value="m.value" />
        </el-select>
      </div>

      <div class="qa-field">
        <span class="qa-label">返回前 N 名</span>
        <el-input-number v-model="topN" size="small" :min="1" :max="50" controls-position="right" style="width: 100%" />
      </div>

      <div class="qa-field">
        <span class="qa-label">入场规则</span>
        <RuleBuilder v-model="entryRules" />
      </div>

      <div class="qa-field">
        <span class="qa-label">参数范围</span>
        <ParamRangeBuilder v-model="paramRanges" />
      </div>

      <el-button
        type="primary"
        :disabled="!selectedStock || paramRanges.length === 0 || loading"
        :loading="loading"
        @click="run"
      >
        {{ loading ? '优化中...' : '运行优化' }}
      </el-button>
      <p v-if="!paramRanges.length" class="qa-muted hint">至少添加一个参数范围后才能运行</p>
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    </CollapsibleSidebar>

    <div class="qa-main">
      <div v-if="!loading && !results && !wfResults" class="qa-card qa-empty">
        请选择股票、设置参数范围，点击运行优化
      </div>
      <div v-else-if="loading" class="qa-card qa-empty">正在运行参数优化...</div>

      <div v-else-if="results" class="qa-card">
        <div class="qa-card-head"><h4>网格搜索结果（Top {{ results.length }}）</h4></div>
        <el-table :data="results" size="small" :row-class-name="({ rowIndex }: { rowIndex: number }) => (rowIndex === 0 ? 'best-row' : '')">
          <el-table-column label="排名" width="60" align="center">
            <template #default="{ $index }">{{ $index + 1 }}</template>
          </el-table-column>
          <el-table-column v-for="k in paramKeys" :key="k" :label="k" width="80" align="right">
            <template #default="{ row }"><span class="mono">{{ row.params[k] }}</span></template>
          </el-table-column>
          <el-table-column label="夏普" width="80" align="right">
            <template #default="{ row }"><span class="mono">{{ row.metrics.sharpeRatio.toFixed(2) }}</span></template>
          </el-table-column>
          <el-table-column label="年化收益" width="95" align="right">
            <template #default="{ row }"><span class="mono up">{{ row.metrics.annualizedReturn.toFixed(1) }}%</span></template>
          </el-table-column>
          <el-table-column label="最大回撤" width="95" align="right">
            <template #default="{ row }"><span class="mono down">{{ row.metrics.maxDrawdown.toFixed(1) }}%</span></template>
          </el-table-column>
          <el-table-column label="胜率" width="80" align="right">
            <template #default="{ row }"><span class="mono">{{ row.metrics.winRate.toFixed(1) }}%</span></template>
          </el-table-column>
          <el-table-column label="交易次数" width="90" align="right" prop="totalTrades" />
        </el-table>
      </div>

      <template v-else-if="wfResults">
        <div class="qa-card">
          <div class="qa-card-head"><h4>滚动优化结果（{{ wfResults.length }} 个窗口）</h4></div>
        </div>
        <div
          v-for="(w, i) in wfResults"
          :key="i"
          class="qa-card qa-wf-card"
          :class="{ overfit: w.overfit }"
        >
          <div class="wf-head">
            <span class="qa-muted">
              窗口 {{ i + 1 }}：样本内 {{ w.window.insampleStart }} ~ {{ w.window.insampleEnd }}
              → 样本外 {{ w.window.outsampleStart }} ~ {{ w.window.outsampleEnd }}
            </span>
            <el-tag v-if="w.overfit" type="danger" size="small">过拟合警告</el-tag>
          </div>
          <div class="qa-stat-grid wf-grid">
            <div class="qa-stat">
              <div class="label">样本内夏普</div>
              <div class="value">{{ w.insampleBest.metrics.sharpeRatio.toFixed(2) }}</div>
            </div>
            <div class="qa-stat">
              <div class="label">样本外夏普</div>
              <div class="value" :class="w.overfit ? 'down' : 'up'">{{ w.outsampleMetrics.sharpeRatio.toFixed(2) }}</div>
            </div>
            <div class="qa-stat">
              <div class="label">最优参数</div>
              <div class="value param">{{ paramText(w.insampleBest.params) }}</div>
            </div>
            <div class="qa-stat">
              <div class="label">样本外交易次数</div>
              <div class="value">{{ w.outsampleTrades }}</div>
            </div>
          </div>
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.side-title { font-size: 15px; color: #303133; }
.picked { margin-top: 4px; color: #409eff; }
.hint { margin-top: 4px; }
.mono { font-family: 'SFMono-Regular', Consolas, monospace; }
:deep(.best-row) { background: #ecf5ff; }
.wf-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 10px; gap: 8px; flex-wrap: wrap; }
.wf-grid { grid-template-columns: repeat(4, minmax(0, 1fr)); }
.value.param { font-size: 13px; color: #409eff; }
@media (max-width: 900px) { .wf-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
</style>