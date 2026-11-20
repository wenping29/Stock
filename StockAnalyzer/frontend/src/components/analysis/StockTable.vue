<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { ScreeningResult } from '../../types/analysis'

const props = defineProps<{ results: ScreeningResult[]; loading?: boolean }>()
const emit = defineEmits<{ (e: 'select', row: ScreeningResult): void }>()

const page = ref(1)
const PAGE_SIZE = 20

const totalPages = computed(() => Math.max(1, Math.ceil(props.results.length / PAGE_SIZE)))
const pageItems = computed(() => props.results.slice((page.value - 1) * PAGE_SIZE, page.value * PAGE_SIZE))

watch(
  () => props.results,
  () => { page.value = 1 },
)

function pctClass(v: number) {
  return v >= 0 ? 'up' : 'down'
}
</script>

<template>
  <div>
    <el-table
      :data="pageItems"
      v-loading="loading"
      size="small"
      row-class-name="row-click"
      @row-click="(row: ScreeningResult) => emit('select', row)"
    >
      <el-table-column label="代码" width="100">
        <template #default="{ row }"><span class="mono">{{ row.code }}</span></template>
      </el-table-column>
      <el-table-column prop="name" label="名称" min-width="120" />
      <el-table-column label="收盘价" width="110" align="right">
        <template #default="{ row }"><span class="mono">{{ Number(row.close).toFixed(2) }}</span></template>
      </el-table-column>
      <el-table-column label="涨跌幅" width="110" align="right">
        <template #default="{ row }">
          <span class="mono" :class="pctClass(row.changePct)">
            {{ row.changePct >= 0 ? '+' : '' }}{{ Number(row.changePct).toFixed(2) }}%
          </span>
        </template>
      </el-table-column>
      <template #empty>
        <el-empty description="暂无结果，请调整筛选条件后重试" :image-size="60" />
      </template>
    </el-table>

    <div v-if="results.length > PAGE_SIZE" class="qa-pager">
      <span>共 {{ results.length }} 条</span>
      <el-pagination
        v-model:current-page="page"
        :page-size="PAGE_SIZE"
        :total="results.length"
        layout="prev, pager, next"
        small
      />
    </div>
    <p v-else-if="results.length" class="qa-pager">
      <span>共 {{ results.length }} 条结果</span>
    </p>
  </div>
</template>

<style scoped>
.mono { font-family: 'SFMono-Regular', Consolas, monospace; }
.qa-pager :deep(.el-pagination) { padding: 0; }
</style>