<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import {
  admitAutoAdt,
  dischargeAutoAdt,
  fetchAdtBridgeStatus,
  fetchAutoAdtBindings,
  fetchAutoAdtDashboard,
  fetchAutoAdtFeatures,
  scanAutoAdtBed,
  scanAutoAdtPatient,
  transferAutoAdt,
  updateAutoAdtPatient,
  upsertAutoAdtPatient,
} from '../api'
import { formatDateTime } from '../utils/time'

const bridgeStatus = ref<any>(null)
const patientCode = ref('')
const bedCode = ref('')
const patientScan = ref<any>(null)
const bedScan = ref<any>(null)
const result = ref<any>(null)
const error = ref('')
const busy = ref(false)
const force = ref(false)
const dashboard = ref<any>(null)
const bindings = ref<any[]>([])
const features = ref<any>({ autoAdmitEnabled: false, autoAdmitConfirmSeconds: 3 })
const autoCountdown = ref(0)
const autoPending = ref(false)
let pushPollTimer: number | undefined
let autoCountdownTimer: number | undefined

// B11: 病人当前是否已有在用绑定（已入住某张床）
const patientBinding = computed(() => {
  const mrn = patientForm.value.mrn?.trim()
  if (!mrn) return null
  return bindings.value.find((b: any) => b.patientId === mrn) || null
})
const hasPatient = computed(() => !!(patientForm.value.mrn && patientForm.value.visitNumber))
const scannedBedId = computed(() => bedScan.value?.bed?.id ?? null)
// 已入住病人 + 扫了一张“不同的”床 => 可转床
const canTransfer = computed(() =>
  hasPatient.value && !!patientBinding.value && !!scannedBedId.value
  && scannedBedId.value !== patientBinding.value.bedId)
const canAdmit = computed(() => hasPatient.value && !patientBinding.value)
const canUpdate = computed(() => hasPatient.value)
const canDischarge = computed(() => hasPatient.value && !!patientBinding.value)

async function loadBindings() {
  try {
    const d = await fetchAutoAdtBindings(true)
    bindings.value = Array.isArray(d) ? d : (d.items || d.records || [])
  } catch {
    bindings.value = []
  }
}

const patientForm = ref({
  mrn: '',
  visitNumber: '',
  patientName: '',
  familyName: '',
  givenName: '',
  gender: 'M',
  dateOfBirth: '',
})

async function loadBridge() {
  try {
    bridgeStatus.value = await fetchAdtBridgeStatus()
    dashboard.value = await fetchAutoAdtDashboard()
  } catch (err: any) {
    bridgeStatus.value = { reachable: false, error: err.message }
  }
}

// C1: 入队后桥接推送是异步的，短轮询刷新桥接状态以反映本次推送结果
function startPushPoll() {
  let n = 0
  if (pushPollTimer) clearInterval(pushPollTimer)
  pushPollTimer = window.setInterval(async () => {
    n++
    await loadBridge()
    await loadBindings()
    if (n >= 6 && pushPollTimer) clearInterval(pushPollTimer)
  }, 2000)
}

async function scanPatient() {
  error.value = ''
  result.value = null
  if (!patientCode.value) {
    error.value = '请扫描或输入病人腕带码'
    return
  }
  try {
    const data = await scanAutoAdtPatient(patientCode.value)
    patientScan.value = data
    await loadBindings()
    patientForm.value.mrn = data.mrn || ''
    patientForm.value.visitNumber = data.visitNumber || data.mrn || ''
    patientForm.value.patientName = data.patient?.name || patientForm.value.patientName
    patientForm.value.gender = data.patient?.gender || patientForm.value.gender
    patientForm.value.dateOfBirth = String(data.patient?.dateOfBirth || '').slice(0, 10)
  } catch (err: any) {
    error.value = err.message || '病人扫码解析失败'
  }
}

async function scanBed() {
  error.value = ''
  result.value = null
  if (!bedCode.value) {
    error.value = '请扫描或输入床位/设备码'
    return
  }
  try {
    bedScan.value = await scanAutoAdtBed(bedCode.value)
  } catch (err: any) {
    bedScan.value = null
    error.value = err.message || '床位扫码解析失败'
  }
}

async function savePatientOnly() {
  error.value = ''
  if (!patientForm.value.mrn || !patientForm.value.visitNumber) {
    error.value = 'MRN 和 Visit Number 必填'
    return
  }
  busy.value = true
  try {
    result.value = await upsertAutoAdtPatient(patientForm.value)
  } catch (err: any) {
    error.value = err.message || '保存病人失败'
  } finally {
    busy.value = false
  }
}

async function loadFeatures() {
  try {
    features.value = await fetchAutoAdtFeatures()
  } catch {
    features.value = { autoAdmitEnabled: false, autoAdmitConfirmSeconds: 3 }
  }
}

function cancelAutoAdmit() {
  autoPending.value = false
  autoCountdown.value = 0
  if (autoCountdownTimer) clearInterval(autoCountdownTimer)
}

function scheduleAutoAdmit() {
  if (!features.value?.autoAdmitEnabled || !canAdmit.value || !bedScan.value?.bed || busy.value) return
  cancelAutoAdmit()
  autoPending.value = true
  autoCountdown.value = Math.max(0, Number(features.value.autoAdmitConfirmSeconds) || 0)
  if (autoCountdown.value === 0) {
    void executeAdmit(false)
    return
  }
  autoCountdownTimer = window.setInterval(() => {
    autoCountdown.value -= 1
    if (autoCountdown.value <= 0) {
      cancelAutoAdmit()
      void executeAdmit(false)
    }
  }, 1000)
}

watch([() => patientForm.value.mrn, () => patientForm.value.visitNumber, () => bedScan.value?.bed?.id, () => features.value?.autoAdmitEnabled], () => {
  if (features.value?.autoAdmitEnabled && canAdmit.value && bedScan.value?.bed) scheduleAutoAdmit()
  else cancelAutoAdmit()
})

async function executeAdmit(useBrowserConfirm: boolean) {
  await runAdtAction('A01', '入院 A01', admitAutoAdt, true, useBrowserConfirm)
}

async function admit() {
  cancelAutoAdmit()
  await executeAdmit(true)
}

async function updatePatient() {
  await runAdtAction('A08', '更新 A08', updateAutoAdtPatient, false)
}

async function transfer() {
  await runAdtAction('A02', '转床 A02', transferAutoAdt, true)
}

async function discharge() {
  await runAdtAction('A03', '出院 A03', dischargeAutoAdt, false)
}

async function runAdtAction(eventType: string, label: string, action: (data: any) => Promise<any>, requireBed: boolean, useBrowserConfirm = true) {
  error.value = ''
  result.value = null
  if (!patientForm.value.mrn || !patientForm.value.visitNumber) {
    error.value = 'MRN 和 Visit Number 必填'
    return
  }
  if (requireBed && !bedScan.value?.bed?.id) {
    error.value = '请先扫描并确认目标床位'
    return
  }
  const target = bedScan.value?.bed?.philipsLocationValue || '当前绑定床位'
  if (useBrowserConfirm && !confirm(`确认${label}：${patientForm.value.mrn} -> ${target}？`)) return
  busy.value = true
  try {
    result.value = await action({
      patient: patientForm.value,
      bedId: bedScan.value?.bed?.id,
      priority: 0,
      force: force.value,
    })
    await loadBridge()
    await loadBindings()
    startPushPoll()
  } catch (err: any) {
    error.value = err.message || `${eventType} 操作失败`
  } finally {
    busy.value = false
  }
}

function bridgeLabel() {
  if (!bridgeStatus.value) return '读取中'
  if (!bridgeStatus.value.reachable) return '桥接件离线'
  if (bridgeStatus.value.subscriber === true) return 'PIC iX 已订阅'
  if (bridgeStatus.value.name) return '订阅已断开或超时'
  return '桥接件在线，等待订阅'
}

onMounted(async () => {
  await loadFeatures()
  await loadBridge()
  await loadBindings()
})
onUnmounted(() => {
  if (pushPollTimer) clearInterval(pushPollTimer)
  cancelAutoAdmit()
})
</script>

<template>
  <div class="auto-page">
    <div class="page-header">
      <h2>Auto ADT 扫码入院</h2>
      <button class="btn btn-secondary" @click="loadBridge">刷新桥接状态</button>
    </div>

    <div class="status-strip">
      <span :class="['dot', bridgeStatus?.reachable && bridgeStatus?.subscriber === true ? 'ok' : bridgeStatus?.reachable ? 'warn' : 'bad']"></span>
      <strong>{{ bridgeLabel() }}</strong>
      <span>订阅：{{ bridgeStatus?.name || '-' }}</span>
      <span>状态：{{ bridgeStatus?.subscriberState || '-' }}</span>
      <span>患者库：{{ bridgeStatus?.storageMode || '-' }} / {{ bridgeStatus?.store || '-' }}</span>
      <span>SearchPatient：{{ bridgeStatus?.searchCount ?? 0 }}</span>
    </div>

    <div v-if="dashboard" class="metric-row">
      <div><span>今日入院</span><strong>{{ dashboard.todayAdmit ?? 0 }}</strong></div>
      <div><span>今日更新</span><strong>{{ dashboard.todayUpdate ?? 0 }}</strong></div>
      <div><span>今日转床</span><strong>{{ dashboard.todayTransfer ?? 0 }}</strong></div>
      <div><span>今日出院</span><strong>{{ dashboard.todayDischarge ?? 0 }}</strong></div>
      <div><span>活动绑定</span><strong>{{ dashboard.activeBindings ?? 0 }}</strong></div>
      <div><span>待发送</span><strong>{{ dashboard.queuedMessages ?? 0 }}</strong></div>
    </div>

    <div v-if="error" class="error-bar">{{ error }}</div>
    <div v-if="autoPending" class="auto-confirm-bar">
      <div>
        <strong>即将自动入院 A01</strong>
        <p>{{ patientForm.mrn }} → {{ bedScan?.bed?.philipsLocationValue }}（{{ autoCountdown > 0 ? `${autoCountdown} 秒后执行` : '执行中…' }}）</p>
      </div>
      <button class="btn btn-secondary btn-sm" @click="cancelAutoAdmit">取消</button>
    </div>

    <div class="scan-grid">
      <section class="panel">
        <h3>1. 病人腕带</h3>
        <div class="scan-row">
          <input v-model="patientCode" placeholder="扫描腕带码或输入 MRN" @keyup.enter="scanPatient" autofocus />
          <button class="btn btn-primary" @click="scanPatient">解析</button>
        </div>
        <div class="form-grid">
          <label>MRN *<input v-model="patientForm.mrn" /></label>
          <label>Visit Number *<input v-model="patientForm.visitNumber" /></label>
          <label>姓名<input v-model="patientForm.patientName" /></label>
          <label>姓<input v-model="patientForm.familyName" /></label>
          <label>名<input v-model="patientForm.givenName" /></label>
          <label>性别
            <select v-model="patientForm.gender">
              <option value="M">男 M</option>
              <option value="F">女 F</option>
              <option value="U">未知 U</option>
            </select>
          </label>
          <label>出生日期<input v-model="patientForm.dateOfBirth" type="date" /></label>
        </div>
        <button class="btn btn-secondary" :disabled="busy" @click="savePatientOnly">仅保存病人</button>
      </section>

      <section class="panel">
        <h3>2. 床位 / 设备码</h3>
        <div class="scan-row">
          <input v-model="bedCode" placeholder="扫描设备码或床位码" @keyup.enter="scanBed" />
          <button class="btn btn-primary" @click="scanBed">解析</button>
        </div>
        <div v-if="bedScan?.bed" class="preview">
          <div><span>床位</span><strong>{{ bedScan.bed.careArea || '-' }} / {{ bedScan.bed.room || '-' }} / {{ bedScan.bed.bed || '-' }}</strong></div>
          <div><span>标签</span><strong>{{ bedScan.bed.bedLabel || '-' }}</strong></div>
          <div><span>设备</span><strong>{{ bedScan.bed.deviceCode || '-' }}</strong></div>
          <div><span>PhilipsLocationValue</span><strong class="mono">{{ bedScan.bed.philipsLocationValue }}</strong></div>
          <div><span>占用</span><strong>{{ bedScan.activeBinding ? `${bedScan.activeBinding.patientId}/${bedScan.activeBinding.visitId}` : '空闲' }}</strong></div>
        </div>
        <div v-else class="empty">等待扫描床位</div>
      </section>
    </div>

    <section class="panel">
      <div class="admit-bar">
        <div>
          <strong>3. 执行 Auto ADT</strong>
          <p>系统会写入 Patients / Visits，维护床位绑定，创建 Auto ADT 日志，并加入现有 ADT 发送队列。</p>
          <p v-if="hasPatient" class="binding-hint">
            <template v-if="patientBinding">当前状态：已入住（床位 #{{ patientBinding.bedId }}），可更新 / 转床 / 出院</template>
            <template v-else>当前状态：未入住，可执行入院</template>
          </p>
          <label class="force-check"><input v-model="force" type="checkbox" /> 覆盖目标床位已有绑定</label>
        </div>
        <div class="action-group">
          <button v-if="canAdmit" class="btn btn-primary" :disabled="busy" @click="admit">入院 A01</button>
          <button v-if="canUpdate" class="btn btn-secondary" :disabled="busy" @click="updatePatient">更新 A08</button>
          <button v-if="canTransfer" class="btn btn-warning" :disabled="busy" @click="transfer">转床 A02</button>
          <button v-if="canDischarge" class="btn btn-danger" :disabled="busy" @click="discharge">出院 A03</button>
          <span v-if="!hasPatient" class="action-tip">请先扫描/录入病人 MRN 与 Visit Number</span>
        </div>
      </div>
    </section>

    <section v-if="result" class="panel result-panel">
      <h3>结果</h3>
      <div class="preview">
        <div><span>消息</span><strong>{{ result.message || '已完成' }}</strong></div>
        <div><span>队列 ID</span><strong>{{ result.queueItem?.queueId || '-' }}</strong></div>
        <div><span>事件 ID</span><strong>{{ result.autoEvent?.id || '-' }}</strong></div>
        <div><span>Message Control ID</span><strong>{{ result.autoMessage?.messageControlId || result.autoEvent?.messageControlId || '-' }}</strong></div>
        <div><span>时间</span><strong>{{ formatDateTime(result.autoEvent?.createdAt) }}</strong></div>
        <div><span>桥接订阅</span><strong>{{ bridgeLabel() }}</strong></div>
        <div>
          <span>桥接推送结果</span>
          <strong :class="{ 'push-ok': bridgeStatus?.lastPushResult }">{{ bridgeStatus?.lastPushResult || '等待 PIC iX 推送…（自动刷新中）' }}</strong>
        </div>
        <div><span>最近推送时间</span><strong>{{ bridgeStatus?.lastPushAt || '-' }}</strong></div>
      </div>
      <textarea v-if="result.hl7" readonly :value="result.hl7"></textarea>
    </section>
  </div>
</template>

<style scoped>
.auto-page { max-width: 1280px; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
.status-strip { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); padding: 12px 16px; margin-bottom: 16px; font-size: 13px; }
.metric-row { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 10px; margin-bottom: 16px; }
.metric-row div { background: var(--card-bg); box-shadow: var(--shadow); border-radius: var(--radius); padding: 12px; }
.metric-row span { display: block; color: var(--text-secondary); font-size: 12px; margin-bottom: 4px; }
.metric-row strong { font-size: 22px; }
.dot { width: 10px; height: 10px; border-radius: 50%; background: #ef4444; }
.dot.ok { background: #22c55e; }
.dot.warn { background: #f59e0b; }
.dot.bad { background: #ef4444; }
.scan-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.panel { background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); padding: 16px; margin-bottom: 16px; }
.panel h3 { font-size: 16px; margin-bottom: 14px; }
.scan-row { display: flex; gap: 8px; margin-bottom: 14px; }
.scan-row input { flex: 1; }
.form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; margin-bottom: 12px; }
label { display: flex; flex-direction: column; gap: 6px; font-size: 12px; color: var(--text-secondary); }
input, select, textarea { padding: 8px 10px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 13px; }
.preview { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; }
.preview div { background: #f8fafc; border: 1px solid var(--border-color); border-radius: var(--radius-sm); padding: 10px; }
.preview span { display: block; color: var(--text-secondary); font-size: 12px; margin-bottom: 4px; }
.mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
.admit-bar { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
.admit-bar p { color: var(--text-secondary); font-size: 13px; margin-top: 6px; }
.force-check { margin-top: 10px; display: inline-flex; flex-direction: row; align-items: center; gap: 6px; }
.action-group { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 8px; }
textarea { width: 100%; min-height: 120px; margin-top: 12px; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
.binding-hint { color: var(--accent); font-size: 12px; margin-top: 6px; }
.action-tip { color: var(--text-secondary); font-size: 12px; align-self: center; }
.push-ok { color: #16a34a; }
.error-bar { background: #fef2f2; color: #b91c1c; padding: 10px 12px; border-radius: var(--radius-sm); margin-bottom: 12px; }
.auto-confirm-bar { display: flex; justify-content: space-between; align-items: center; gap: 12px; background: #fffbeb; border: 1px solid #fcd34d; color: #92400e; padding: 12px 16px; border-radius: var(--radius-sm); margin-bottom: 12px; }
.auto-confirm-bar p { margin: 4px 0 0; font-size: 13px; }
.empty { padding: 22px; text-align: center; color: var(--text-secondary); background: #f8fafc; border-radius: var(--radius-sm); }
@media (max-width: 1000px) { .scan-grid { grid-template-columns: 1fr; } .preview { grid-template-columns: 1fr; } .metric-row { grid-template-columns: repeat(2, minmax(0, 1fr)); } .admit-bar { align-items: stretch; flex-direction: column; } .action-group { justify-content: flex-start; } }
</style>
