<script setup lang="ts">
import type { Trade } from '../../types/analysis'

defineProps<{ trades: Trade[] }>()

function tone(v: number) {
  return v >= 0 ? 'up' : 'down'
}
function signed(v: number, digits = 2, suffix = '') {
  return `${v >= 0 ? '+' : ''}${Number(v).toFixed(digits)}${suffix}`
}
</script>

<template>
  <div v-if="!trades.length" class="qa-empty">无交易记录</div>
  <el-table v-else :data="trades" size="small" max-height="420">
    <el-table-column label="买入日期" width="110" prop="entryDate" />
    <el-table-column label="买入价" width="90" align="right">
      <template #default="{ row }"><span class="mono">{{ Number(row.entryPrice).toFixed(2) }}</span></template>
    </el-table-column>
    <el-table-column label="卖出日期" width="110" prop="exitDate" />
    <el-table-column label="卖出价" width="90" align="right">
      <template #default="{ row }"><span class="mono">{{ Number(row.exitPrice).toFixed(2) }}</span></template>
    </el-table-column>
    <el-table-column label="数量" width="90" align="right" prop="shares" />
    <el-table-column label="盈亏" width="110" align="right">
      <template #default="{ row }">
        <span class="mono" :class="tone(row.pnl)">{{ signed(row.pnl) }}</span>
      </template>
    </el-table-column>
    <el-table-column label="收益率" width="100" align="right">
      <template #default="{ row }">
        <span class="mono" :class="tone(row.pnlPct)">{{ signed(row.pnlPct, 2, '%') }}</span>
      </template>
    </el-table-column>
    <el-table-column label="退出原因" min-width="120" prop="exitReason" />
  </el-table>
</template>

<style scoped>
.mono { font-family: 'SFMono-Regular', Consolas, monospace; }
</style>