<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import type {
  BacktestConfig,
  BacktestResult,
  KlinePeriod,
  RuleGroup,
  StockInfo,
} from '../../types/analysis'
import { analysisErrorMessage, runBacktest, searchStock } from '../../api/analysis'
import { useStockSelectionStore } from '../../stores/stockSelection'
import CollapsibleSidebar from '../../components/analysis/CollapsibleSidebar.vue'
import RuleBuilder from '../../components/analysis/RuleBuilder.vue'
import PerformanceCards from '../../components/analysis/PerformanceCards.vue'
import EquityCurveChart from '../../components/analysis/EquityCurveChart.vue'
import MonthlyHeatmap from '../../components/analysis/MonthlyHeatmap.vue'
import TradeTable from '../../components/analysis/TradeTable.vue'

const store = useStockSelectionStore()

const DEFAULT_ENTRY_RULES: RuleGroup = {
  logic: 'AND',
  conditions: [
    { indicator: 'EMA', operator: 'cross_above', value: 0, params: { period: 5 } },
    { indicator: 'MACD', operator: 'cross_above', value: 0, params: {} },
  ],
}
const DEFAULT_EXIT_RULES: RuleGroup = {
  logic: 'AND',
  conditions: [{ indicator: 'EMA', operator: 'cross_below', value: 0, params: { period: 20 } }],
}

const query = ref('')
const searchResults = ref<StockInfo[]>([])
const selectedStock = ref<StockInfo | null>(null)
const startDate = ref('2023-01-01')
const endDate = ref(new Date().toISOString().slice(0, 10))
const period = ref<KlinePeriod>('daily')
const initialCapital = ref(100000)
const positionMethod = ref<'fixed' | 'volatility_weighted'>('fixed')
const positionSize = ref(0.1)
const commission = ref(0.0003)
const slippage = ref(0.001)
const entryRules = ref<RuleGroup>(DEFAULT_ENTRY_RULES)
const exitRules = ref<RuleGroup>(DEFAULT_EXIT_RULES)
const stopLossPct = ref(5)
const takeProfitPct = ref(15)

const loading = ref(false)
const error = ref('')
const result = ref<BacktestResult | null>(null)

let timer: ReturnType<typeof setTimeout> | undefined
watch(query, (q) => {
  clearTimeout(timer)
  if (q.length < 2) {
    searchResults.value = []
    return
  }
  timer = setTimeout(async () => {
    try {
      const list = await searchStock(q)
      searchResults.value = list.slice(0, 10)
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

async function run() {
  if (!selectedStock.value) return
  loading.value = true
  error.value = ''
  result.value = null
  const config: BacktestConfig = {
    code: selectedStock.value.code,
    name: selectedStock.value.name,
    startDate: startDate.value,
    endDate: endDate.value,
    period: period.value,
    initialCapital: initialCapital.value,
    entryRules: entryRules.value,
    exitRules: {
      stopLossPct: stopLossPct.value,
      takeProfitPct: takeProfitPct.value,
      exitIndicator: exitRules.value,
    },
    positionMethod: positionMethod.value,
    positionSizing: positionSize.value,
    commission: commission.value,
    slippage: slippage.value,
  }
  try {
    result.value = await runBacktest(config)
    if (!result.value.trades?.length && !result.value.equityCurve?.length) {
      ElMessage.warning('回测完成，但未产生交易，请调整规则或区间')
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
      <h3 class="side-title">回测配置</h3>

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
        <span class="qa-label">周期</span>
        <div class="qa-seg">
          <button :class="{ active: period === 'daily' }" @click="period = 'daily'">日线</button>
          <button :class="{ active: period === 'weekly' }" @click="period = 'weekly'">周线</button>
          <button :class="{ active: period === 'monthly' }" @click="period = 'monthly'">月线</button>
        </div>
      </div>

      <div class="qa-field">
        <span class="qa-label">初始资金</span>
        <el-input-number v-model="initialCapital" size="small" :min="1000" :step="10000" controls-position="right" style="width: 100%" />
      </div>

      <div class="qa-field">
        <span class="qa-label">仓位方法</span>
        <el-radio-group v-model="positionMethod" size="small">
          <el-radio-button value="fixed">固定仓位</el-radio-button>
          <el-radio-button value="volatility_weighted">波动率加权</el-radio-button>
        </el-radio-group>
      </div>

      <div class="qa-field">
        <span class="qa-label">仓位比例（{{ (positionSize * 100).toFixed(0) }}%）</span>
        <el-slider v-model="positionSize" :min="0.01" :max="1" :step="0.01" :show-tooltip="false" />
      </div>

      <div class="qa-grid-2">
        <div class="qa-field">
          <span class="qa-label">佣金（{{ (commission * 100).toFixed(2) }}%）</span>
          <el-input-number v-model="commission" size="small" :step="0.0001" :precision="4" controls-position="right" style="width: 100%" />
        </div>
        <div class="qa-field">
          <span class="qa-label">滑点（{{ (slippage * 100).toFixed(1) }}%）</span>
          <el-input-number v-model="slippage" size="small" :step="0.001" :precision="3" controls-position="right" style="width: 100%" />
        </div>
      </div>

      <div class="qa-field">
        <span class="qa-label">入场规则</span>
        <RuleBuilder v-model="entryRules" />
      </div>

      <div class="qa-field">
        <span class="qa-label">止损止盈</span>
        <div class="qa-grid-2">
          <el-input-number v-model="stopLossPct" size="small" :min="0" controls-position="right" style="width: 100%" />
          <el-input-number v-model="takeProfitPct" size="small" :min="0" controls-position="right" style="width: 100%" />
        </div>
        <span class="qa-muted hint">左：止损 %　右：止盈 %</span>
      </div>

      <div class="qa-field">
        <span class="qa-label">退出指标信号</span>
        <RuleBuilder v-model="exitRules" />
      </div>

      <el-button type="primary" :disabled="!selectedStock || loading" :loading="loading" @click="run">
        {{ loading ? '回测中...' : '运行回测' }}
      </el-button>
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    </CollapsibleSidebar>

    <div class="qa-main">
      <div v-if="!result && !loading" class="qa-card qa-empty">请选择股票并配置回测参数，点击运行</div>
      <div v-else-if="loading" class="qa-card qa-empty">正在运行回测...</div>
      <template v-else-if="result">
        <div class="qa-card">
          <PerformanceCards :metrics="result.metrics" />
        </div>
        <div class="qa-card">
          <div class="qa-card-head"><h4>资金曲线</h4></div>
          <EquityCurveChart
            :equity-curve="result.equityCurve"
            :trades="result.trades"
            :initial-capital="initialCapital"
          />
        </div>
        <div class="qa-card">
          <div class="qa-card-head"><h4>月度收益热力图</h4></div>
          <MonthlyHeatmap :equity-curve="result.equityCurve" />
        </div>
        <div class="qa-card">
          <div class="qa-card-head"><h4>交易记录（{{ result.trades.length }}）</h4></div>
          <TradeTable :trades="result.trades" />
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.side-title { font-size: 15px; color: #303133; }
.picked { margin-top: 4px; color: #409eff; }
.hint { margin-top: 4px; }
</style>