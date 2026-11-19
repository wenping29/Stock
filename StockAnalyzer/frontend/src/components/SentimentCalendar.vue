<script setup lang="ts">
import { computed, ref } from 'vue'
import type { SentimentHistoryPoint } from '../types'

// 情绪日历：按交易日格子显示情绪温度分，用冷热 5 档配色填充，悬停查看当日明细。
const props = defineProps<{ points: SentimentHistoryPoint[] }>()

interface Tone {
  label: string
  color: string
  bg: string
}

const TONES: Tone[] = [
  { label: '冰点', color: '#909399', bg: 'rgba(144, 147, 153, 0.16)' },
  { label: '低迷', color: '#409eff', bg: 'rgba(64, 158, 255, 0.14)' },
  { label: '中性', color: '#c9a227', bg: 'rgba(212, 177, 6, 0.16)' },
  { label: '活跃', color: '#e6a23c', bg: 'rgba(230, 162, 60, 0.18)' },
  { label: '亢奋', color: '#c0392b', bg: 'rgba(192, 57, 43, 0.16)' },
]

const now = new Date()
const cursor = ref(new Date(now.getFullYear(), now.getMonth(), 1))
const todayKey = formatKey(now)
const weekdays = ['一', '二', '三', '四', '五', '六', '日']

const byDate = computed(() => new Map(props.points.map((p) => [p.date, p])))

const cells = computed(() => {
  const year = cursor.value.getFullYear()
  const month = cursor.value.getMonth()
  // 周一为一周起点
  const offset = (new Date(year, month, 1).getDay() + 6) % 7
  const daysInMonth = new Date(year, month + 1, 0).getDate()
  const list: Array<{ key: string | null; day: number; point?: SentimentHistoryPoint }> = []

  for (let i = 0; i < offset; i++) list.push({ key: null, day: 0 })
  for (let d = 1; d <= daysInMonth; d++) {
    const key = formatKey(new Date(year, month, d))
    list.push({ key, day: d, point: byDate.value.get(key) })
  }
  while (list.length % 7 !== 0) list.push({ key: null, day: 0 })
  return list
})

const monthLabel = computed(() => `${cursor.value.getFullYear()} 年 ${cursor.value.getMonth() + 1} 月`)
const isCurrentMonth = computed(() => {
  const n = new Date()
  return cursor.value.getFullYear() === n.getFullYear() && cursor.value.getMonth() === n.getMonth()
})

const monthSummary = computed(() => {
  const prefix = `${cursor.value.getFullYear()}-${String(cursor.value.getMonth() + 1).padStart(2, '0')}-`
  const monthPoints = props.points.filter((p) => p.date.startsWith(prefix))
  if (!monthPoints.length) return null
  const avg = monthPoints.reduce((sum, p) => sum + p.sentimentScore, 0) / monthPoints.length
  return { days: monthPoints.length, avg }
})

function formatKey(d: Date) {
  const month = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${month}-${day}`
}

function tone(score: number): Tone {
  if (score >= 80) return TONES[4]
  if (score >= 60) return TONES[3]
  if (score >= 40) return TONES[2]
  if (score >= 20) return TONES[1]
  return TONES[0]
}

function move(delta: number) {
  cursor.value = new Date(cursor.value.getFullYear(), cursor.value.getMonth() + delta, 1)
}

function backToNow() {
  const n = new Date()
  cursor.value = new Date(n.getFullYear(), n.getMonth(), 1)
}

function fmtPct(v: number) {
  return `${v >= 0 ? '+' : ''}${v.toFixed(2)}%`
}
</script>

<template>
  <div class="sentiment-calendar">
    <div class="calendar-head">
      <el-button-group size="small">
        <el-button @click="move(-1)">‹</el-button>
        <el-button :disabled="isCurrentMonth" @click="move(1)">›</el-button>
      </el-button-group>
      <span class="month-label">{{ monthLabel }}</span>
      <el-button v-if="!isCurrentMonth" link size="small" @click="backToNow">回到本月</el-button>
      <div style="flex: 1" />
      <span class="month-summary" v-if="monthSummary">
        本月 {{ monthSummary.days }} 个交易日 · 平均 {{ monthSummary.avg.toFixed(1) }} 分
      </span>
      <div class="legend">
        <span v-for="t in TONES" :key="t.label" class="legend-item">
          <i :style="{ background: t.color }" />{{ t.label }}
        </span>
      </div>
    </div>

    <div class="week-head">
      <span v-for="w in weekdays" :key="w">周{{ w }}</span>
    </div>

    <div class="calendar-grid">
      <el-tooltip
        v-for="(cell, i) in cells"
        :key="i"
        placement="top"
        :show-after="150"
        :disabled="!cell.key"
      >
        <template #content>
          <template v-if="cell.point">
            <div class="tip-title">{{ cell.point.date }} · {{ tone(cell.point.sentimentScore).label }}</div>
            <div>情绪分 <b>{{ cell.point.sentimentScore.toFixed(1) }}</b></div>
            <div>涨 {{ cell.point.upCount }} · 跌 {{ cell.point.downCount }} · 平 {{ cell.point.flatCount }}</div>
            <div>涨停 {{ cell.point.limitUpCount }} · 跌停 {{ cell.point.limitDownCount }} · 炸板 {{ cell.point.brokenBoardCount }}</div>
            <div>最高 {{ cell.point.maxBoardStreak }} 连板 · 成交 {{ (cell.point.totalAmount / 10000).toFixed(2) }} 万亿</div>
            <div>全市场均涨 {{ fmtPct(cell.point.avgChangePct) }}</div>
          </template>
          <div v-else>{{ cell.key }} 暂无情绪数据</div>
        </template>

        <div
          class="cell"
          :class="{ empty: !cell.key, today: cell.key === todayKey, has: !!cell.point }"
          :style="cell.point
            ? { background: tone(cell.point.sentimentScore).bg, borderLeftColor: tone(cell.point.sentimentScore).color }
            : undefined"
        >
          <span v-if="cell.key" class="day">{{ cell.day }}</span>
          <span
            v-if="cell.point"
            class="score"
            :style="{ color: tone(cell.point.sentimentScore).color }"
          >{{ Math.round(cell.point.sentimentScore) }}</span>
        </div>
      </el-tooltip>
    </div>
  </div>
</template>

<style scoped>
.calendar-head {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 12px;
}
.month-label { font-size: 15px; font-weight: 600; color: #303133; }
.month-summary { color: #909399; font-size: 12px; }

.legend { display: flex; align-items: center; gap: 10px; }
.legend-item { display: inline-flex; align-items: center; gap: 4px; color: #909399; font-size: 12px; }
.legend-item i { width: 9px; height: 9px; border-radius: 2px; display: inline-block; }

.week-head,
.calendar-grid {
  display: grid;
  grid-template-columns: repeat(7, minmax(0, 1fr));
  gap: 6px;
}
.week-head { margin-bottom: 6px; }
.week-head span { text-align: center; color: #909399; font-size: 12px; }

.cell {
  min-height: 58px;
  padding: 4px 6px;
  border: 1px solid #ebeef5;
  border-left: 3px solid transparent;
  border-radius: 6px;
  background: #fafafa;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  box-sizing: border-box;
}
.cell.empty { background: transparent; border-color: transparent; }
.cell.has { cursor: default; }
.cell.has:hover { filter: brightness(0.97); }
.cell.today { box-shadow: inset 0 0 0 1px #409eff; }

.day { color: #909399; font-size: 12px; line-height: 1; }
.score { font-size: 18px; font-weight: 700; text-align: center; line-height: 1.1; }

.tip-title { font-weight: 600; margin-bottom: 4px; }

@media (max-width: 900px) {
  .cell { min-height: 46px; }
  .score { font-size: 15px; }
  .legend { display: none; }
}
</style>
