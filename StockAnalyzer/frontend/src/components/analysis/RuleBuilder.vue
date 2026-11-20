<script setup lang="ts">
import type { RuleCondition, RuleGroup } from '../../types/analysis'

const props = defineProps<{ modelValue: RuleGroup }>()
const emit = defineEmits<{ (e: 'update:modelValue', value: RuleGroup): void }>()

const INDICATOR_OPTIONS: { value: string; label: string; defaultParams: Record<string, number> }[] = [
  { value: 'MA', label: 'MA (移动平均线)', defaultParams: { period: 20 } },
  { value: 'EMA', label: 'EMA (指数均线)', defaultParams: { period: 20 } },
  { value: 'MACD', label: 'MACD', defaultParams: {} },
  { value: 'BOLL', label: 'BOLL (布林带)', defaultParams: { period: 20, stddev: 2 } },
  { value: 'RSI', label: 'RSI (相对强弱)', defaultParams: { period: 14 } },
  { value: 'KDJ', label: 'KDJ (随机指标)', defaultParams: {} },
  { value: 'WR', label: 'WR (威廉指标)', defaultParams: { period: 14 } },
  { value: 'VOL', label: 'VOL (成交量均线)', defaultParams: { period: 5 } },
  { value: 'OBV', label: 'OBV (能量潮)', defaultParams: { maPeriod: 20 } },
  { value: 'VolumeRatio', label: '量比', defaultParams: { period: 5 } },
  { value: 'ATR', label: 'ATR (平均真实波幅)', defaultParams: { period: 14 } },
]

const OPERATOR_OPTIONS = [
  { value: '>', label: '大于' },
  { value: '<', label: '小于' },
  { value: '>=', label: '大于等于' },
  { value: '<=', label: '小于等于' },
  { value: 'cross_above', label: '上穿' },
  { value: 'cross_below', label: '下穿' },
] as const

function conditionsOf(group: RuleGroup): RuleCondition[] {
  return group.conditions as RuleCondition[]
}

function addCondition() {
  const first = INDICATOR_OPTIONS[0]
  emit('update:modelValue', {
    ...props.modelValue,
    conditions: [
      ...props.modelValue.conditions,
      { indicator: first.value, operator: '>', value: 50, params: { ...first.defaultParams } },
    ],
  })
}

function updateCondition(index: number, patch: Partial<RuleCondition>) {
  const updated = conditionsOf(props.modelValue).map((c, i) => (i === index ? { ...c, ...patch } : c))
  emit('update:modelValue', { ...props.modelValue, conditions: updated })
}

function changeIndicator(index: number, value: string) {
  const opt = INDICATOR_OPTIONS.find((o) => o.value === value)
  updateCondition(index, { indicator: value, params: { ...(opt?.defaultParams ?? {}) } })
}

function removeCondition(index: number) {
  emit('update:modelValue', {
    ...props.modelValue,
    conditions: props.modelValue.conditions.filter((_, i) => i !== index),
  })
}

function toggleLogic() {
  emit('update:modelValue', { ...props.modelValue, logic: props.modelValue.logic === 'AND' ? 'OR' : 'AND' })
}

function paramText(c: RuleCondition) {
  return Object.keys(c.params ?? {})
    .map((k) => `${k}=${c.params[k]}`)
    .join(' ')
}
</script>

<template>
  <div class="rule-builder">
    <div class="rule-toolbar">
      <span class="qa-muted">逻辑关系：</span>
      <el-button size="small" :type="modelValue.logic === 'AND' ? 'primary' : 'warning'" plain @click="toggleLogic">
        {{ modelValue.logic === 'AND' ? 'AND（全部满足）' : 'OR（任一满足）' }}
      </el-button>
      <el-button size="small" @click="addCondition">+ 添加条件</el-button>
    </div>

    <div v-for="(cond, idx) in conditionsOf(modelValue)" :key="idx" class="qa-rule-row">
      <span v-if="idx > 0" class="qa-rule-logic">{{ modelValue.logic }}</span>
      <el-select
        :model-value="cond.indicator"
        size="small"
        style="width: 160px"
        @update:model-value="(v: string) => changeIndicator(idx, v)"
      >
        <el-option v-for="o in INDICATOR_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
      </el-select>
      <el-select
        :model-value="cond.operator"
        size="small"
        style="width: 104px"
        @update:model-value="(v: RuleCondition['operator']) => updateCondition(idx, { operator: v })"
      >
        <el-option v-for="o in OPERATOR_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
      </el-select>
      <el-input-number
        :model-value="cond.value"
        size="small"
        :step="0.1"
        controls-position="right"
        style="width: 110px"
        @update:model-value="(v: number | undefined) => updateCondition(idx, { value: v ?? 0 })"
      />
      <span v-if="paramText(cond)" class="qa-muted">{{ paramText(cond) }}</span>
      <el-button link type="danger" size="small" class="rule-remove" @click="removeCondition(idx)">✕</el-button>
    </div>

    <p v-if="modelValue.conditions.length === 0" class="qa-rule-empty">点击“+ 添加条件”添加筛选条件</p>
  </div>
</template>

<style scoped>
.rule-builder {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.rule-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.rule-remove {
  margin-left: auto;
}
</style>