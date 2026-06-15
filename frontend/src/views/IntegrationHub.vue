<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue'
import {
  fetchIntegrationPartners,
  fetchIntegrationTraces,
  injectIntegrationHl7,
} from '../api'
import { formatDateTime } from '../utils/time'
import IntegrationRouting from '../components/IntegrationRouting.vue'

const tab = ref<'hub' | 'routing'>('hub')

const partners = ref<any[]>([])
const recentTraces = ref<any[]>([])
const timeline = ref<any[]>([])
const traceIdInput = ref('')
const activeTraceId = ref('')
const injectHl7 = ref(`MSH|^~\\&|HL7Gateway|TEST|Philips.HIF|PICIX|20260615120000||ADT^A01|SIM001|P|2.3\rEVN|A01|20260615120000\rPID|||SIM001^^^^MR||TEST^PATIENT||19800101|M\rPV1||I|ICU^^ICU01`)
const injectMode = ref<'adt-queue' | 'bridge-direct'>('adt-queue')
const loading = ref(true)
const traceLoading = ref(false)
const injectBusy = ref(false)
const injectResult = ref('')
const error = ref('')
let refreshTimer: number | undefined

const statusColor: Record<string, string> = {
  ok: '#10b981',
  warn: '#f59e0b',
  error: '#ef4444',
  idle: '#9ca3af',
}

const statusLabel: Record<string, string> = {
  ok: '正常',
  warn: '注意',
  error: '异常',
  idle: '未启用',
}

const timelineEvents = computed(() =>
  activeTraceId.value ? timeline.value : [])

async function loadPartners() {
  try {
    const data = await fetchIntegrationPartners()
    partners.value = data.partners || []
    error.value = ''
  } catch (e: any) {
    error.value = e.message || '加载失败'
  }
  loading.value = false
}

async function loadRecentTraces() {
  try {
    const data = await fetchIntegrationTraces({ recent: 20 })
    recentTraces.value = data.recent || []
  } catch { /* ignore */ }
}

async function loadTimeline(id?: string) {
  const tid = (id ?? traceIdInput.value).trim()
  if (!tid) return
  traceLoading.value = true
  injectResult.value = ''
  try {
    const data = await fetchIntegrationTraces({ traceId: tid, limit: 200 })
    activeTraceId.value = data.traceId || tid
    timeline.value = data.events || []
  } catch (e: any) {
    error.value = e.message || 'Trace 加载失败'
    timeline.value = []
  }
  traceLoading.value = false
}

function pickRecent(traceId: string) {
  traceIdInput.value = traceId
  loadTimeline(traceId)
}

async function doInject() {
  injectBusy.value = true
  injectResult.value = ''
  error.value = ''
  try {
    const res = await injectIntegrationHl7(injectHl7.value, injectMode.value)
    injectResult.value = JSON.stringify(res, null, 2)
    if (res.traceId) {
      traceIdInput.value = res.traceId
      await loadTimeline(res.traceId)
    }
    await loadRecentTraces()
    await loadPartners()
  } catch (e: any) {
    error.value = e.message || '注入失败'
  }
  injectBusy.value = false
}

async function refreshAll() {
  await Promise.all([loadPartners(), loadRecentTraces()])
  if (activeTraceId.value) await loadTimeline(activeTraceId.value)
}

onMounted(() => {
  refreshAll()
  refreshTimer = window.setInterval(refreshAll, 30000)
})

onUnmounted(() => {
  if (refreshTimer) clearInterval(refreshTimer)
})
</script>

<template>
  <div class="integration-page">
    <div class="page-head">
      <div>
        <h2 class="page-title">集成中枢</h2>
        <p class="page-sub">对接伙伴健康 · Trace · 联调 · 模块化路由</p>
      </div>
      <div class="head-actions">
        <div class="tabs">
          <button :class="{ active: tab === 'hub' }" @click="tab = 'hub'">监控 / Trace</button>
          <button :class="{ active: tab === 'routing' }" @click="tab = 'routing'">入站路由</button>
        </div>
        <button v-if="tab === 'hub'" class="btn" @click="refreshAll">刷新</button>
      </div>
    </div>

    <IntegrationRouting v-if="tab === 'routing'" />

    <template v-else>

    <div v-if="error" class="alert error">{{ error }}</div>

    <section class="section">
      <h3>Partner Health</h3>
      <div v-if="loading" class="muted">加载中…</div>
      <div v-else class="partner-grid">
        <div v-for="p in partners" :key="p.key" class="partner-card">
          <div class="partner-top">
            <span class="dot" :style="{ background: statusColor[p.status] || '#9ca3af' }" />
            <strong>{{ p.name }}</strong>
            <span class="badge" :style="{ color: statusColor[p.status] || '#9ca3af' }">
              {{ statusLabel[p.status] || p.status }}
            </span>
          </div>
          <div class="partner-detail">{{ p.detail }}</div>
        </div>
      </div>
    </section>

    <div class="split">
      <section class="section">
        <h3>Trace 时间线</h3>
        <div class="trace-search">
          <input v-model="traceIdInput" placeholder="Message Control ID (MSH-10)" @keyup.enter="loadTimeline()" />
          <button class="btn primary" :disabled="traceLoading" @click="loadTimeline()">查询</button>
        </div>

        <div class="recent-box">
          <div class="recent-title">最近 24h Trace</div>
          <button
            v-for="r in recentTraces"
            :key="r.traceId"
            class="recent-item"
            @click="pickRecent(r.traceId)"
          >
            <code>{{ r.traceId }}</code>
            <span>{{ r.lastStep }} · {{ r.lastStatus }}</span>
            <small>{{ formatDateTime(r.lastAt) }}</small>
          </button>
          <div v-if="!recentTraces.length" class="muted">暂无 Trace 记录</div>
        </div>

        <div v-if="traceLoading" class="muted">加载 Trace…</div>
        <div v-else-if="activeTraceId" class="timeline">
          <div class="timeline-head">TraceId: <code>{{ activeTraceId }}</code></div>
          <div v-for="ev in timelineEvents" :key="ev.id" class="tl-item">
            <div class="tl-time">{{ formatDateTime(ev.createdAt) }}</div>
            <div class="tl-body">
              <div class="tl-step">
                <span class="tl-status" :class="ev.status.toLowerCase()">{{ ev.status }}</span>
                <strong>{{ ev.step }}</strong>
                <span class="tl-cat">{{ ev.category }}</span>
              </div>
              <div v-if="ev.detail" class="tl-detail">{{ ev.detail }}</div>
            </div>
          </div>
          <div v-if="!timelineEvents.length" class="muted">该 TraceId 无事件</div>
        </div>
      </section>

      <section class="section">
        <h3>联调注入</h3>
        <label class="field-label">模式</label>
        <select v-model="injectMode" class="select">
          <option value="adt-queue">入 ADT 队列（推荐）</option>
          <option value="bridge-direct">直 POST 桥接 /adt</option>
        </select>

        <label class="field-label">HL7 原文</label>
        <textarea v-model="injectHl7" rows="12" class="hl7-box" spellcheck="false" />

        <button class="btn primary wide" :disabled="injectBusy" @click="doInject">
          {{ injectBusy ? '注入中…' : '注入消息' }}
        </button>

        <pre v-if="injectResult" class="result-box">{{ injectResult }}</pre>

        <p class="hint">
          重放历史 ADT：在「消息列表」记下 MessageId，调用 API
          <code>POST /api/integration/simulate/replay/{messageId}</code>
        </p>
      </section>
    </div>
    </template>
  </div>
</template>

<style scoped>
.integration-page { padding: 0 4px; }
.page-head { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 16px; gap: 12px; }
.head-actions { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.tabs { display: flex; gap: 4px; background: #f3f4f6; padding: 3px; border-radius: 8px; }
.tabs button { border: none; background: transparent; padding: 6px 12px; border-radius: 6px; cursor: pointer; font-size: 0.85rem; }
.tabs button.active { background: #fff; box-shadow: 0 1px 2px rgba(0,0,0,.08); font-weight: 600; }
.page-title { margin: 0 0 4px; font-size: 1.35rem; }
.page-sub { margin: 0; color: #6b7280; font-size: 0.9rem; }
.section { background: #fff; border: 1px solid #e5e7eb; border-radius: 10px; padding: 16px; margin-bottom: 16px; }
.section h3 { margin: 0 0 12px; font-size: 1rem; }
.partner-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 12px; }
.partner-card { border: 1px solid #eef0f3; border-radius: 8px; padding: 12px; background: #fafbfc; }
.partner-top { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; }
.dot { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; }
.badge { margin-left: auto; font-size: 0.75rem; font-weight: 600; }
.partner-detail { color: #4b5563; font-size: 0.85rem; line-height: 1.4; }
.split { display: grid; grid-template-columns: 1.2fr 0.8fr; gap: 16px; }
@media (max-width: 1100px) { .split { grid-template-columns: 1fr; } }
.trace-search { display: flex; gap: 8px; margin-bottom: 12px; }
.trace-search input { flex: 1; padding: 8px 10px; border: 1px solid #d1d5db; border-radius: 6px; }
.recent-box { margin-bottom: 14px; max-height: 180px; overflow: auto; border: 1px dashed #e5e7eb; border-radius: 8px; padding: 8px; }
.recent-title { font-size: 0.8rem; color: #6b7280; margin-bottom: 6px; }
.recent-item { display: grid; grid-template-columns: 1fr auto; gap: 4px 8px; width: 100%; text-align: left; border: none; background: transparent; padding: 6px 4px; cursor: pointer; border-radius: 4px; }
.recent-item:hover { background: #f3f4f6; }
.recent-item code { font-size: 0.78rem; }
.recent-item small { grid-column: 2; color: #9ca3af; font-size: 0.72rem; }
.timeline { border-left: 2px solid #e5e7eb; margin-left: 8px; padding-left: 14px; }
.timeline-head { margin-bottom: 10px; font-size: 0.85rem; }
.tl-item { margin-bottom: 12px; }
.tl-time { font-size: 0.72rem; color: #9ca3af; margin-bottom: 2px; }
.tl-step { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.tl-status { font-size: 0.7rem; font-weight: 700; padding: 2px 6px; border-radius: 4px; background: #e5e7eb; }
.tl-status.ok { background: #d1fae5; color: #065f46; }
.tl-status.failed { background: #fee2e2; color: #991b1b; }
.tl-status.pending { background: #fef3c7; color: #92400e; }
.tl-cat { font-size: 0.75rem; color: #6b7280; }
.tl-detail { font-size: 0.82rem; color: #374151; margin-top: 4px; white-space: pre-wrap; word-break: break-word; }
.field-label { display: block; font-size: 0.85rem; color: #4b5563; margin: 10px 0 6px; }
.select, .hl7-box { width: 100%; box-sizing: border-box; border: 1px solid #d1d5db; border-radius: 6px; padding: 8px; font-family: ui-monospace, monospace; font-size: 0.82rem; }
.btn { border: 1px solid #d1d5db; background: #fff; border-radius: 6px; padding: 8px 14px; cursor: pointer; }
.btn.primary { background: #2563eb; color: #fff; border-color: #2563eb; }
.btn.primary:disabled { opacity: 0.6; cursor: not-allowed; }
.btn.wide { width: 100%; margin-top: 10px; }
.result-box { margin-top: 12px; background: #0f172a; color: #e2e8f0; padding: 10px; border-radius: 8px; font-size: 0.78rem; overflow: auto; max-height: 200px; }
.hint { margin-top: 12px; font-size: 0.78rem; color: #6b7280; line-height: 1.5; }
.muted { color: #9ca3af; font-size: 0.85rem; }
.alert.error { background: #fef2f2; color: #991b1b; border: 1px solid #fecaca; padding: 10px 12px; border-radius: 8px; margin-bottom: 12px; }
</style>
