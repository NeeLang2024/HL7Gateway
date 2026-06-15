<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { fetchMessage, fetchMessageRaw, reparseMessage, validateMessage } from '../api'
import { formatDateTime } from '../utils/time'

const route = useRoute()
const router = useRouter()
const messageId = route.params.id as string

const loading = ref(true)
const reparseLoading = ref(false)
const validationLoading = ref(false)
const raw = ref('')
const message = ref<any>(null)
const segments = ref<any[]>([])
const observations = ref<any[]>([])
const vitalSigns = ref<any[]>([])
const validationIssues = ref<any[]>([])
const validationResult = ref<any>(null)
const activeTab = ref<'raw' | 'segments' | 'observations' | 'vitals' | 'validation'>('raw')

onMounted(async () => {
  try {
    const [msgData, rawData] = await Promise.all([
      fetchMessage(messageId),
      fetchMessageRaw(messageId).catch(() => ''),
    ])
    message.value = msgData.message ?? msgData
    raw.value = typeof rawData === 'string' ? rawData : rawData?.raw || JSON.stringify(rawData, null, 2) || ''
    if (msgData.segments) segments.value = msgData.segments
    if (msgData.observations) observations.value = msgData.observations
    if (msgData.vitalSigns) vitalSigns.value = msgData.vitalSigns
  } catch (err) {
    console.error(err)
  } finally {
    loading.value = false
  }
})

async function doReparse() {
  if (!confirm('确定要重置此消息为待解析状态吗？')) return
  reparseLoading.value = true
  try {
    await reparseMessage(messageId)
    alert('已重置，请刷新页面查看状态')
    router.go(0)
  } catch (err: any) {
    alert('重解析失败: ' + (err?.message || '未知错误'))
  } finally {
    reparseLoading.value = false
  }
}

async function doValidate() {
  validationLoading.value = true
  activeTab.value = 'validation'
  try {
    const res = await validateMessage(messageId)
    validationResult.value = res
    validationIssues.value = res.issues || []
  } catch (err: any) {
    validationIssues.value = []
    validationResult.value = null
    alert('验证失败: ' + (err?.message || '未知错误'))
  } finally {
    validationLoading.value = false
  }
}

function goTrends() {
  if (message.value?.patientId) {
    router.push(`/trends?patientId=${encodeURIComponent(message.value.patientId)}&type=HR`)
  }
}
</script>

<template>
  <div class="detail-page">
    <div v-if="loading" class="loading">加载中...</div>
    <template v-else>
      <div class="page-header">
        <div class="header-row">
          <h2>消息详情</h2>
          <div class="header-actions">
            <button
              v-if="message?.parseStatus === 2"
              class="btn btn-warning"
              :disabled="reparseLoading"
              @click="doReparse"
            >
              {{ reparseLoading ? '重置中...' : '重解析' }}
            </button>
            <button class="btn btn-info" @click="doValidate" :disabled="validationLoading">
              {{ validationLoading ? '验证中...' : '验证' }}
            </button>
            <button
              v-if="message?.patientId"
              class="btn btn-secondary"
              @click="goTrends"
            >趋势图</button>
          </div>
        </div>
        <div class="msg-meta" v-if="message">
          <span>控制 ID: {{ message.messageControlId }}</span>
          <span>类型: {{ message.messageType }}</span>
          <span>患者: {{ message.patientId }}</span>
          <span>来源: {{ message.sourceIp }}</span>
          <span>时间: {{ formatDateTime(message.receivedAt) }}</span>
          <span :class="['badge', message.parseStatus === 1 ? 'badge-success' : message.parseStatus === 2 ? 'badge-error' : 'badge-warning']">
            {{ message.parseStatus === 0 ? '待解析' : message.parseStatus === 1 ? '已解析' : '解析失败' }}
          </span>
          <span v-if="message.errorMessage" class="error-msg-text" :title="message.errorMessage">错误: {{ message.errorMessage }}</span>
        </div>
      </div>

      <div class="tabs">
        <button :class="['tab', { active: activeTab === 'raw' }]" @click="activeTab = 'raw'">原始内容</button>
        <button :class="['tab', { active: activeTab === 'segments' }]" @click="activeTab = 'segments'">段 ({{ segments.length }})</button>
        <button :class="['tab', { active: activeTab === 'observations' }]" @click="activeTab = 'observations'">观察值 ({{ observations.length }})</button>
        <button :class="['tab', { active: activeTab === 'vitals' }]" @click="activeTab = 'vitals'">生命体征 ({{ vitalSigns.length }})</button>
        <button :class="['tab', { active: activeTab === 'validation' }]" @click="activeTab = 'validation'">
          验证 <span v-if="validationResult" :class="validationResult.valid ? 'tab-badge-ok' : 'tab-badge-err'">{{ validationResult.totalIssues }}</span>
        </button>
      </div>

      <div class="tab-content">
        <div v-if="activeTab === 'raw'">
          <pre class="raw-content">{{ raw }}</pre>
        </div>

        <div v-if="activeTab === 'segments'">
          <table v-if="segments.length">
            <thead>
              <tr><th>序号</th><th>段 ID</th><th>内容</th></tr>
            </thead>
            <tbody>
              <tr v-for="(seg, i) in segments" :key="i">
                <td>{{ i + 1 }}</td>
                <td>{{ seg.segmentType || '-' }}</td>
                <td class="seg-value">{{ seg.segmentRaw || '-' }}</td>
              </tr>
            </tbody>
          </table>
          <div v-else class="empty">暂无段数据</div>
        </div>

        <div v-if="activeTab === 'observations'">
          <table v-if="observations.length">
            <thead>
              <tr><th>编号</th><th>名称</th><th>值</th><th>单位</th><th>参考范围</th><th>异常标志</th></tr>
            </thead>
            <tbody>
              <tr v-for="(obs, n) in observations" :key="obs.observationId ?? n">
                <td>{{ obs.observationId ?? n + 1 }}</td>
                <td>{{ obs.identifierText || obs.identifierCode || '-' }}</td>
                <td>{{ obs.observationValue || '-' }}</td>
                <td>{{ obs.units || '-' }}</td>
                <td>{{ obs.referenceRange || '-' }}</td>
                <td>
                  <span v-if="obs.abnormalFlags" :class="['badge', obs.abnormalFlags === 'L' || obs.abnormalFlags === 'H' ? 'badge-warning' : obs.abnormalFlags === 'LL' || obs.abnormalFlags === 'HH' ? 'badge-error' : 'badge-gray']">{{ obs.abnormalFlags }}</span>
                  <span v-else class="text-muted">-</span>
                </td>
              </tr>
            </tbody>
          </table>
          <div v-else class="empty">暂无观察值</div>
        </div>

        <div v-if="activeTab === 'vitals'">
          <table v-if="vitalSigns.length">
            <thead>
              <tr><th>类型</th><th>值</th><th>单位</th><th>时间</th><th>异常</th></tr>
            </thead>
            <tbody>
              <tr v-for="(v, i) in vitalSigns" :key="v.vitalSignId ?? i">
                <td>{{ v.vitalSignName || v.vitalSignType || '-' }}</td>
                <td :class="{ 'abnormal-value': v.abnormalFlags }">{{ v.valueNumeric ?? v.valueString ?? '-' }}</td>
                <td>{{ v.units || '-' }}</td>
                <td>{{ formatDateTime(v.observationDateTime) }}</td>
                <td>
                  <span v-if="v.abnormalFlags" class="badge badge-error">{{ v.abnormalFlags }}</span>
                  <span v-else class="text-muted">-</span>
                </td>
              </tr>
            </tbody>
          </table>
          <div v-else class="empty">暂无生命体征数据</div>
        </div>

        <div v-if="activeTab === 'validation'">
          <div v-if="validationLoading" class="loading">验证中...</div>
          <div v-else-if="!validationResult" class="empty">点击"验证"按钮开始验证</div>
          <template v-else>
            <div class="validation-summary">
              <span :class="validationResult.valid ? 'summary-ok' : 'summary-err'">
                {{ validationResult.valid ? '通过' : '发现问题' }}
              </span>
              <span class="summary-count">错误: {{ validationResult.errors }}</span>
              <span class="summary-count">警告: {{ validationResult.warnings }}</span>
              <span class="summary-count">共 {{ validationResult.totalIssues }} 项</span>
            </div>
            <table v-if="validationIssues.length">
              <thead>
                <tr><th>严重程度</th><th>代码</th><th>描述</th></tr>
              </thead>
              <tbody>
                <tr v-for="(issue, i) in validationIssues" :key="i">
                  <td>
                    <span :class="['badge', issue.severity === 'error' ? 'badge-error' : issue.severity === 'warning' ? 'badge-warning' : 'badge-gray']">{{ issue.severity }}</span>
                  </td>
                  <td>{{ issue.code }}</td>
                  <td>{{ issue.message }}</td>
                </tr>
              </tbody>
            </table>
          </template>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.detail-page { max-width: 1200px; }
.page-header { margin-bottom: 20px; }
.header-row { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
.header-row h2 { font-size: 22px; margin: 0; }
.header-actions { display: flex; gap: 8px; }
.msg-meta {
  display: flex; flex-wrap: wrap; gap: 16px; font-size: 13px; color: var(--text-secondary);
  background: var(--card-bg); padding: 12px 16px; border-radius: var(--radius); box-shadow: var(--shadow);
}
.error-msg-text { color: var(--error); font-weight: 500; max-width: 400px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.tabs {
  display: flex; gap: 2px; margin-bottom: 0;
  background: var(--card-bg); border-radius: var(--radius) var(--radius) 0 0; box-shadow: var(--shadow); overflow: hidden;
}
.tab {
  padding: 10px 20px; border: none; background: transparent; font-size: 13px; cursor: pointer;
  color: var(--text-secondary); border-bottom: 2px solid transparent; transition: all 0.2s;
}
.tab:hover { color: var(--text-primary); background: #f9fafb; }
.tab.active { color: var(--accent); border-bottom-color: var(--accent); font-weight: 500; }
.tab-badge-ok, .tab-badge-err {
  display: inline-block; padding: 1px 7px; border-radius: 10px; font-size: 11px; margin-left: 4px;
}
.tab-badge-ok { background: var(--success-bg); color: #065f46; }
.tab-badge-err { background: var(--error-bg); color: #991b1b; }
.tab-content {
  background: var(--card-bg); border-radius: 0 0 var(--radius) var(--radius);
  box-shadow: var(--shadow); padding: 20px; overflow-x: auto;
}
.raw-content {
  background: #1f2937; color: #e5e7eb; padding: 16px; border-radius: var(--radius-sm);
  font-size: 12px; line-height: 1.6; overflow-x: auto; white-space: pre-wrap;
  word-break: break-all; max-height: 600px; overflow-y: auto;
}
table { width: 100%; border-collapse: collapse; }
th {
  text-align: left; padding: 10px 12px; font-size: 12px; font-weight: 600;
  color: var(--text-secondary); background: #f9fafb; border-bottom: 1px solid var(--border-color); white-space: nowrap;
}
td { padding: 8px 12px; font-size: 13px; border-bottom: 1px solid var(--border-color); }
tbody tr:nth-child(even) { background: #f9fafb; }
.seg-value { font-family: var(--font-mono); font-size: 12px; max-width: 600px; overflow-x: auto; white-space: pre-wrap; word-break: break-all; }
.badge { display: inline-block; padding: 2px 8px; border-radius: 10px; font-size: 11px; font-weight: 500; }
.badge-success { background: var(--success-bg); color: #065f46; }
.badge-warning { background: var(--warning-bg); color: #92400e; }
.badge-error { background: var(--error-bg); color: #991b1b; }
.badge-gray { background: var(--gray-bg); color: #4b5563; }
.abnormal-value { color: var(--error); font-weight: 600; }
.text-muted { color: var(--text-muted); }
.loading { text-align: center; padding: 60px; color: var(--text-muted); }
.empty { text-align: center; padding: 40px; color: var(--text-muted); }
.validation-summary {
  display: flex; gap: 16px; align-items: center; padding: 12px 16px;
  background: #f9fafb; border-radius: var(--radius-sm); margin-bottom: 16px; font-size: 13px;
}
.summary-ok { color: #065f46; font-weight: 600; }
.summary-err { color: #991b1b; font-weight: 600; }
.summary-count { color: var(--text-secondary); }
.btn {
  padding: 6px 14px; border: none; border-radius: var(--radius-sm);
  font-size: 13px; font-weight: 500; cursor: pointer; transition: background 0.2s;
}
.btn:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-warning { background: var(--warning-bg); color: #92400e; }
.btn-warning:hover:not(:disabled) { background: #fde68a; }
.btn-info { background: #dbeafe; color: #1e40af; }
.btn-info:hover:not(:disabled) { background: #bfdbfe; }
.btn-secondary { background: var(--gray-bg); color: var(--text-primary); }
.btn-secondary:hover:not(:disabled) { background: #e5e7eb; }
</style>
