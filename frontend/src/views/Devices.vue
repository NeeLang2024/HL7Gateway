<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { fetchDevices, fetchDeviceStats } from '../api'
import { signalrService } from '../services/signalr'
import { formatDateTime } from '../utils/time'

const devices = ref<any[]>([])
const loading = ref(true)
const statsLoading = ref(false)
const deviceStats = ref<any>(null)
const selectedSourceIp = ref<string | undefined>(undefined)
const statsHours = ref(24)

async function loadDevices() {
  try {
    const data = await fetchDevices()
    devices.value = Array.isArray(data) ? data : data.items || data.records || data.data || []
    if (devices.value.length && !selectedSourceIp.value) {
      selectedSourceIp.value = devices.value[0].ip
    }
  } catch {
    devices.value = []
  } finally {
    loading.value = false
  }
}

async function loadStats() {
  statsLoading.value = true
  try {
    const data = await fetchDeviceStats(selectedSourceIp.value, statsHours.value)
    deviceStats.value = data
  } catch {
    deviceStats.value = null
  } finally {
    statsLoading.value = false
  }
}

function getStatusColor(lastActivity: string): string {
  if (!lastActivity) return 'var(--gray)'
  const now = Date.now()
  const activity = new Date(lastActivity).getTime()
  const diff = now - activity
  if (diff < 60000) return 'var(--success)'
  if (diff < 300000) return 'var(--warning)'
  return 'var(--gray)'
}

function getStatusLabel(lastActivity: string): string {
  if (!lastActivity) return '未知'
  const now = Date.now()
  const activity = new Date(lastActivity).getTime()
  const diff = now - activity
  if (diff < 60000) return '在线'
  if (diff < 300000) return '空闲'
  return '离线'
}

onMounted(async () => {
  await loadDevices()
  await loadStats()
  await signalrService.start()
  signalrService.onDeviceConnected(() => loadDevices())
  signalrService.onDeviceDisconnected(() => loadDevices())
})

onUnmounted(() => {
  signalrService.off('DeviceConnected')
  signalrService.off('DeviceDisconnected')
})
</script>

<template>
  <div class="devices-page">
    <div class="page-header">
      <h2>设备连接</h2>
    </div>

    <div v-if="loading" class="loading">加载中...</div>

    <template v-else>
      <!-- Device cards -->
      <div v-if="!devices.length" class="empty">暂无设备连接</div>

      <div v-else class="device-grid">
        <div v-for="dev in devices" :key="dev.id || dev.deviceId" class="device-card">
          <div class="device-header">
            <span class="device-name">{{ dev.name || dev.deviceName || dev.ip }}</span>
            <span class="status-dot" :style="{ background: getStatusColor(dev.lastActivity) }" :title="getStatusLabel(dev.lastActivity)"></span>
          </div>
          <div class="device-body">
            <div class="device-field"><span class="field-label">IP 地址</span><span>{{ dev.ip }}</span></div>
            <div class="device-field"><span class="field-label">端口</span><span>{{ dev.port }}</span></div>
            <div class="device-field"><span class="field-label">消息数</span><span>{{ dev.messageCount ?? dev.messagecount ?? 0 }}</span></div>
            <div class="device-field"><span class="field-label">最后活动</span><span>{{ formatDateTime(dev.lastActivity) }}</span></div>
            <div class="device-field">
              <span class="field-label">状态</span>
              <span :class="['status-badge', getStatusLabel(dev.lastActivity) === '在线' ? 'status-online' : getStatusLabel(dev.lastActivity) === '空闲' ? 'status-idle' : 'status-offline']">
                {{ getStatusLabel(dev.lastActivity) }}
              </span>
            </div>
          </div>
        </div>
      </div>

      <!-- Stats section -->
      <div class="stats-section">
        <div class="stats-header">
          <h3>设备统计</h3>
          <div class="stats-controls">
            <select v-model="selectedSourceIp" @change="loadStats" v-if="devices.length">
              <option v-for="dev in devices" :key="dev.ip || dev.deviceId || dev.id" :value="dev.ip">
                {{ dev.name || dev.deviceName || dev.ip }}
              </option>
            </select>
            <select v-model.number="statsHours" @change="loadStats">
              <option :value="1">最近 1 小时</option>
              <option :value="6">最近 6 小时</option>
              <option :value="12">最近 12 小时</option>
              <option :value="24">最近 24 小时</option>
              <option :value="48">最近 48 小时</option>
              <option :value="72">最近 72 小时</option>
            </select>
          </div>
        </div>
        <div v-if="statsLoading" class="loading">统计加载中...</div>
        <div v-else-if="deviceStats" class="stats-grid">
          <div class="stat-card">
            <div class="stat-value" style="color: #6366f1">{{ deviceStats.totalMessages }}</div>
            <div class="stat-label">总消息</div>
          </div>
          <div class="stat-card">
            <div class="stat-value" style="color: #10b981">{{ deviceStats.successMessages }}</div>
            <div class="stat-label">成功</div>
          </div>
          <div class="stat-card">
            <div class="stat-value" style="color: #ef4444">{{ deviceStats.failedMessages }}</div>
            <div class="stat-label">失败</div>
          </div>
          <div class="stat-card">
            <div class="stat-value" style="color: #f59e0b">{{ deviceStats.totalObservations }}</div>
            <div class="stat-label">观察值</div>
          </div>
        </div>
        <div v-else class="empty">暂无统计数据</div>

        <div v-if="deviceStats?.hourlyMessages?.length" class="hourly-chart">
          <h4>每时消息量</h4>
          <div class="bar-chart">
            <div class="bar-row" v-for="h in deviceStats.hourlyMessages" :key="h.date + h.hour">
              <span class="bar-label">{{ h.date?.slice(5) }} {{ h.hour }}</span>
              <div class="bar-track">
                <div class="bar-fill" :style="{ width: Math.min(100, (h.count / Math.max(...deviceStats.hourlyMessages.map((x: any) => x.count)) * 100)) + '%' }"></div>
              </div>
              <span class="bar-count">{{ h.count }}</span>
            </div>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.devices-page { max-width: 1200px; }
.page-header { margin-bottom: 20px; }
.page-header h2 { font-size: 22px; }
.device-grid {
  display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 16px; margin-bottom: 24px;
}
.device-card {
  background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); overflow: hidden;
  transition: box-shadow 0.2s;
}
.device-card:hover { box-shadow: var(--shadow-md); }
.device-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 14px 16px; background: #f9fafb; border-bottom: 1px solid var(--border-color);
}
.device-name { font-weight: 600; font-size: 14px; color: var(--text-primary); }
.status-dot { width: 10px; height: 10px; border-radius: 50%; display: inline-block; }
.device-body { padding: 14px 16px; display: flex; flex-direction: column; gap: 8px; }
.device-field { display: flex; justify-content: space-between; font-size: 13px; }
.field-label { color: var(--text-secondary); }
.status-badge { display: inline-block; padding: 2px 10px; border-radius: 12px; font-size: 12px; font-weight: 500; }
.status-online { background: var(--success-bg); color: #065f46; }
.status-idle { background: var(--warning-bg); color: #92400e; }
.status-offline { background: var(--gray-bg); color: #4b5563; }
.stats-section {
  background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); padding: 20px;
}
.stats-header {
  display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px;
}
.stats-header h3 { margin: 0; font-size: 16px; }
.stats-controls { display: flex; gap: 8px; }
.stats-controls select { padding: 4px 8px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 13px; }
.stats-grid {
  display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; margin-bottom: 20px;
}
.stat-card {
  background: #f9fafb; border-radius: var(--radius-sm); padding: 16px; text-align: center;
}
.stat-value { font-size: 28px; font-weight: 700; }
.stat-label { font-size: 13px; color: var(--text-secondary); margin-top: 4px; }
.hourly-chart h4 { font-size: 14px; margin: 0 0 12px; }
.bar-chart { display: flex; flex-direction: column; gap: 6px; }
.bar-row { display: flex; align-items: center; gap: 8px; }
.bar-label { font-size: 12px; min-width: 70px; color: var(--text-secondary); }
.bar-track { flex: 1; height: 18px; background: #f3f4f6; border-radius: 4px; overflow: hidden; }
.bar-fill { height: 100%; background: #6366f1; border-radius: 4px; min-width: 4px; transition: width 0.3s; }
.bar-count { font-size: 12px; font-weight: 600; min-width: 24px; text-align: right; }
.loading { text-align: center; padding: 40px; color: var(--text-muted); }
.empty { text-align: center; padding: 40px; color: var(--text-muted); }
</style>
