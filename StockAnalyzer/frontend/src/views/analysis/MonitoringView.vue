<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { MonitorJobSummary, RuleGroup, WebhookType } from '../../types/analysis'
import {
  analysisErrorMessage,
  createMonitorJob,
  deleteMonitorJob,
  listMonitorJobs,
  testAnalysisWebhook,
  triggerMonitorJob,
} from '../../api/analysis'
import CollapsibleSidebar from '../../components/analysis/CollapsibleSidebar.vue'
import RuleBuilder from '../../components/analysis/RuleBuilder.vue'

const DEFAULT_ENTRY_RULES: RuleGroup = {
  logic: 'AND',
  conditions: [
    { indicator: 'RSI', operator: '<', value: 25, params: { period: 14 } },
    { indicator: 'OBV', operator: 'cross_above', value: 0, params: { maPeriod: 20 } },
  ],
}

const CRON_PRESETS = [
  { label: '每交易日15:00', value: '0 15 * * 1-5' },
  { label: '每交易日09:30', value: '30 9 * * 1-5' },
  { label: '每日09:00', value: '0 9 * * *' },
  { label: '每2小时', value: '0 */2 * * *' },
  { label: '每30分钟', value: '*/30 * * * *' },
  { label: '每2分钟(测试)', value: '*/2 * * * *' },
]

const jobs = ref<MonitorJobSummary[]>([])
const jobsLoading = ref(false)
const editing = ref(false)

const name = ref('')
const cron = ref('0 15 * * 1-5')
const stockPool = ref('')
const watchlist = ref('')
const entryRules = ref<RuleGroup>(DEFAULT_ENTRY_RULES)
const stopLossPct = ref(5)
const takeProfitPct = ref(15)
const webhookUrl = ref('')
const webhookType = ref<WebhookType>('wecom')

const loading = ref(false)
const testing = ref(false)
const error = ref('')

const splitCodes = (s: string) =>
  s.split(',').map((x) => x.trim()).filter(Boolean)

async function loadJobs() {
  jobsLoading.value = true
  try {
    jobs.value = await listMonitorJobs()
  } catch {
    /* 分析服务不可用时保持空列表 */
  } finally {
    jobsLoading.value = false
  }
}

onMounted(loadJobs)

function openCreate() {
  editing.value = true
  error.value = ''
}

async function create() {
  if (!name.value) return
  loading.value = true
  error.value = ''
  try {
    await createMonitorJob({
      id: '',
      name: name.value,
      cron: cron.value,
      stockPool: splitCodes(stockPool.value),
      entryRules: entryRules.value,
      exitRules: { stopLossPct: stopLossPct.value, takeProfitPct: takeProfitPct.value },
      watchlist: splitCodes(watchlist.value),
      webhooks: webhookUrl.value ? [{ type: webhookType.value, url: webhookUrl.value }] : [],
      enabled: true,
    })
    ElMessage.success('任务创建成功')
    name.value = ''
    editing.value = false
    await loadJobs()
  } catch (e) {
    error.value = analysisErrorMessage(e)
  } finally {
    loading.value = false
  }
}

async function remove(job: MonitorJobSummary) {
  try {
    await ElMessageBox.confirm(`确定删除监控任务「${job.name}」？`, '删除监控任务', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消',
    })
  } catch {
    return
  }
  try {
    await deleteMonitorJob(job.id)
    ElMessage.success('已删除')
    await loadJobs()
  } catch (e) {
    ElMessage.error(analysisErrorMessage(e))
  }
}

async function trigger(job: MonitorJobSummary) {
  try {
    await triggerMonitorJob(job.id)
    ElMessage.success('手动执行已触发')
  } catch (e) {
    ElMessage.error(analysisErrorMessage(e))
  }
}

async function testWebhook() {
  if (!webhookUrl.value) return
  testing.value = true
  try {
    await testAnalysisWebhook(webhookUrl.value, webhookType.value)
    ElMessage.success('测试消息已发送')
  } catch (e) {
    ElMessage.error(analysisErrorMessage(e))
  } finally {
    testing.value = false
  }
}
</script>

<template>
  <div class="qa-page">
    <CollapsibleSidebar :width="280">
      <div class="job-head">
        <h3 class="side-title">监控任务</h3>
        <el-button type="primary" size="small" @click="openCreate">新建</el-button>
      </div>

      <div v-loading="jobsLoading" class="job-list">
        <div v-if="!jobsLoading && !jobs.length" class="qa-muted">暂无任务</div>
        <div v-for="job in jobs" :key="job.id" class="job-item">
          <div class="job-row">
            <span class="job-name">{{ job.name }}</span>
            <el-button link type="danger" size="small" @click="remove(job)">删除</el-button>
          </div>
          <div class="job-cron">{{ job.cron }}</div>
          <div class="job-actions">
            <el-tag :type="job.enabled ? 'success' : 'info'" size="small">{{ job.enabled ? '启用' : '停用' }}</el-tag>
            <el-button link type="primary" size="small" @click="trigger(job)">立即执行</el-button>
          </div>
        </div>
      </div>
    </CollapsibleSidebar>

    <div class="qa-main">
      <div v-if="!editing" class="qa-card qa-empty">
        点击「新建」创建监控任务，定时扫描符合条件的股票并通过 Webhook 推送通知
      </div>

      <div v-else class="qa-card job-form">
        <div class="qa-card-head"><h4>创建监控任务</h4></div>

        <div class="qa-field">
          <span class="qa-label">任务名称</span>
          <el-input v-model="name" size="small" placeholder="如：每日超跌扫描" style="max-width: 420px" />
        </div>

        <div class="qa-field">
          <span class="qa-label">执行周期</span>
          <div class="cron-presets">
            <el-button
              v-for="p in CRON_PRESETS"
              :key="p.value"
              size="small"
              round
              :type="cron === p.value ? 'primary' : 'default'"
              @click="cron = p.value"
            >
              {{ p.label }}
            </el-button>
          </div>
          <el-input v-model="cron" size="small" class="cron-input" placeholder="Cron 表达式" />
        </div>

        <div class="qa-grid-2 form-grid">
          <div class="qa-field">
            <span class="qa-label">股票池（逗号分隔，留空 = 全 A 股）</span>
            <el-input v-model="stockPool" size="small" placeholder="000001,600519" />
          </div>
          <div class="qa-field">
            <span class="qa-label">观察列表（逗号分隔）</span>
            <el-input v-model="watchlist" size="small" placeholder="000001,600519" />
          </div>
        </div>

        <div class="qa-grid-2 form-grid">
          <div class="qa-field">
            <span class="qa-label">止损 %</span>
            <el-input-number v-model="stopLossPct" size="small" :min="0" controls-position="right" style="width: 100%" />
          </div>
          <div class="qa-field">
            <span class="qa-label">止盈 %</span>
            <el-input-number v-model="takeProfitPct" size="small" :min="0" controls-position="right" style="width: 100%" />
          </div>
        </div>

        <div class="qa-field">
          <span class="qa-label">入场规则</span>
          <RuleBuilder v-model="entryRules" />
        </div>

        <div class="qa-field">
          <span class="qa-label">Webhook 配置</span>
          <div class="webhook-row">
            <el-select v-model="webhookType" size="small" style="width: 120px">
              <el-option label="企业微信" value="wecom" />
              <el-option label="钉钉" value="dingtalk" />
              <el-option label="通用" value="generic" />
            </el-select>
            <el-button size="small" :loading="testing" :disabled="!webhookUrl" @click="testWebhook">测试</el-button>
          </div>
          <el-input v-model="webhookUrl" size="small" placeholder="Webhook URL" />
        </div>

        <div class="form-actions">
          <el-button type="primary" :loading="loading" :disabled="!name" @click="create">
            {{ loading ? '创建中...' : '创建任务' }}
          </el-button>
          <el-button @click="editing = false">取消</el-button>
        </div>

        <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
      </div>
    </div>
  </div>
</template>

<style scoped>
.side-title { font-size: 15px; color: #303133; }
.job-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
.job-list { display: flex; flex-direction: column; gap: 8px; }
.job-item { border: 1px solid #ebeef5; border-radius: 6px; padding: 10px; background: #fafbfc; }
.job-row { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
.job-name { font-size: 13px; font-weight: 600; color: #303133; }
.job-cron { font-size: 12px; color: #909399; margin-top: 4px; font-family: 'SFMono-Regular', Consolas, monospace; }
.job-actions { display: flex; align-items: center; justify-content: space-between; margin-top: 8px; }
.job-form { max-width: 720px; display: flex; flex-direction: column; gap: 14px; }
.cron-presets { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 8px; }
.cron-input { max-width: 260px; font-family: 'SFMono-Regular', Consolas, monospace; }
.form-grid { gap: 16px; }
.webhook-row { display: flex; gap: 8px; margin-bottom: 8px; }
.form-actions { display: flex; gap: 8px; }
</style>