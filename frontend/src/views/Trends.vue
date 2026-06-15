<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { fetchVitalSignTrends } from '../api'

interface Point {
  t: string
  valueNumeric?: number | null
}
interface TrendData {
  type: string
  vitalSignName: string
  units: string
  patientId: string
  points: Point[]
}

const loading = ref(false)
const error = ref('')
const patientId = ref('')
const vitalTypes = ['HR', 'SPO2', 'RESP', 'NIBP_SYS', 'NIBP_DIA', 'NIBP_MEAN', 'TEMP', 'PULSE', 'ETCO2', 'IBP_SYS', 'IBP_DIA', 'IBP_MEAN']
const selectedType = ref('HR')
const trends = ref<TrendData | null>(null)
const hours = ref(24)
const chartSvg = ref('')

function buildSvg(points: Point[], name: string, units: string): string {
  if (!points.length) return '<text x="10" y="20" fill="#999">暂无数据</text>'
  const w = 700, h = 250, pad = 40
  const values = points.map(p => p.valueNumeric ?? 0)
  const min = Math.min(...values)
  const max = Math.max(...values)
  const range = max - min || 1
  const padRange = range * 0.1
  const yMin = min - padRange
  const yMax = max + padRange
  const yRange = yMax - yMin || 1

  let path = ''
  for (let i = 0; i < points.length; i++) {
    const x = pad + (i / (points.length - 1 || 1)) * (w - 2 * pad)
    const y = h - pad - ((values[i] - yMin) / yRange) * (h - 2 * pad)
    path += (i === 0 ? 'M' : 'L') + x.toFixed(1) + ',' + y.toFixed(1)
  }

  const ySteps = 5
  let gridLines = ''
  let yLabels = ''
  for (let i = 0; i <= ySteps; i++) {
    const y = pad + (i / ySteps) * (h - 2 * pad)
    const val = yMax - (i / ySteps) * yRange
    gridLines += `<line x1="${pad}" y1="${y}" x2="${w - pad}" y2="${y}" stroke="#e5e7eb" stroke-width="1"/>`
    yLabels += `<text x="${pad - 5}" y="${y + 4}" text-anchor="end" fill="#6b7280" font-size="11">${val.toFixed(val % 1 < 0.01 ? 0 : 1)}</text>`
  }

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${w} ${h}" width="100%" height="${h}">
    <rect width="${w}" height="${h}" fill="#fff"/>
    ${gridLines}
    <path d="${path}" fill="none" stroke="#6366f1" stroke-width="2" stroke-linejoin="round"/>
    ${yLabels}
    <text x="${w / 2}" y="${h - 5}" text-anchor="middle" fill="#6b7280" font-size="11">${name} (${units}) · ${points.length} 数据点</text>
  </svg>`
}

async function loadTrends() {
  if (!patientId.value || !selectedType.value) {
    error.value = '请输入患者 ID 并选择体征类型'
    return
  }
  loading.value = true
  error.value = ''
  try {
    const from = new Date(Date.now() - hours.value * 3600000).toISOString()
    const data = await fetchVitalSignTrends(patientId.value, selectedType.value, from)
    trends.value = data
    if (data.points && data.points.length) {
      chartSvg.value = buildSvg(data.points, data.vitalSignName || data.type, data.units || '')
    } else {
      chartSvg.value = '<text x="10" y="20" fill="#999">该时间段内无数据</text>'
    }
  } catch (err: any) {
    error.value = '加载趋势失败: ' + (err?.message || '未知错误')
    trends.value = null
    chartSvg.value = ''
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  const params = new URLSearchParams(window.location.hash.split('?')[1] || '')
  if (params.get('patientId')) {
    patientId.value = params.get('patientId') || ''
    if (params.get('type')) selectedType.value = params.get('type') || 'HR'
    loadTrends()
  }
})
</script>

<template>
  <div class="trends-page">
    <div class="page-header">
      <h2>体征趋势图</h2>
    </div>
    <div class="controls">
      <div class="field">
        <label>患者 ID</label>
        <input v-model="patientId" placeholder="患者 ID" @keyup.enter="loadTrends" />
      </div>
      <div class="field">
        <label>体征类型</label>
        <select v-model="selectedType">
          <option v-for="t in vitalTypes" :key="t" :value="t">{{ t }}</option>
        </select>
      </div>
      <div class="field">
        <label>时间范围</label>
        <select v-model.number="hours">
          <option :value="1">最近 1 小时</option>
          <option :value="6">最近 6 小时</option>
          <option :value="12">最近 12 小时</option>
          <option :value="24">最近 24 小时</option>
          <option :value="48">最近 48 小时</option>
          <option :value="72">最近 72 小时</option>
        </select>
      </div>
      <div class="field-actions">
        <button class="btn btn-primary" @click="loadTrends" :disabled="loading">{{ loading ? '加载中...' : '查询' }}</button>
      </div>
    </div>
    <div v-if="error" class="error-msg">{{ error }}</div>
    <div class="chart-container" v-if="chartSvg" v-html="chartSvg"></div>
    <div v-else-if="!loading && !error" class="empty">请输入患者 ID 并点击查询</div>
  </div>
</template>

<style scoped>
.trends-page { max-width: 900px; }
.page-header { margin-bottom: 20px; }
.page-header h2 { font-size: 22px; }
.controls {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  background: var(--card-bg);
  padding: 16px;
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  margin-bottom: 16px;
  align-items: flex-end;
}
.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.field label {
  font-size: 12px;
  color: var(--text-secondary);
  font-weight: 500;
}
.field input, .field select {
  padding: 6px 10px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  font-size: 13px;
  outline: none;
  min-width: 120px;
}
.field input:focus, .field select:focus { border-color: var(--accent); }
.field-actions { display: flex; align-items: flex-end; padding-bottom: 1px; }
.btn {
  padding: 6px 14px;
  border: none;
  border-radius: var(--radius-sm);
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
}
.btn:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-primary { background: var(--accent); color: white; }
.btn-primary:hover:not(:disabled) { background: var(--accent-hover); }
.error-msg {
  background: var(--error-bg);
  color: #991b1b;
  padding: 10px 14px;
  border-radius: var(--radius-sm);
  font-size: 13px;
  margin-bottom: 12px;
}
.chart-container {
  background: var(--card-bg);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  padding: 16px;
  overflow-x: auto;
}
.empty {
  text-align: center;
  padding: 60px;
  color: var(--text-muted);
}
</style>
