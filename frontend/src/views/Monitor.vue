<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { fetchMonitor } from '../api'

const data = ref<any>(null)
const loading = ref(true)
let timer: number | undefined

async function load() {
  try {
    data.value = await fetchMonitor()
  } catch { /* ignore */ }
  loading.value = false
}

onMounted(() => {
  load()
  timer = window.setInterval(load, 30000)
})

onUnmounted(() => {
  if (timer) clearInterval(timer)
})
</script>

<template>
  <div class="monitor-page">
    <h2 class="page-title">系统监控</h2>

    <div v-if="loading" class="loading">加载中...</div>

    <template v-else-if="data">
      <div class="monitor-grid">
        <div class="card">
          <div class="metric-label">CPU 使用率</div>
          <div class="metric-value">{{ data.cpuPercent?.toFixed(1) ?? 'N/A' }}%</div>
        </div>
        <div class="card">
          <div class="metric-label">内存使用率</div>
          <div class="metric-value">{{ data.memoryPercent?.toFixed(1) ?? 'N/A' }}%</div>
        </div>
        <div class="card">
          <div class="metric-label">已用内存</div>
          <div class="metric-value">{{ data.usedMemoryMB?.toFixed(0) ?? 'N/A' }} MB</div>
        </div>
        <div class="card">
          <div class="metric-label">可用内存</div>
          <div class="metric-value">{{ data.availableMemoryMB?.toFixed(0) ?? 'N/A' }} MB</div>
        </div>
        <div class="card" v-if="data.totalMemoryMB">
          <div class="metric-label">总内存</div>
          <div class="metric-value">{{ data.totalMemoryMB?.toFixed(0) }} MB</div>
        </div>
        <div class="card">
          <div class="metric-label">运行时间</div>
          <div class="metric-value">{{ data.uptime }} 小时</div>
        </div>
        <div class="card">
          <div class="metric-label">进程名</div>
          <div class="metric-value small">{{ data.processName }}</div>
        </div>
        <div class="card">
          <div class="metric-label">进程 ID</div>
          <div class="metric-value small">{{ data.processId }}</div>
        </div>
        <div class="card">
          <div class="metric-label">5 分钟消息</div>
          <div class="metric-value">{{ data.messages?.last5Minutes ?? 0 }}</div>
        </div>
        <div class="card">
          <div class="metric-label">1 小时消息</div>
          <div class="metric-value">{{ data.messages?.last1Hour ?? 0 }}</div>
        </div>
        <div class="card">
          <div class="metric-label">24 小时消息</div>
          <div class="metric-value">{{ data.messages?.last24Hours ?? 0 }}</div>
        </div>
        <div class="card">
          <div class="metric-label">已连设备</div>
          <div class="metric-value">{{ data.connectedDevices ?? 0 }}</div>
        </div>
      </div>
      <div v-if="data.serviceStatuses?.length" class="service-list">
        <h3>服务状态</h3>
        <div v-for="svc in data.serviceStatuses" :key="svc.name" class="service-row">
          <span>{{ svc.name }}</span>
          <strong :class="{ ok: svc.status === 'Running', bad: svc.status !== 'Running' }">{{ svc.status }}</strong>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.monitor-page { max-width: 1000px; }
.page-title { font-size: 22px; color: var(--text-primary); margin-bottom: 24px; }
.loading { text-align: center; padding: 60px 0; color: var(--text-muted); }
.monitor-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; }
.card { background: var(--card-bg); border-radius: var(--radius); padding: 20px; box-shadow: var(--shadow); }
.metric-label { font-size: 13px; color: var(--text-secondary); margin-bottom: 8px; }
.metric-value { font-size: 28px; font-weight: 700; color: var(--accent); }
.metric-value.small { font-size: 16px; }
.service-list { margin-top: 16px; background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); padding: 18px 20px; }
.service-list h3 { font-size: 15px; margin-bottom: 12px; }
.service-row { display: flex; justify-content: space-between; padding: 8px 0; border-bottom: 1px solid var(--border-color); font-size: 13px; }
.service-row:last-child { border-bottom: 0; }
.service-row .ok { color: var(--success); }
.service-row .bad { color: var(--error); }
</style>
