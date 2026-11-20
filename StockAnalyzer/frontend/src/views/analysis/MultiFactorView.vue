<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import type { KlinePeriod, PresetInfo, RuleGroup, ScreeningResult } from '../../types/analysis'
import { analysisErrorMessage, fetchPresets, loadPreset, runScreening } from '../../api/analysis'
import { useStockSelectionStore } from '../../stores/stockSelection'
import CollapsibleSidebar from '../../components/analysis/CollapsibleSidebar.vue'
import RuleBuilder from '../../components/analysis/RuleBuilder.vue'
import StockTable from '../../components/analysis/StockTable.vue'

const router = useRouter()
const store = useStockSelectionStore()

const STOCK_POOLS = ['全部A股', '创业板']
const PERIODS: { value: KlinePeriod; label: string }[] = [
  { value: '60min', label: '60分' },
  { value: 'daily', label: '日线' },
  { value: 'weekly', label: '周线' },
  { value: 'monthly', label: '月线' },
]

const stockPool = ref('全部A股')
const rules = ref<RuleGroup>({ logic: 'AND', conditions: [] })
const presets = ref<PresetInfo[]>([])
const selectedPreset = ref('')
const results = ref<ScreeningResult[]>([])
const loading = ref(false)
const error = ref('')
const period = ref<KlinePeriod>('daily')
const dateRange = ref({ start: '2024-06-01', end: new Date().toISOString().slice(0, 10) })
const meta = ref<{ total: number; matched: number } | null>(null)

const presetDesc = ref('')

async function loadPresets() {
  try { presets.value = await fetchPresets() } catch { /* 忽略 */ }
}

async function onPresetChange(key: string) {
  selectedPreset.value = key
  presetDesc.value = presets.value.find((p) => p.key === key)?.description ?? ''
  if (!key) {
    rules.value = { logic: 'AND', conditions: [] }
    return
  }
  try {
    rules.value = await loadPreset(key)
  } catch (e) {
    ElMessage.error(analysisErrorMessage(e))
  }
}

async function run() {
  if (!rules.value.conditions.length) return
  loading.value = true
  meta.value = null
  error.value = ''
  try {
    const res = await runScreening({
      stockPool: stockPool.value,
      rules: rules.value,
      startDate: dateRange.value.start,
      endDate: dateRange.value.end,
      period: period.value,
    })
    results.value = res.data ?? []
    meta.value = res.meta ?? null
  } catch (e) {
    error.value = analysisErrorMessage(e)
    ElMessage.error(error.value)
  } finally {
    loading.value = false
  }
}

function openStock(row: ScreeningResult) {
  store.selectStock({ code: row.code, name: row.name })
  router.push('/technical')
}

onMounted(loadPresets)
</script>

<template>
  <div class="qa-page">
    <CollapsibleSidebar :width="300">
      <h3 class="side-title">多因子筛选</h3>

      <div class="qa-field">
        <span class="qa-label">股票池</span>
        <el-select v-model="stockPool" size="small">
          <el-option v-for="p in STOCK_POOLS" :key="p" :label="p" :value="p" />
        </el-select>
      </div>

      <div class="qa-field">
        <span class="qa-label">预设策略</span>
        <el-select
          :model-value="selectedPreset"
          size="small"
          placeholder="-- 自定义规则 --"
          clearable
          @update:model-value="(v: string) => onPresetChange(v ?? '')"
        >
          <el-option v-for="p in presets" :key="p.key" :label="p.name" :value="p.key" />
        </el-select>
        <p v-if="presetDesc" class="qa-muted preset-desc">{{ presetDesc }}</p>
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
          style="width: 100%; margin-bottom: 6px"
        />
        <el-date-picker
          v-model="dateRange.end"
          type="date"
          size="small"
          value-format="YYYY-MM-DD"
          style="width: 100%"
        />
      </div>

      <el-button
        type="primary"
        :disabled="!rules.conditions.length"
        :loading="loading"
        @click="run"
      >{{ loading ? '筛选中...' : '执行筛选' }}</el-button>

      <div v-if="meta" class="meta-box">
        <p>扫描股票：{{ meta.total }} 只</p>
        <p>符合条件：<b>{{ meta.matched }}</b> 只</p>
      </div>
      <p v-if="error" class="error-text">{{ error }}</p>
    </CollapsibleSidebar>

    <div class="qa-main">
      <div class="qa-card">
        <div class="qa-card-head">
          <h4>筛选规则</h4>
          <span class="qa-muted">点击表格行可在「技术指标」中查看该股票</span>
        </div>
        <RuleBuilder v-model="rules" />
      </div>

      <div class="qa-card">
        <div class="qa-card-head">
          <h4>筛选结果</h4>
          <span v-if="meta" class="qa-muted">符合 {{ meta.matched }} / 扫描 {{ meta.total }}</span>
        </div>
        <StockTable :results="results" :loading="loading" @select="openStock" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.side-title {
  font-size: 15px;
  color: #303133;
}
.period-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
}
.period-grid button { width: 100%; }
.preset-desc { margin-top: 4px; line-height: 1.5; }
.meta-box {
  background: #f5f7fa;
  border-radius: 6px;
  padding: 10px 12px;
  font-size: 13px;
  color: #606266;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.meta-box b { color: #409eff; }
.error-text { color: #f56c6c; font-size: 12px; }
</style>