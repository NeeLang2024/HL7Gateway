<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted } from 'vue'
import { fetchDashboard, fetchAdtBridgeStatus } from '../api'
import { signalrService } from '../services/signalr'
import { formatDateTime } from '../utils/time'

interface StatCard {
  label: string
  value: number | string
  color: string
}

interface MessageTypeCount {
  type: string
  count: number
}

const stats = ref<StatCard[]>([])
const lastMessage = ref<any>(null)
const typeCounts = ref<MessageTypeCount[]>([])
const liveConnected = ref(false)
const notification = ref('')
const loading = ref(true)
const messageRate = ref(0)
const uptimeHours = ref(0)
const parseFailures24h = ref(0)
const devices = ref<any[]>([])
const avgResponseTimeMs = ref(0)
const bridge = ref<any>(null)

const maxTypeCount = computed(() => Math.max(1, ...typeCounts.value.map(t => Number(t.count) || 0)))

// PIC iX / 桥接连接的动态状态卡片
const picixStatus = computed(() => {
  const b = bridge.value
  if (!b) return { text: '检测中…', sub: '正在查询桥接状态', color: '#9ca3af', dot: '#9ca3af' }
  if (b.enabled === false) {
    return { text: '桥接未启用', sub: '未启用 Philips HIF Bridge', color: '#9ca3af', dot: '#9ca3af' }
  }
  if (!b.reachable) {
    return { text: '桥接离线', sub: '无法连接桥接件，请检查服务', color: '#ef4444', dot: '#ef4444' }
  }
  const subscribed = b.subscriber === true
  if (subscribed) {
    const who = b.name ? `已订阅 · ${b.name}` : '已订阅'
    return { text: 'PIC iX ' + who, sub: '可接收 ADT 消息', color: '#10b981', dot: '#10b981' }
  }
  if (b.name && b.name !== '(null)') {
    return { text: '桥接在线 · 订阅超时', sub: `最近订阅者 · ${b.name}`, color: '#f59e0b', dot: '#f59e0b' }
  }
  return { text: '桥接在线 · 未订阅', sub: 'PIC iX 尚未订阅，暂不可收 ADT', color: '#f59e0b', dot: '#f59e0b' }
})

let refreshTimer: number | undefined
let bridgeTimer: number | undefined
let notifTimer: number | undefined
let dashboardDebounceTimer: number | undefined

function scheduleDashboardRefresh() {
  if (dashboardDebounceTimer) return
  dashboardDebounceTimer = window.setTimeout(() => {
    dashboardDebounceTimer = undefined
    loadDashboard()
  }, 5000)
}

function onBridgeEvent() {
  loadBridge()
  scheduleDashboardRefresh()
}

async function loadDashboard() {
  try {
    const data = await fetchDashboard()
    stats.value = [
      { label: '总消息数', value: data.totalMessages ?? 0, color: '#6366f1' },
      { label: '今日消息', value: data.todayMessages ?? 0, color: '#10b981' },
      { label: '解析失败', value: data.parseFailures ?? 0, color: '#ef4444' },
      { label: '已连设备', value: data.connectedDevices ?? 0, color: '#3b82f6' },
    ]
    if (data.lastMessage) lastMessage.value = data.lastMessage
    typeCounts.value = [...(data.typeCounts ?? data.todayByType ?? [])]
      .sort((a: MessageTypeCount, b: MessageTypeCount) => (b.count || 0) - (a.count || 0))
    messageRate.value = data.messageRate ?? data.messageRatePerMin ?? 0
    uptimeHours.value = data.uptimeHours ?? ((data.systemUptime ?? 0) / 3600)
    parseFailures24h.value = data.parseFailures24h ?? 0
    avgResponseTimeMs.value = data.avgResponseTimeMs ?? data.avgProcessingTimeMs ?? 0
    if (data.devices) devices.value = data.devices
  } catch {
  } finally {
    loading.value = false
  }
}

async function loadBridge() {
  try {
    bridge.value = await fetchAdtBridgeStatus()
  } catch {
    bridge.value = { reachable: false }
  }
}

function showNotification(msg: string) {
  notification.value = msg
  if (notifTimer) clearTimeout(notifTimer)
  notifTimer = window.setTimeout(() => {
    notification.value = ''
  }, 4000)
}

function typeBarWidth(count: number) {
  if (!count) return '0%'
  return `${Math.max(3, Math.min(100, (count / maxTypeCount.value) * 100))}%`
}

function typePercent(count: number) {
  const total = typeCounts.value.reduce((sum, item) => sum + (Number(item.count) || 0), 0)
  if (!total) return '0%'
  return `${Math.round((count / total) * 100)}%`
}

onMounted(async () => {
  await Promise.all([loadDashboard(), loadBridge()])
  refreshTimer = window.setInterval(loadDashboard, 30000)
  bridgeTimer = window.setInterval(loadBridge, 10000)
  signalrService.start()
    .then(() => {
      liveConnected.value = true
      signalrService.onMessageReceived((msg: any) => {
        showNotification(`收到新消息: ${msg?.messageControlId || ''}`)
        onBridgeEvent()
      })
      signalrService.onDeviceConnected((device: any) => {
        showNotification(`设备已连接: ${device?.ip || ''}`)
        onBridgeEvent()
      })
      signalrService.onDeviceDisconnected((device: any) => {
        showNotification(`设备已断开: ${device?.ip || ''}`)
        onBridgeEvent()
      })
    })
    .catch(() => {
      liveConnected.value = false
    })
})

onUnmounted(() => {
  if (refreshTimer) clearInterval(refreshTimer)
  if (bridgeTimer) clearInterval(bridgeTimer)
  if (notifTimer) clearTimeout(notifTimer)
  if (dashboardDebounceTimer) clearTimeout(dashboardDebounceTimer)
  signalrService.off('MessageReceived')
  signalrService.off('DeviceConnected')
  signalrService.off('DeviceDisconnected')
})
</script>

<template>
  <div class="dashboard">
    <div class="page-header">
      <h2>仪表盘</h2>
      <div class="live-indicator" :class="{ connected: liveConnected }">
        <span class="live-dot"></span>
        <span>{{ liveConnected ? '实时连接' : '未连接' }}</span>
      </div>
    </div>

    <div v-if="notification" class="notification">{{ notification }}</div>

    <div v-if="loading" class="loading">加载中...</div>

    <template v-else>
      <div class="stat-cards">
        <div
          v-for="card in stats"
          :key="card.label"
          class="stat-card"
          :style="{ borderTopColor: card.color }"
        >
          <div class="stat-value" :style="{ color: card.color }">{{ card.value }}</div>
          <div class="stat-label">{{ card.label }}</div>
        </div>

        <div class="stat-card picix-card" :style="{ borderTopColor: picixStatus.color }">
          <div class="picix-head">
            <span class="picix-dot" :style="{ background: picixStatus.dot }"></span>
            <span class="picix-text" :style="{ color: picixStatus.color }">{{ picixStatus.text }}</span>
          </div>
          <div class="stat-label">{{ picixStatus.sub }}</div>
        </div>
      </div>

      <div class="metric-row">
        <div class="metric-card">
          <span class="metric-val">{{ messageRate.toFixed(1) }}</span>
          <span class="metric-lbl">条/分 (速率)</span>
        </div>
        <div class="metric-card">
          <span class="metric-val">{{ uptimeHours.toFixed(1) }}</span>
          <span class="metric-lbl">小时 (运行时间)</span>
        </div>
        <div class="metric-card">
          <span class="metric-val" :class="{ warn: parseFailures24h > 0 }">{{ parseFailures24h }}</span>
          <span class="metric-lbl">24h 解析失败</span>
        </div>
        <div class="metric-card">
          <span class="metric-val">{{ avgResponseTimeMs.toFixed(0) }}</span>
          <span class="metric-lbl">ms (平均响应)</span>
        </div>
      </div>

      <div class="dashboard-row">
        <div class="card last-message-card">
          <h3 class="card-title">最近消息</h3>
          <div v-if="lastMessage" class="last-msg-content">
            <div class="msg-field">
              <span class="field-label">控制 ID:</span>
              <span>{{ lastMessage.messageControlId }}</span>
            </div>
            <div class="msg-field">
              <span class="field-label">类型:</span>
              <span>{{ lastMessage.messageType }}</span>
            </div>
            <div class="msg-field">
              <span class="field-label">患者:</span>
              <span>{{ lastMessage.patientId }}</span>
            </div>
            <div class="msg-field">
              <span class="field-label">来源:</span>
              <span>{{ lastMessage.sourceIp }}</span>
            </div>
            <div class="msg-field">
              <span class="field-label">时间:</span>
              <span>{{ formatDateTime(lastMessage.receivedAt) }}</span>
            </div>
          </div>
          <div v-else class="no-data">暂无消息</div>
        </div>

        <div class="card type-chart-card">
          <h3 class="card-title">今日消息类型分布</h3>
          <div v-if="typeCounts.length" class="type-chart">
            <div class="type-bar-row" v-for="t in typeCounts" :key="t.type">
              <span class="type-label">{{ t.type }}</span>
              <div class="type-bar-track">
                <div
                  class="type-bar-fill"
                  :style="{ width: typeBarWidth(t.count) }"
                ></div>
              </div>
              <span class="type-count">{{ t.count }}</span>
              <span class="type-percent">{{ typePercent(t.count) }}</span>
            </div>
          </div>
          <div v-else class="no-data">暂无数据</div>
        </div>
      </div>

      <div class="card" v-if="devices.length">
        <h3 class="card-title">已连设备</h3>
        <div class="device-mini-list">
          <div class="device-item" v-for="d in devices" :key="d.sourceIp">
            <span class="device-ip">{{ d.sourceIp }}:{{ d.sourcePort }}</span>
            <span class="device-since">{{ formatDateTime(d.connectedAt) }}</span>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.dashboard { max-width: 1200px; }
.page-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 24px; }
.page-header h2 { font-size: 22px; color: var(--text-primary); }
.live-indicator { display: flex; align-items: center; gap: 6px; font-size: 13px; color: var(--text-muted); }
.live-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--text-muted); display: inline-block; }
.live-indicator.connected .live-dot { background: var(--success); box-shadow: 0 0 6px var(--success); }
.live-indicator.connected { color: var(--success); }
.notification { background: var(--info-bg); color: #1e40af; padding: 10px 16px; border-radius: var(--radius); margin-bottom: 16px; font-size: 13px; border: 1px solid #bfdbfe; }
.loading { text-align: center; padding: 60px 0; color: var(--text-muted); font-size: 15px; }
.stat-cards { display: grid; grid-template-columns: repeat(5, 1fr); gap: 16px; margin-bottom: 16px; }
.stat-card { background: var(--card-bg); border-radius: var(--radius); padding: 20px; box-shadow: var(--shadow); border-top: 3px solid; }
.stat-value { font-size: 32px; font-weight: 700; margin-bottom: 4px; }
.stat-label { font-size: 13px; color: var(--text-secondary); }
.picix-card { display: flex; flex-direction: column; justify-content: center; }
.picix-head { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; }
.picix-dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; box-shadow: 0 0 6px currentColor; }
.picix-text { font-size: 16px; font-weight: 700; line-height: 1.25; }
.metric-row { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; margin-bottom: 24px; }
.metric-card { background: var(--card-bg); border-radius: var(--radius); padding: 16px; box-shadow: var(--shadow); display: flex; flex-direction: column; align-items: center; }
.metric-val { font-size: 24px; font-weight: 700; color: var(--accent); }
.metric-val.warn { color: #ef4444; }
.metric-lbl { font-size: 12px; color: var(--text-muted); margin-top: 4px; }
.dashboard-row { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 24px; }
.card { background: var(--card-bg); border-radius: var(--radius); padding: 20px; box-shadow: var(--shadow); }
.card-title { font-size: 15px; font-weight: 600; margin-bottom: 16px; padding-bottom: 12px; border-bottom: 1px solid var(--border-color); color: var(--text-primary); }
.last-msg-content { display: flex; flex-direction: column; gap: 8px; }
.msg-field { font-size: 13px; display: flex; gap: 8px; }
.field-label { color: var(--text-secondary); min-width: 60px; }
.no-data { color: var(--text-muted); font-size: 13px; text-align: center; padding: 24px 0; }
.type-chart { display: flex; flex-direction: column; gap: 10px; overflow: hidden; }
.type-bar-row { display: grid; grid-template-columns: 64px minmax(0, 1fr) 54px 48px; align-items: center; gap: 8px; }
.type-label { font-size: 13px; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.type-bar-track { min-width: 0; height: 20px; background: #f3f4f6; border-radius: 4px; overflow: hidden; }
.type-bar-fill { height: 100%; background: var(--accent); border-radius: 4px; min-width: 4px; transition: width 0.3s; }
.type-count { font-size: 13px; font-weight: 600; text-align: right; color: var(--text-primary); font-variant-numeric: tabular-nums; }
.type-percent { font-size: 12px; text-align: right; color: var(--text-secondary); font-variant-numeric: tabular-nums; }
.device-mini-list { display: flex; flex-direction: column; gap: 8px; }
.device-item { display: flex; justify-content: space-between; font-size: 13px; padding: 6px 0; border-bottom: 1px solid var(--border-color); }
.device-item:last-child { border-bottom: none; }
.device-ip { color: var(--text-primary); font-family: monospace; }
.device-since { color: var(--text-secondary); }
</style>
