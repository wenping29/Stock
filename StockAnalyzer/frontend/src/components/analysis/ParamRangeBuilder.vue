<script setup lang="ts">
import type { ParamRange } from '../../types/analysis'

const props = defineProps<{ modelValue: ParamRange[] }>()
const emit = defineEmits<{ (e: 'update:modelValue', value: ParamRange[]): void }>()

const INDICATOR_PARAMS: Record<string, { label: string; params: { key: string; label: string }[] }> = {
  RSI: { label: 'RSI', params: [{ key: 'period', label: '周期' }] },
  KDJ: { label: 'KDJ', params: [{ key: 'n', label: 'N' }, { key: 'k', label: 'K' }, { key: 'd', label: 'D' }] },
  WR: { label: 'WR', params: [{ key: 'period', label: '周期' }] },
  MA: { label: 'MA', params: [{ key: 'period', label: '周期' }] },
  EMA: { label: 'EMA', params: [{ key: 'period', label: '周期' }] },
  MACD: { label: 'MACD', params: [{ key: 'fast', label: '快线' }, { key: 'slow', label: '慢线' }, { key: 'signal', label: '信号' }] },
  BOLL: { label: 'BOLL', params: [{ key: 'period', label: '周期' }, { key: 'stddev', label: '标准差' }] },
  VOL: { label: 'VOL', params: [{ key: 'period', label: '周期' }] },
  OBV: { label: 'OBV', params: [{ key: 'maPeriod', label: 'MA周期' }] },
  VolumeRatio: { label: '量比', params: [{ key: 'period', label: '周期' }] },
  ATR: { label: 'ATR', params: [{ key: 'period', label: '周期' }] },
}

function addRange() {
  emit('update:modelValue', [...props.modelValue, { indicator: 'RSI', param: 'period', min: 6, max: 20, step: 2 }])
}

function updateRange(index: number, patch: Partial<ParamRange>) {
  emit(
    'update:modelValue',
    props.modelValue.map((r, i) => (i === index ? { ...r, ...patch } : r)),
  )
}

function changeIndicator(index: number, value: string) {
  const first = INDICATOR_PARAMS[value]?.params[0]?.key ?? 'period'
  updateRange(index, { indicator: value, param: first })
}

function removeRange(index: number) {
  emit('update:modelValue', props.modelValue.filter((_, i) => i !== index))
}
</script>

<template>
  <div class="range-builder">
    <div v-for="(r, i) in modelValue" :key="i" class="range-card">
      <div class="range-head">
        <span class="qa-muted">参数 {{ i + 1 }}</span>
        <el-button link type="danger" size="small" @click="removeRange(i)">移除</el-button>
      </div>
      <el-select
        :model-value="r.indicator"
        size="small"
        style="width: 100%"
        @update:model-value="(v: string) => changeIndicator(i, v)"
      >
        <el-option v-for="(info, key) in INDICATOR_PARAMS" :key="key" :label="info.label" :value="key" />
      </el-select>
      <el-select
        :model-value="r.param"
        size="small"
        style="width: 100%"
        @update:model-value="(v: string) => updateRange(i, { param: v })"
      >
        <el-option
          v-for="p in INDICATOR_PARAMS[r.indicator]?.params ?? []"
          :key="p.key"
          :label="p.label"
          :value="p.key"
        />
      </el-select>
      <div class="range-grid">
        <div>
          <span class="qa-muted">最小</span>
          <el-input-number
            :model-value="r.min"
            size="small"
            controls-position="right"
            style="width: 100%"
            @update:model-value="(v: number | undefined) => updateRange(i, { min: v ?? 0 })"
          />
        </div>
        <div>
          <span class="qa-muted">最大</span>
          <el-input-number
            :model-value="r.max"
            size="small"
            controls-position="right"
            style="width: 100%"
            @update:model-value="(v: number | undefined) => updateRange(i, { max: v ?? 0 })"
          />
        </div>
        <div>
          <span class="qa-muted">步长</span>
          <el-input-number
            :model-value="r.step"
            size="small"
            :min="0.1"
            controls-position="right"
            style="width: 100%"
            @update:model-value="(v: number | undefined) => updateRange(i, { step: v ?? 1 })"
          />
        </div>
      </div>
    </div>
    <el-button class="add-range" size="small" @click="addRange">+ 添加参数范围</el-button>
  </div>
</template>

<style scoped>
.range-builder {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.range-card {
  background: #fafbfc;
  border: 1px solid #ebeef5;
  border-radius: 6px;
  padding: 8px 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.range-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.range-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 6px;
}
.add-range {
  width: 100%;
  border-style: dashed;
}
</style>