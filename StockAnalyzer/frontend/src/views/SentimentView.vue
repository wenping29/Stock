<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { SentimentOverview, SentimentHistory, SentimentStructure } from '../types'
import { fetchSentimentOverview, fetchSentimentHistory, fetchSentimentStructure } from '../api/sentiment'
import RefreshButton from '../components/RefreshButton.vue'
import SentimentGauge from '../components/SentimentGauge.vue'
import SentimentTrendChart from '../components/SentimentTrendChart.vue'
import SentimentCalendar from '../components/SentimentCalendar.vue'

const overview = ref<SentimentOverview | null>(null)
const history = ref<SentimentHistory | null>(null)
const structure = ref<SentimentStructure | null>(null)
const loading = ref(false)
const historyDays = ref(60)
const industryTab = ref<'top' | 'bottom'>('top')

const latest = computed(() => overview.value?.latest ?? null)
// 一次性取一年历史，趋势图按所选区间在本地截取，日历直接复用全量点
const allPoints = computed(() => history.value?.points ?? [])
const points = computed(() => {
  const since = shiftDateKey(historyDays.value)
  return allPoints.value.filter((p) => p.date >= since)
})

function shiftDateKey(days: number) {
  const d = new Date()
  d.setDate(d.getDate() - days)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

async function load() {
  loading.value = true
  try {
    const [ov, hi, st] = await Promise.all([
      fetchSentimentOverview().catch(() => null),
      fetchSentimentHistory(365).catch(() => null),
      fetchSentimentStructure().catch(() => null),
    ])
    overview.value = ov
    history.value = hi
    structure.value = st
  } finally {
    loading.value = false
  }
}

// ---------- 概览派生指标 ----------
const upRatio = computed(() => {
  const s = latest.value
  if (!s) return 0
  const total = s.upCount + s.downCount + s.flatCount
  return total > 0 ? (s.upCount / total) * 100 : 0
})

const sealRate = computed(() => {
  const s = latest.value
  if (!s) return 0
  const total = s.limitUpCount + s.brokenBoardCount
  return total > 0 ? (s.limitUpCount / total) * 100 : 0
})

const industries = computed(() => {
  if (!structure.value) return []
  return industryTab.value === 'top'
    ? structure.value.topIndustries
    : structure.value.bottomIndustries
})

const maxIndustryAbs = computed(() =>
  Math.max(0.1, ...(industries.value.map((i) => Math.abs(i.avgChangePct)) ?? [])))

const distMax = computed(() =>
  Math.max(1, ...(structure.value?.distribution.map((b) => b.count) ?? [])))

// ---------- 展示辅助 ----------
function pctClass(v: number | null | undefined) {
  return v == null ? '' : v >= 0 ? 'up' : 'down'
}
function fmtPct(v: number | null | undefined, digits = 2) {
  return v == null ? '—' : `${v >= 0 ? '+' : ''}${v.toFixed(digits)}%`
}
function fmtChange(v: number | null | undefined, digits = 1) {
  return v == null ? '—' : `${v >= 0 ? '+' : ''}${v.toFixed(digits)}`
}
function barWidth(v: number) {
  return Math.max(3, Math.abs(v) / maxIndustryAbs.value * 100)
}
function bucketClass(label: string) {
  if (label === '平盘') return 'bg-flat'
  return label.startsWith('-') || label.startsWith('≤') ? 'bg-down' : 'bg-up'
}
function levelTagType(level: string) {
  return level === '亢奋' ? 'danger'
    : level === '活跃' ? 'warning'
    : level === '低迷' ? 'success'
    : level === '冰点' ? 'info' : 'primary'
}

onMounted(load)
</script>

<template>
  <div v-loading="loading">
    <div class="page-card head-card">
      <div class="title-row">
        <h3>🌡️ 市场情绪</h3>
        <span class="sub">涨跌家数 · 涨停跌停 · 连板高度 · 量能 · 行业冷暖</span>
        <div style="flex: 1" />
        <RefreshButton category="sentiment" label="同步最新" @done="load" />
        <el-button size="small" :loading="loading" @click="load">刷新</el-button>
      </div>
    </div>

    <template v-if="latest">
      <!-- 情绪温度 + 核心指标 -->
      <div class="sentiment-top">
        <div class="page-card gauge-card">
          <SentimentGauge :score="latest.sentimentScore" :level="latest.level" />
          <div class="gauge-meta">
            <el-tag :type="levelTagType(latest.level)" effect="dark" round>{{ latest.level }}</el-tag>
            <span class="gauge-date">{{ latest.date }}</span>
          </div>
          <div class="gauge-advice">{{ latest.advice }}</div>
          <div class="gauge-delta" v-if="latest.scoreChange != null">
            较上日 <b :class="pctClass(latest.scoreChange)">{{ fmtChange(latest.scoreChange) }}</b> 分
          </div>
          <div class="gauge-delta dim" v-else>暂无上一交易日对比</div>
        </div>

        <div class="stat-cards sentiment-cards">
          <div class="stat-card">
            <div class="label">涨跌家数</div>
            <div class="value">
              <span class="up">{{ latest.upCount }}</span>
              <span class="sep">/</span>
              <span class="down">{{ latest.downCount }}</span>
            </div>
            <div class="sub">平盘 {{ latest.flatCount }} 家 · 上涨占比 {{ upRatio.toFixed(1) }}%</div>
          </div>
          <div class="stat-card">
            <div class="label">涨停 / 跌停</div>
            <div class="value">
              <span class="up">{{ latest.limitUpCount }}</span>
              <span class="sep">/</span>
              <span class="down">{{ latest.limitDownCount }}</span>
            </div>
            <div class="sub">炸板 {{ latest.brokenBoardCount }} 家 · 封板率 {{ sealRate.toFixed(1) }}%</div>
          </div>
          <div class="stat-card">
            <div class="label">最高连板</div>
            <div class="value">{{ latest.maxBoardStreak }}<span class="unit">板</span></div>
            <div class="sub">涨停梯队高度</div>
          </div>
          <div class="stat-card">
            <div class="label">两市成交额</div>
            <div class="value">{{ (latest.totalAmount / 10000).toFixed(2) }}<span class="unit">万亿</span></div>
            <div class="sub" :class="pctClass(latest.amountChangePct)">
              较上日 {{ fmtPct(latest.amountChangePct) }}
            </div>
          </div>
          <div class="stat-card">
            <div class="label">全市场平均涨幅</div>
            <div class="value" :class="pctClass(latest.avgChangePct)">{{ fmtPct(latest.avgChangePct) }}</div>
            <div class="sub">中位数 {{ fmtPct(latest.medianChangePct) }}</div>
          </div>
          <div class="stat-card">
            <div class="label">极端涨跌（±5%）</div>
            <div class="value">
              <span class="up">{{ latest.up5Count }}</span>
              <span class="sep">/</span>
              <span class="down">{{ latest.down5Count }}</span>
            </div>
            <div class="sub">涨超5% / 跌超5%</div>
          </div>
        </div>
      </div>

      <!-- 情绪历史 -->
      <div class="page-card">
        <div class="chart-toolbar">
          <span class="chart-title">情绪温度与涨跌家数趋势 —— 柱形向上为上涨家数、向下为下跌家数，蓝线为 0-100 情绪分</span>
          <div style="flex: 1" />
          <el-radio-group v-model="historyDays" size="small">
            <el-radio-button :value="30">近1月</el-radio-button>
            <el-radio-button :value="60">近2月</el-radio-button>
            <el-radio-button :value="90">近3月</el-radio-button>
            <el-radio-button :value="180">近半年</el-radio-button>
          </el-radio-group>
        </div>
        <SentimentTrendChart v-if="points.length" :points="points" />
        <el-empty v-else description="情绪历史将从今天开始逐日积累，盘中刷新即可看到当日快照" :image-size="60" />
      </div>

      <!-- 情绪日历 -->
      <div class="page-card">
        <div class="card-head">
          <h4>情绪日历</h4>
          <span class="dim">按交易日显示情绪温度分，颜色越暖代表情绪越高</span>
        </div>
        <SentimentCalendar :points="allPoints" />
      </div>

      <!-- 行业冷暖 + 涨跌分布 -->
      <div class="sentiment-grid" v-if="structure">
        <div class="page-card">
          <div class="card-head">
            <h4>行业冷暖</h4>
            <span class="dim" v-if="structure.basisDate">基于 {{ structure.basisDate }} 收盘快照</span>
            <div style="flex: 1" />
            <el-radio-group v-model="industryTab" size="small">
              <el-radio-button value="top">领涨</el-radio-button>
              <el-radio-button value="bottom">领跌</el-radio-button>
            </el-radio-group>
          </div>
          <div class="industry-list" v-if="industries.length">
            <div v-for="(item, i) in industries" :key="item.industry" class="industry-row">
              <span class="rank">{{ i + 1 }}</span>
              <span class="industry-name">{{ item.industry }}</span>
              <span class="industry-count">{{ item.count }}只</span>
              <div class="industry-bar">
                <div class="industry-fill" :class="item.avgChangePct >= 0 ? 'bg-up' : 'bg-down'"
                  :style="{ width: `${barWidth(item.avgChangePct)}%` }" />
              </div>
              <span class="industry-pct" :class="pctClass(item.avgChangePct)">{{ fmtPct(item.avgChangePct) }}</span>
              <span class="industry-sub">涨{{ item.upRatio.toFixed(0) }}% · 涨停{{ item.limitUpCount }}</span>
            </div>
          </div>
          <el-empty v-else description="行业数据尚未采集（等待收盘基本面快照）" :image-size="60" />
        </div>

        <div class="page-card">
          <div class="card-head">
            <h4>全市场涨跌分布</h4>
            <span class="dim">共 {{ structure.totalStocks }} 只</span>
          </div>
          <div class="dist-list">
            <div v-for="bucket in structure.distribution" :key="bucket.label" class="dist-row">
              <span class="dist-label">{{ bucket.label }}</span>
              <div class="dist-bar">
                <div class="dist-fill" :class="bucketClass(bucket.label)"
                  :style="{ width: `${(bucket.count / distMax) * 100}%` }" />
              </div>
              <span class="dist-count">{{ bucket.count }}</span>
            </div>
          </div>
        </div>
      </div>

      <div class="page-note">
        口径：涨跌家数与成交额为沪深两市实时统计（盘中每 10 分钟更新）；涨停 / 跌停 / 炸板 / 连板来自东方财富涨停板行情池；
        涨跌分布与行业冷暖基于最近一次收盘基本面快照<span v-if="latest.basisDate">（{{ latest.basisDate }}）</span>。
        情绪分 = 上涨占比 35% + 涨停跌停对比 25% + 连板高度 15% + 封板率 10% + 成交额相对 20 日均值 15%。
      </div>
    </template>

    <div v-else-if="!loading" class="page-card">
      <el-empty description="暂无市场情绪数据，点击下方按钮采集当日快照" :image-size="70">
        <RefreshButton category="sentiment" label="立即采集" @done="load" />
      </el-empty>
    </div>
  </div>
</template>

<style scoped>
.head-card .title-row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
.head-card h3 { font-size: 17px; }
.sub { color: #909399; font-size: 13px; }

.sentiment-top { display: flex; gap: 12px; align-items: stretch; margin-bottom: 16px; }
.gauge-card {
  width: 260px;
  flex: none;
  margin-bottom: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
}
.gauge-meta { display: flex; align-items: center; gap: 8px; margin-top: 2px; }
.gauge-date { color: #909399; font-size: 13px; }
.gauge-advice { margin-top: 10px; color: #606266; font-size: 13px; text-align: center; line-height: 1.5; }
.gauge-delta { margin-top: 8px; font-size: 13px; color: #909399; }
.gauge-delta b { font-size: 15px; }

.sentiment-cards { flex: 1; align-content: flex-start; }
.sentiment-cards .stat-card { min-width: 150px; }
.sentiment-cards .value { display: flex; align-items: baseline; gap: 4px; }
.sep { color: #c0c4cc; font-weight: 400; font-size: 16px; }
.unit { font-size: 13px; color: #909399; margin-left: 2px; font-weight: 400; }

.chart-toolbar { display: flex; align-items: center; gap: 12px; margin-bottom: 10px; flex-wrap: wrap; }
.chart-title { color: #606266; font-size: 13px; }

.sentiment-grid { display: grid; grid-template-columns: minmax(0, 1.15fr) minmax(0, 1fr); gap: 16px; }
.sentiment-grid .page-card { margin-bottom: 16px; }
.card-head { display: flex; align-items: center; gap: 8px; margin-bottom: 12px; flex-wrap: wrap; }
.card-head h4 { font-size: 15px; }
.dim { color: #909399; font-size: 12px; }

.industry-list { display: flex; flex-direction: column; gap: 8px; }
.industry-row { display: flex; align-items: center; gap: 8px; font-size: 13px; }
.rank { width: 18px; color: #909399; font-size: 12px; text-align: right; }
.industry-name { width: 96px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.industry-count { width: 46px; color: #909399; font-size: 12px; }
.industry-bar { flex: 1; height: 8px; background: #f2f3f5; border-radius: 4px; overflow: hidden; }
.industry-fill { height: 100%; border-radius: 4px; }
.industry-pct { width: 62px; text-align: right; font-weight: 600; }
.industry-sub { width: 118px; color: #909399; font-size: 12px; text-align: right; }

.bg-up { background: #ef232a; }
.bg-down { background: #14b143; }
.bg-flat { background: #c0c4cc; }

.dist-list { display: flex; flex-direction: column; gap: 7px; }
.dist-row { display: flex; align-items: center; gap: 10px; font-size: 13px; }
.dist-label { width: 82px; color: #606266; font-size: 12px; }
.dist-bar { flex: 1; height: 8px; background: #f2f3f5; border-radius: 4px; overflow: hidden; }
.dist-fill { height: 100%; border-radius: 4px; }
.dist-count { width: 48px; text-align: right; color: #606266; font-size: 12px; }

.page-note {
  color: #909399;
  font-size: 12px;
  line-height: 1.7;
  padding: 0 4px 16px;
}

@media (max-width: 900px) {
  .sentiment-top { flex-direction: column; }
  .gauge-card { width: 100%; }
  .sentiment-grid { grid-template-columns: 1fr; gap: 0; }
}
@media (max-width: 600px) {
  .industry-sub { display: none; }
  .industry-name { width: 76px; }
}
</style>
