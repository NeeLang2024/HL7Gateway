<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { fetchAdtQueue, fetchAdtLogs, fetchAdtBridgeStatus, fetchAdtBridgeLogs, sendAdtMessage, composeAdtMessage, deleteAdtQueueItem, retryAdtQueueItem } from '../api'
import { signalrService, signalrConnected } from '../services/signalr'
import { formatDateTime } from '../utils/time'

const activeTab = ref<'queue' | 'logs' | 'send'>('queue')

// Philips HIF bridge
const bridgeStatus = ref<any>(null)
const bridgeLoading = ref(false)
const bridgeLogItems = ref<any[]>([])
const bridgeLogError = ref('')
const bridgeLogPaused = ref(false)
const bridgeLogLastId = ref(0)
let bridgeTimer: number | undefined
let bridgeLogTimer: number | undefined

// Queue
const queueItems = ref<any[]>([])
const queueLoading = ref(false)
const queueError = ref('')

// Logs
const logItems = ref<any[]>([])
const logPage = ref(1)
const logPageSize = ref(20)
const logLoading = ref(false)
const logError = ref('')

// Live
const notification = ref('')
let notifTimer: number | undefined

// Send - Raw
const sendTargetHost = ref('')
const sendTargetPort = ref('')
const sendAdtType = ref('ADT^A01')
const sendMessageContent = ref('')
const messageComposed = ref(false)
const building = ref(false)
const sending = ref(false)
const sendResult = ref('')

// Send - Compose
const compose = ref({
  targetHost: '',
  targetPort: '',
  adtType: 'ADT^A01',
  priority: 0,
  patientId: '',
  patientName: '',
  dateOfBirth: '',
  gender: '',
  visitId: '',
  department: '',
  ward: '',
  room: '',
  bed: '',
  attendingDoctor: '',
  admitDiagnosis: '',
  sendingFacility: '',
  receivingFacility: '',
})

function showNotification(msg: string) {
  notification.value = msg
  if (notifTimer) clearTimeout(notifTimer)
  notifTimer = window.setTimeout(() => { notification.value = '' }, 4000)
}

const adtTypes = ['ADT^A01', 'ADT^A02', 'ADT^A03', 'ADT^A04', 'ADT^A05', 'ADT^A06', 'ADT^A07', 'ADT^A08']
const queueStatusText = ['待发送', '发送中', '已发送', '失败']

function isQueueSuccess(status: number) {
  return status === 2
}

function isQueueError(status: number) {
  return status === 3
}

async function loadQueue() {
  queueLoading.value = true
  queueError.value = ''
  try {
    const data = await fetchAdtQueue()
    queueItems.value = Array.isArray(data) ? data : data.items || data.records || data.data || []
  } catch (err: any) {
    queueItems.value = []
    queueError.value = err.message || '加载失败'
  } finally {
    queueLoading.value = false
  }
}

async function loadLogs() {
  logLoading.value = true
  logError.value = ''
  try {
    const data = await fetchAdtLogs(logPage.value, logPageSize.value)
    logItems.value = Array.isArray(data) ? data : data.items || data.records || data.data || []
  } catch (err: any) {
    logItems.value = []
    logError.value = err.message || '加载失败'
  } finally {
    logLoading.value = false
  }
}

async function loadBridgeStatus() {
  bridgeLoading.value = true
  try {
    bridgeStatus.value = await fetchAdtBridgeStatus()
  } catch (err: any) {
    bridgeStatus.value = { reachable: false, error: err.message || '桥接状态读取失败' }
  } finally {
    bridgeLoading.value = false
  }
}

async function loadBridgeLogs(reset = false) {
  if (bridgeLogPaused.value && !reset) return
  try {
    const sinceId = reset ? 0 : bridgeLogLastId.value
    const data = await fetchAdtBridgeLogs(sinceId, reset ? 200 : 100)
    bridgeLogError.value = data?.reachable === false ? (data?.error || '桥接日志不可用') : ''
    const items = Array.isArray(data?.items) ? data.items : []
    if (reset) bridgeLogItems.value = []
    if (items.length) {
      bridgeLogItems.value = [...bridgeLogItems.value, ...items].slice(-300)
      bridgeLogLastId.value = Math.max(...bridgeLogItems.value.map((item: any) => Number(item.Id ?? item.id ?? 0)))
      window.setTimeout(scrollBridgeLogsToBottom, 0)
    }
  } catch (err: any) {
    bridgeLogError.value = err.message || '桥接日志读取失败'
  }
}

function clearBridgeLogs() {
  bridgeLogItems.value = []
  bridgeLogLastId.value = 0
}

function scrollBridgeLogsToBottom() {
  const el = document.querySelector('.bridge-log-body')
  if (el) el.scrollTop = el.scrollHeight
}

function bridgeLogLevel(item: any) {
  return String(item.Level ?? item.level ?? 'Information')
}

function bridgeLogTime(item: any) {
  return formatDateTime(item.Time ?? item.time)
}

function bridgeLogMessage(item: any) {
  return String(item.Message ?? item.message ?? '')
}

function bridgeSubscribed() {
  return !!bridgeStatus.value?.reachable && bridgeStatus.value?.subscriber === true
}

function bridgeStatusLabel() {
  if (bridgeLoading.value && !bridgeStatus.value) return '读取中...'
  if (!bridgeStatus.value?.reachable) return '桥接件未连接'
  if (bridgeSubscribed()) return 'PIC iX 已订阅'
  if (bridgeStatus.value?.name) return '订阅已断开或超时，等待 PIC iX 重新订阅'
  return '桥接件在线，等待 PIC iX 订阅'
}

async function doSendRaw() {
  if (!sendMessageContent.value) {
    sendResult.value = '请先构建或输入 ADT 消息'
    return
  }
  sending.value = true
  sendResult.value = ''
  try {
    const res = await sendAdtMessage({
      targetHost: sendTargetHost.value,
      targetPort: Number(sendTargetPort.value),
      adtType: sendAdtType.value,
      messageContent: sendMessageContent.value,
    })
    sendResult.value = res?.message || '发送成功'
    messageComposed.value = false
    await loadQueue()
    await loadLogs()
  } catch (err: any) {
    sendResult.value = `发送失败: ${err.message}`
  } finally {
    sending.value = false
  }
}

function composePayload() {
  const c = compose.value
  const payload: any = {
    targetHost: c.targetHost,
    targetPort: Number(c.targetPort),
    adtType: c.adtType,
    priority: c.priority,
    patientId: c.patientId,
    patientName: c.patientName,
    gender: c.gender,
    visitId: c.visitId,
    department: c.department,
    ward: c.ward,
    room: c.room,
    bed: c.bed,
    attendingDoctor: c.attendingDoctor,
    admitDiagnosis: c.admitDiagnosis,
    sendingFacility: c.sendingFacility,
    receivingFacility: c.receivingFacility,
  }
  if (c.dateOfBirth) payload.dateOfBirth = c.dateOfBirth
  return payload
}

async function doBuildCompose() {
  const c = compose.value
  if (!c.patientId || !c.department || !c.bed) {
    sendResult.value = '请填写患者 ID / MRN、科室和床位'
    return
  }
  building.value = true
  sendResult.value = ''
  try {
    const res = await composeAdtMessage(composePayload())
    sendTargetHost.value = c.targetHost
    sendTargetPort.value = c.targetPort
    sendAdtType.value = res?.adtType || c.adtType
    sendMessageContent.value = res?.content || ''
    messageComposed.value = true
    sendResult.value = 'ADT 消息已生成，请检查下方内容后发送'
  } catch (err: any) {
    sendResult.value = `构建失败: ${err.message}`
  } finally {
    building.value = false
  }
}

async function doDelete(id: number) {
  if (!confirm('确定删除此队列项？')) return
  try {
    await deleteAdtQueueItem(id)
    showNotification('已删除')
    loadQueue()
  } catch { showNotification('删除失败') }
}

async function doRetry(id: number) {
  try {
    await retryAdtQueueItem(id)
    showNotification('已重置为重试')
    loadQueue()
  } catch { showNotification('重试失败') }
}

onMounted(async () => {
  loadQueue()
  loadLogs()
  loadBridgeStatus()
  loadBridgeLogs(true)
  bridgeTimer = window.setInterval(loadBridgeStatus, 10000)
  bridgeLogTimer = window.setInterval(() => loadBridgeLogs(false), 10000)
  await signalrService.start()
  signalrService.onMessageReceived(() => { loadQueue(); loadLogs(); showNotification('收到新消息') })
  signalrService.onAdtSent((data: any) => { loadQueue(); loadLogs(); loadBridgeStatus(); showNotification(`ADT ${data?.adtType || ''} 已发送`) })
})

onUnmounted(() => {
  if (notifTimer) clearTimeout(notifTimer)
  if (bridgeTimer) clearInterval(bridgeTimer)
  if (bridgeLogTimer) clearInterval(bridgeLogTimer)
  signalrService.off('MessageReceived')
  signalrService.off('AdtSent')
})
</script>

<template>
  <div class="adt-page">
    <div class="page-header">
      <h2>ADT 管理</h2>
      <div class="live-indicator" :class="{ connected: signalrConnected }">
        <span class="live-dot"></span>
        <span>{{ signalrConnected ? '实时连接' : '未连接' }}</span>
      </div>
    </div>

    <div v-if="notification" class="notification">{{ notification }}</div>

    <div class="bridge-card">
      <div class="bridge-main">
        <div class="bridge-title">Philips HIF / PPIS 桥接</div>
        <div class="bridge-subtitle">
          <span :class="['bridge-dot', bridgeSubscribed() ? 'ok' : bridgeStatus?.reachable ? 'warn' : 'bad']"></span>
          <span>{{ bridgeStatusLabel() }}</span>
        </div>
      </div>
      <div class="bridge-details">
        <span>地址：{{ bridgeStatus?.baseUrl || '-' }}</span>
        <span>订阅：{{ bridgeStatus?.name || '-' }}</span>
        <span>状态：{{ bridgeStatus?.subscriberState || '-' }}</span>
        <span>最近活动：{{ bridgeStatus?.lastSubscriberActivityAt || '-' }}</span>
        <span>患者：{{ bridgeStatus?.patients ?? 0 }}</span>
        <span>已加载：{{ bridgeStatus?.loadedPatients ?? 0 }}</span>
        <span>搜索：{{ bridgeStatus?.searchCount ?? 0 }}</span>
        <span>患者库：{{ bridgeStatus?.storageMode || '-' }} / {{ bridgeStatus?.store || bridgeStatus?.storePath || '-' }}</span>
        <span>最近推送：{{ bridgeStatus?.lastPushResult || bridgeStatus?.error || '-' }}</span>
      </div>
      <button class="btn btn-sm btn-secondary" @click="loadBridgeStatus">刷新</button>
    </div>

    <div class="bridge-log-panel">
      <div class="bridge-log-header">
        <div>
          <div class="bridge-log-title">桥接实时日志</div>
          <div class="bridge-log-subtitle">订阅、OnPIChange、SearchPatient、患者缓存等原始事件</div>
        </div>
        <div class="bridge-log-actions">
          <span v-if="bridgeLogError" class="bridge-log-error">{{ bridgeLogError }}</span>
          <button class="btn btn-sm btn-secondary" @click="bridgeLogPaused = !bridgeLogPaused">
            {{ bridgeLogPaused ? '继续' : '暂停' }}
          </button>
          <button class="btn btn-sm btn-secondary" @click="loadBridgeLogs(true)">刷新</button>
          <button class="btn btn-sm btn-secondary" @click="clearBridgeLogs">清空</button>
        </div>
      </div>
      <div class="bridge-log-body">
        <div v-if="!bridgeLogItems.length" class="bridge-log-empty">暂无桥接日志</div>
        <div
          v-for="item in bridgeLogItems"
          :key="item.Id || item.id"
          :class="['bridge-log-line', `level-${bridgeLogLevel(item).toLowerCase()}`]"
        >
          <span class="bridge-log-time">{{ bridgeLogTime(item) }}</span>
          <span class="bridge-log-level">{{ bridgeLogLevel(item) }}</span>
          <span class="bridge-log-message">{{ bridgeLogMessage(item) }}</span>
        </div>
      </div>
    </div>

    <div class="tabs">
      <button :class="['tab', { active: activeTab === 'queue' }]" @click="activeTab = 'queue'">队列</button>
      <button :class="['tab', { active: activeTab === 'logs' }]" @click="activeTab = 'logs'">日志</button>
      <button :class="['tab', { active: activeTab === 'send' }]" @click="activeTab = 'send'">发送</button>
    </div>

    <!-- Queue -->
    <div v-if="activeTab === 'queue'" class="tab-content">
      <div class="toolbar">
        <span class="toolbar-title">待发送队列</span>
        <button class="btn btn-sm btn-secondary" @click="loadQueue">刷新</button>
      </div>
      <table v-if="queueItems.length">
          <thead>
            <tr>
              <th>ID</th>
              <th>类型</th>
              <th>目标</th>
              <th>状态</th>
              <th>重试</th>
              <th>结果</th>
              <th>创建时间</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in queueItems" :key="item.queueId || item.id">
              <td>{{ item.queueId || item.id }}</td>
              <td>{{ item.adtMessageType || item.messageType }}</td>
              <td>{{ item.targetEndpoint }}</td>
              <td>
              <span :class="['badge', {
                'badge-warning': item.status === 0,
                'badge-info': item.status === 1,
                'badge-success': isQueueSuccess(item.status),
                'badge-error': isQueueError(item.status)
              }]">{{ queueStatusText[item.status] || item.status }}</span>
              </td>
              <td>{{ item.retryCount }}/{{ item.maxRetries }}</td>
              <td class="result-cell" :title="item.lastError || ''">{{ item.lastError || (isQueueSuccess(item.status) ? '已推送' : '-') }}</td>
              <td>{{ formatDateTime(item.createdAt) }}</td>
              <td>
                <button class="btn btn-sm btn-secondary" @click="doRetry(item.queueId || item.id)" :disabled="item.status === 0" title="重试">↻</button>
                <button class="btn btn-sm btn-danger" @click="doDelete(item.queueId || item.id)" title="删除">✕</button>
              </td>
          </tr>
        </tbody>
      </table>
      <div v-else-if="queueError" class="empty error">{{ queueError }}</div>
      <div v-else-if="!queueLoading" class="empty">队列为空</div>
      <div v-if="queueLoading" class="loading">加载中...</div>
    </div>

    <!-- Logs -->
    <div v-if="activeTab === 'logs'" class="tab-content">
      <div class="toolbar">
        <span class="toolbar-title">ADT 日志</span>
        <button class="btn btn-sm btn-secondary" @click="loadLogs">刷新</button>
      </div>
      <table v-if="logItems.length">
        <thead>
          <tr>
            <th>ID</th>
            <th>类型</th>
            <th>目标</th>
            <th>状态</th>
            <th>结果</th>
            <th>耗时</th>
            <th>时间</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="log in logItems" :key="log.logId || log.id">
            <td>{{ log.logId || log.id }}</td>
            <td>{{ log.messageType }}</td>
            <td>{{ log.targetEndpoint }}</td>
            <td>
              <span :class="['badge', log.status === 2 ? 'badge-success' : log.status === 3 ? 'badge-error' : 'badge-info']">
                {{ queueStatusText[log.status] || log.status }}
              </span>
            </td>
            <td class="result-cell" :title="log.errorMessage || log.responseContent || ''">
              {{ log.errorMessage || log.responseContent || '-' }}
            </td>
            <td>{{ log.durationMs != null ? log.durationMs + 'ms' : '-' }}</td>
            <td>{{ formatDateTime(log.createdAt) }}</td>
          </tr>
        </tbody>
      </table>
      <div v-else-if="logError" class="empty error">{{ logError }}</div>
      <div v-else-if="!logLoading" class="empty">暂无日志</div>
      <div v-if="logLoading" class="loading">加载中...</div>
    </div>

    <!-- Send -->
    <div v-if="activeTab === 'send'" class="tab-content">
      <div class="compose-form">
        <h3 class="form-title">构建 ADT 消息</h3>

        <div class="form-section">
          <div class="section-label">连接信息</div>
          <div class="form-row">
            <div class="form-group">
              <label>传统 MLLP 目标主机</label>
              <input v-model="compose.targetHost" placeholder="桥接模式可留空" />
            </div>
            <div class="form-group">
              <label>传统 MLLP 目标端口</label>
              <input v-model="compose.targetPort" type="number" placeholder="桥接模式可留空" />
            </div>
            <div class="form-group">
              <label>ADT 类型</label>
              <select v-model="compose.adtType">
                <option v-for="t in adtTypes" :key="t" :value="t">{{ t }}</option>
              </select>
            </div>
          </div>
        </div>

        <div class="form-section">
          <div class="section-label">患者信息</div>
          <div class="form-row">
            <div class="form-group">
              <label>患者 ID / MRN *</label>
              <input v-model="compose.patientId" required placeholder="必填，对应飞利浦 MRN" />
            </div>
            <div class="form-group">
              <label>患者姓名</label>
              <input v-model="compose.patientName" placeholder="张三" />
            </div>
            <div class="form-group">
              <label>出生日期</label>
              <input v-model="compose.dateOfBirth" type="date" />
            </div>
            <div class="form-group">
              <label>性别</label>
              <select v-model="compose.gender"><option value="">--</option><option value="M">男</option><option value="F">女</option><option value="O">其他</option></select>
            </div>
          </div>
        </div>

        <div class="form-section">
          <div class="section-label">就诊信息</div>
          <div class="form-row">
            <div class="form-group">
              <label>就诊号</label>
              <input v-model="compose.visitId" placeholder="留空则用患者ID" />
            </div>
            <div class="form-group">
              <label>科室 *</label>
              <input v-model="compose.department" required placeholder="如 ICU" />
            </div>
            <div class="form-group">
              <label>病区</label>
              <input v-model="compose.ward" placeholder="如 ICU2" />
            </div>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label>房间</label>
              <input v-model="compose.room" />
            </div>
            <div class="form-group">
              <label>床位 *</label>
              <input v-model="compose.bed" required placeholder="如 01" />
            </div>
            <div class="form-group">
              <label>主治医生</label>
              <input v-model="compose.attendingDoctor" />
            </div>
          </div>
          <div class="form-group">
            <label>入院诊断</label>
            <input v-model="compose.admitDiagnosis" placeholder="诊断描述" />
          </div>
        </div>

        <div class="form-section">
          <div class="section-label">系统信息</div>
          <div class="form-row">
            <div class="form-group">
              <label>发送站点</label>
              <input v-model="compose.sendingFacility" placeholder="如 HL7Gateway" />
            </div>
            <div class="form-group">
              <label>接收站点</label>
              <input v-model="compose.receivingFacility" placeholder="如 PICiX" />
            </div>
          </div>
        </div>

        <div class="form-actions">
          <button class="btn btn-primary" :disabled="building || sending" @click="doBuildCompose">
            {{ building ? '构建中...' : '构建消息' }}
          </button>
        </div>

        <div class="form-section message-preview">
          <div class="section-label">生成的 HL7 消息</div>
          <div class="preview-meta">
            <span :class="['badge', messageComposed ? 'badge-success' : 'badge-warning']">
              {{ messageComposed ? '已构建' : '未构建' }}
            </span>
            <span>ADT 类型：{{ sendAdtType }}</span>
            <span>目标：{{ sendTargetHost || compose.targetHost ? `${sendTargetHost || compose.targetHost}:${sendTargetPort || compose.targetPort}` : 'Philips HIF bridge' }}</span>
          </div>
          <div class="form-group">
            <textarea
              v-model="sendMessageContent"
              rows="9"
              placeholder="点击“构建消息”后，这里会显示 HL7 内容，可检查或手动调整..."
            ></textarea>
          </div>
          <div class="form-actions">
            <button class="btn btn-primary" :disabled="sending || !sendMessageContent" @click="doSendRaw">
              {{ sending ? '发送中...' : '发送消息' }}
            </button>
          </div>
        </div>

        <div v-if="sendResult" :class="['send-result', sendResult.includes('失败') ? 'error' : 'success']">
          {{ sendResult }}
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.adt-page {
  max-width: 1200px;
}

.tabs {
  display: flex;
  gap: 2px;
  margin-bottom: 0;
  background: var(--card-bg);
  border-radius: var(--radius) var(--radius) 0 0;
  box-shadow: var(--shadow);
  overflow: hidden;
}

.tab {
  padding: 10px 24px;
  border: none;
  background: transparent;
  font-size: 13px;
  cursor: pointer;
  color: var(--text-secondary);
  border-bottom: 2px solid transparent;
  transition: all 0.2s;
}

.tab:hover {
  color: var(--text-primary);
  background: #f9fafb;
}

.tab.active {
  color: var(--accent);
  border-bottom-color: var(--accent);
  font-weight: 500;
}

.tab-content {
  background: var(--card-bg);
  border-radius: 0 0 var(--radius) var(--radius);
  box-shadow: var(--shadow);
  padding: 20px;
  overflow-x: auto;
}

.bridge-card {
  display: grid;
  grid-template-columns: minmax(190px, 240px) 1fr auto;
  gap: 16px;
  align-items: center;
  padding: 14px 16px;
  margin-bottom: 14px;
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
}

.bridge-title {
  font-weight: 600;
  color: var(--text-primary);
}

.bridge-subtitle {
  display: flex;
  align-items: center;
  gap: 7px;
  margin-top: 4px;
  color: var(--text-secondary);
  font-size: 12px;
}

.bridge-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #ef4444;
}

.bridge-dot.ok {
  background: #10b981;
}

.bridge-dot.warn {
  background: #f59e0b;
}

.bridge-dot.bad {
  background: #ef4444;
}

.bridge-details {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 14px;
  color: var(--text-secondary);
  font-size: 12px;
  min-width: 0;
}

.bridge-details span {
  max-width: 360px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.bridge-log-panel {
  margin-bottom: 16px;
  background: #101827;
  border: 1px solid #1f2937;
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  overflow: hidden;
}

.bridge-log-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  background: #111827;
  border-bottom: 1px solid #243044;
}

.bridge-log-title {
  color: #f9fafb;
  font-size: 14px;
  font-weight: 600;
}

.bridge-log-subtitle {
  margin-top: 2px;
  color: #9ca3af;
  font-size: 12px;
}

.bridge-log-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.bridge-log-error {
  color: #fca5a5;
  font-size: 12px;
  max-width: 360px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.bridge-log-body {
  height: 240px;
  overflow: auto;
  padding: 8px 0;
  font-family: var(--font-mono);
  font-size: 12px;
  line-height: 1.45;
}

.bridge-log-empty {
  padding: 34px;
  text-align: center;
  color: #6b7280;
  font-family: inherit;
}

.bridge-log-line {
  display: grid;
  grid-template-columns: 150px 92px minmax(0, 1fr);
  gap: 10px;
  padding: 3px 14px;
  color: #d1d5db;
}

.bridge-log-line:hover {
  background: #1f2937;
}

.bridge-log-time {
  color: #9ca3af;
}

.bridge-log-level {
  color: #93c5fd;
}

.bridge-log-message {
  white-space: pre-wrap;
  word-break: break-word;
}

.bridge-log-line.level-warning .bridge-log-level {
  color: #fbbf24;
}

.bridge-log-line.level-error .bridge-log-level {
  color: #f87171;
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}

.toolbar-title {
  font-weight: 600;
  font-size: 14px;
  color: var(--text-primary);
}

.send-mode-switch {
  display: flex;
  gap: 0;
  margin-bottom: 16px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  overflow: hidden;
  width: fit-content;
}

.mode-btn {
  padding: 6px 16px;
  border: none;
  background: transparent;
  font-size: 13px;
  cursor: pointer;
  color: var(--text-secondary);
  transition: all 0.2s;
}

.mode-btn.active {
  background: var(--accent);
  color: white;
}

table {
  width: 100%;
  border-collapse: collapse;
}

th {
  text-align: left;
  padding: 10px 12px;
  font-size: 12px;
  font-weight: 600;
  color: var(--text-secondary);
  background: #f9fafb;
  border-bottom: 1px solid var(--border-color);
}

td {
  padding: 8px 12px;
  font-size: 13px;
  border-bottom: 1px solid var(--border-color);
}

tbody tr:nth-child(even) {
  background: #f9fafb;
}

.badge {
  display: inline-block;
  padding: 2px 10px;
  border-radius: 12px;
  font-size: 12px;
  font-weight: 500;
}

.badge-success {
  background: var(--success-bg);
  color: #065f46;
}

.badge-error {
  background: var(--error-bg);
  color: #991b1b;
}

.badge-warning {
  background: var(--warning-bg);
  color: #92400e;
}

.badge-info {
  background: var(--info-bg);
  color: #1e40af;
}

.result-cell {
  max-width: 280px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--text-secondary);
}

.message-preview {
  border: 1px solid var(--border-color);
  background: #fff;
}

.preview-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 14px;
  margin-bottom: 10px;
  font-size: 12px;
  color: var(--text-secondary);
}

.form-section {
  margin-bottom: 16px;
  padding: 12px 14px;
  background: #f9fafb;
  border-radius: var(--radius-sm);
}

.section-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-secondary);
  margin-bottom: 10px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.send-form,
.compose-form {
  max-width: 800px;
}

.form-title {
  font-size: 15px;
  font-weight: 600;
  margin-bottom: 16px;
  color: var(--text-primary);
}

.form-row {
  display: flex;
  gap: 12px;
  margin-bottom: 10px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
  flex: 1;
  margin-bottom: 6px;
}

.form-group label {
  font-size: 12px;
  color: var(--text-secondary);
  font-weight: 500;
}

.form-group input,
.form-group select {
  padding: 8px 10px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  font-size: 13px;
  outline: none;
  transition: border-color 0.2s;
}

.form-group input:focus,
.form-group select:focus {
  border-color: var(--accent);
}

.form-group textarea {
  padding: 8px 10px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  font-size: 13px;
  outline: none;
  font-family: var(--font-mono);
  resize: vertical;
  transition: border-color 0.2s;
}

.form-group textarea:focus {
  border-color: var(--accent);
}

.form-actions {
  margin-top: 12px;
}

.btn {
  padding: 8px 20px;
  border: none;
  border-radius: var(--radius-sm);
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  transition: background 0.2s;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-primary {
  background: var(--accent);
  color: white;
}

.btn-primary:hover:not(:disabled) {
  background: var(--accent-hover);
}

.btn-secondary {
  background: var(--gray-bg);
  color: var(--text-primary);
}

.btn-secondary:hover:not(:disabled) {
  background: #e5e7eb;
}

.btn-sm {
  padding: 4px 12px;
  font-size: 12px;
}

.send-result {
  margin-top: 12px;
  padding: 10px 14px;
  border-radius: var(--radius-sm);
  font-size: 13px;
}

.send-result.success {
  background: var(--success-bg);
  color: #065f46;
}

.send-result.error {
  background: var(--error-bg);
  color: #991b1b;
}

.loading {
  text-align: center;
  padding: 40px;
  color: var(--text-muted);
}

.empty {
  text-align: center;
  padding: 40px;
  color: var(--text-muted);
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 20px;
}

.page-header h2 {
  font-size: 22px;
}

.live-indicator {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  color: var(--text-muted);
}

.live-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--text-muted);
  display: inline-block;
}

.live-indicator.connected .live-dot {
  background: var(--success);
  box-shadow: 0 0 6px var(--success);
}

.live-indicator.connected {
  color: var(--success);
}

.notification {
  background: var(--info-bg);
  color: #1e40af;
  padding: 10px 16px;
  border-radius: var(--radius);
  margin-bottom: 16px;
  font-size: 13px;
  border: 1px solid #bfdbfe;
}

.empty {
  text-align: center;
  padding: 40px;
  color: var(--text-muted);
}
</style>
