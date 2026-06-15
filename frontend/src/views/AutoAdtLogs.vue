<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { fetchAutoAdtEvents, fetchAutoAdtMessages, resendAutoAdtMessage } from '../api'
import { formatDateTime } from '../utils/time'

const activeTab = ref<'events' | 'messages'>('events')
const events = ref<any[]>([])
const messages = ref<any[]>([])
const loading = ref(false)
const busyId = ref<number | null>(null)
const error = ref('')
const detail = ref<any>(null)
const copied = ref(false)

function openDetail(item: any) {
  detail.value = item
  copied.value = false
}
function closeDetail() {
  detail.value = null
}
async function copyHl7() {
  if (!detail.value?.hl7Raw) return
  try {
    await navigator.clipboard.writeText(detail.value.hl7Raw)
    copied.value = true
    setTimeout(() => { copied.value = false }, 1500)
  } catch {
    error.value = '复制失败，请手动选择文本'
  }
}

const queueText: Record<number, string> = {
  0: '待发送',
  1: '发送中',
  2: '已发送',
  3: '失败',
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    if (activeTab.value === 'events') {
      const data = await fetchAutoAdtEvents(1, 100)
      events.value = data.items || []
    } else {
      const data = await fetchAutoAdtMessages(1, 100)
      messages.value = data.items || []
    }
  } catch (err: any) {
    error.value = err.message || '加载失败'
  } finally {
    loading.value = false
  }
}

function switchTab(tab: 'events' | 'messages') {
  activeTab.value = tab
  load()
}

function queueStatus(value: number | null | undefined) {
  if (value === null || value === undefined) return '-'
  return queueText[value] || String(value)
}

async function resend(item: any) {
  if (!confirm(`确认重发 ${item.messageType} #${item.id}？`)) return
  busyId.value = item.id
  error.value = ''
  try {
    await resendAutoAdtMessage(item.id)
    await load()
  } catch (err: any) {
    error.value = err.message || '重发失败'
  } finally {
    busyId.value = null
  }
}

onMounted(load)
</script>

<template>
  <div class="auto-page">
    <div class="page-header">
      <h2>Auto ADT 日志</h2>
      <button class="btn btn-secondary" @click="load">刷新</button>
    </div>

    <div class="tabs">
      <button :class="['tab', { active: activeTab === 'events' }]" @click="switchTab('events')">事件</button>
      <button :class="['tab', { active: activeTab === 'messages' }]" @click="switchTab('messages')">消息</button>
    </div>

    <div v-if="error" class="error-bar">{{ error }}</div>

    <section class="panel" v-if="activeTab === 'events'">
      <table v-if="events.length">
        <thead>
          <tr>
            <th>ID</th>
            <th>类型</th>
            <th>患者</th>
            <th>Visit</th>
            <th>目标床位</th>
            <th>状态</th>
            <th>操作人</th>
            <th>Control ID</th>
            <th>时间</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in events" :key="item.id">
            <td>{{ item.id }}</td>
            <td>{{ item.eventType }}</td>
            <td>{{ item.patientId }}</td>
            <td>{{ item.visitId }}</td>
            <td>{{ item.targetBedId || '-' }}</td>
            <td>{{ item.eventStatus }}</td>
            <td>{{ item.operatorUser || '-' }}</td>
            <td class="mono">{{ item.messageControlId }}</td>
            <td>{{ formatDateTime(item.createdAt) }}</td>
          </tr>
        </tbody>
      </table>
      <div v-else-if="!loading" class="empty">暂无事件</div>
    </section>

    <section class="panel" v-else>
      <table v-if="messages.length">
        <thead>
          <tr>
            <th>ID</th>
            <th>事件</th>
            <th>类型</th>
            <th>队列</th>
            <th>发送状态</th>
            <th>队列状态</th>
            <th>错误/响应</th>
            <th>Control ID</th>
            <th>时间</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in messages" :key="item.id">
            <td>{{ item.id }}</td>
            <td>{{ item.eventId }}</td>
            <td>{{ item.messageType }}</td>
            <td>{{ item.adtQueueId || '-' }}</td>
            <td>{{ item.sendStatus }}</td>
            <td>{{ queueStatus(item.queueStatus) }}</td>
            <td class="result-cell">{{ item.queueError || item.queueAck || item.responseText || item.errorText || '-' }}</td>
            <td class="mono">{{ item.messageControlId }}</td>
            <td>{{ formatDateTime(item.createdAt) }}</td>
            <td class="actions">
              <button class="btn btn-sm btn-light" @click="openDetail(item)">详情</button>
              <button class="btn btn-sm btn-secondary" :disabled="busyId === item.id" @click="resend(item)">{{ busyId === item.id ? '处理中' : '重发' }}</button>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-else-if="!loading" class="empty">暂无消息</div>
    </section>
    <div v-if="loading" class="loading">加载中...</div>

    <!-- 消息详情 / HL7 原文 -->
    <div v-if="detail" class="modal-mask" @click.self="closeDetail">
      <div class="modal">
        <div class="modal-head">
          <h3>消息详情 #{{ detail.id }} · {{ detail.messageType }}</h3>
          <button class="modal-close" @click="closeDetail">×</button>
        </div>
        <div class="modal-body">
          <div class="kv"><span>Control ID</span><b class="mono">{{ detail.messageControlId }}</b></div>
          <div class="kv"><span>发送状态</span><b>{{ detail.sendStatus }}</b></div>
          <div class="kv"><span>队列状态</span><b>{{ queueStatus(detail.queueStatus) }}</b></div>
          <div class="kv"><span>重试次数</span><b>{{ detail.retryCount ?? 0 }}</b></div>
          <div class="kv"><span>入队时间</span><b>{{ formatDateTime(detail.queuedAt || detail.createdAt) }}</b></div>
          <div class="kv"><span>发送时间</span><b>{{ detail.sentAt ? formatDateTime(detail.sentAt) : '-' }}</b></div>

          <div v-if="detail.errorText || detail.queueError" class="block error">
            <div class="block-title">错误</div>
            <pre>{{ detail.errorText || detail.queueError }}</pre>
          </div>
          <div v-if="detail.responseText || detail.queueAck" class="block">
            <div class="block-title">PIC iX / 桥接响应</div>
            <pre>{{ detail.responseText || detail.queueAck }}</pre>
          </div>

          <div class="block">
            <div class="block-title">
              HL7 原文
              <button class="btn btn-sm btn-light copy-btn" @click="copyHl7">{{ copied ? '已复制' : '复制' }}</button>
            </div>
            <pre class="hl7">{{ detail.hl7Raw || '(无)' }}</pre>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.auto-page { max-width: 1280px; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
.tabs { display: flex; gap: 8px; margin-bottom: 16px; }
.tab { border: 1px solid var(--border-color); background: #fff; padding: 8px 16px; border-radius: var(--radius-sm); cursor: pointer; }
.tab.active { background: var(--accent); border-color: var(--accent); color: #fff; }
.panel { background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); overflow: hidden; }
table { width: 100%; border-collapse: collapse; font-size: 13px; }
th, td { padding: 10px 8px; border-bottom: 1px solid var(--border-color); text-align: left; vertical-align: top; }
th { color: var(--text-secondary); background: #f8fafc; }
.mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
.result-cell { max-width: 360px; word-break: break-word; }
.error-bar { background: #fef2f2; color: #b91c1c; padding: 10px 12px; border-radius: var(--radius-sm); margin-bottom: 12px; }
.empty, .loading { padding: 24px; text-align: center; color: var(--text-secondary); }
.actions { display: flex; gap: 6px; white-space: nowrap; }
.btn { border: none; border-radius: var(--radius-sm); padding: 6px 12px; cursor: pointer; font-size: 13px; }
.btn-sm { padding: 4px 10px; font-size: 12px; }
.btn-secondary { background: var(--accent); color: #fff; }
.btn-light { background: #eef2ff; color: #4338ca; }
.btn:disabled { opacity: 0.6; cursor: not-allowed; }
.modal-mask { position: fixed; inset: 0; background: rgba(15, 23, 42, 0.45); display: flex; align-items: center; justify-content: center; z-index: 1000; }
.modal { background: #fff; border-radius: 12px; width: 680px; max-width: 92vw; max-height: 86vh; display: flex; flex-direction: column; box-shadow: 0 20px 60px rgba(0,0,0,0.25); }
.modal-head { display: flex; align-items: center; justify-content: space-between; padding: 16px 20px; border-bottom: 1px solid var(--border-color); }
.modal-head h3 { font-size: 16px; }
.modal-close { border: none; background: transparent; font-size: 22px; line-height: 1; cursor: pointer; color: var(--text-secondary); }
.modal-body { padding: 16px 20px; overflow-y: auto; }
.kv { display: flex; gap: 12px; font-size: 13px; padding: 4px 0; }
.kv span { min-width: 88px; color: var(--text-secondary); }
.block { margin-top: 14px; }
.block-title { display: flex; align-items: center; gap: 10px; font-size: 13px; font-weight: 600; color: var(--text-primary); margin-bottom: 6px; }
.block pre { background: #0f172a; color: #e2e8f0; padding: 12px; border-radius: 8px; font-size: 12px; white-space: pre-wrap; word-break: break-word; max-height: 280px; overflow: auto; }
.block.error pre { background: #fef2f2; color: #b91c1c; }
.copy-btn { margin-left: auto; }
</style>
