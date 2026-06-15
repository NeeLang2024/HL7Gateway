<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { fetchSystemLogs, clearSystemLogs } from '../api'
import { formatDateTime } from '../utils/time'

const LOG_LEVELS = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical', 'None']

function levelName(level: number): string {
  return LOG_LEVELS[level] ?? `Unknown(${level})`
}

function levelBadge(level: number): string {
  const map: Record<string, string> = {
    'Warning': 'badge-warning',
    'Error': 'badge-error',
    'Critical': 'badge-error',
    'Information': 'badge-info',
  }
  return map[levelName(level)] || 'badge-gray'
}

const items = ref<any[]>([])
const totalCount = ref(0)
const loading = ref(false)
const filterLevel = ref('')
const filterCategory = ref('')
const filterKeyword = ref('')
const filterDateFrom = ref('')
const filterDateTo = ref('')
const page = ref(1)
const pageSize = ref(30)
const clearing = ref(false)
const categories = ref<string[]>([])

async function loadLogs() {
  loading.value = true
  try {
    const params: Record<string, string | number | undefined> = {
      page: page.value,
      pageSize: pageSize.value,
      level: filterLevel.value ? String(LOG_LEVELS.indexOf(filterLevel.value)) : undefined,
      category: filterCategory.value || undefined,
      keyword: filterKeyword.value || undefined,
      from: filterDateFrom.value || undefined,
      to: filterDateTo.value || undefined,
    }
    const data = await fetchSystemLogs(params)
    items.value = data.items || []
    totalCount.value = data.total ?? 0

    const cats = new Set<string>()
    for (const item of items.value) {
      if (item.category) cats.add(item.category)
    }
    categories.value = Array.from(cats)
  } catch {
    items.value = []
  } finally {
    loading.value = false
  }
}

function search() {
  page.value = 1
  loadLogs()
}

function reset() {
  filterLevel.value = ''
  filterCategory.value = ''
  filterKeyword.value = ''
  filterDateFrom.value = ''
  filterDateTo.value = ''
  page.value = 1
  loadLogs()
}

async function doClear() {
  if (!confirm('确定要清除日志吗？')) return
  clearing.value = true
  try {
    await clearSystemLogs(filterDateFrom.value || undefined)
    await loadLogs()
  } catch (err: any) {
    alert(`清除失败: ${err.message}`)
  } finally {
    clearing.value = false
  }
}

const totalPages = () => Math.max(1, Math.ceil(totalCount.value / pageSize.value))

onMounted(loadLogs)
</script>

<template>
  <div class="logs-page">
    <div class="page-header">
      <h2>系统日志</h2>
    </div>

    <div class="filter-bar">
      <div class="filter-item">
        <label>级别</label>
        <select v-model="filterLevel">
          <option value="">全部</option>
          <option v-for="l in LOG_LEVELS.slice(2)" :key="l" :value="l">{{ l }}</option>
        </select>
      </div>
      <div class="filter-item">
        <label>类别</label>
        <select v-model="filterCategory">
          <option value="">全部</option>
          <option v-for="c in categories" :key="c" :value="c">{{ c }}</option>
        </select>
      </div>
      <div class="filter-item filter-keyword">
        <label>关键词</label>
        <input
          v-model="filterKeyword"
          placeholder="Subscribe / SOAP Fault / callbackAction"
          @keyup.enter="search"
        />
      </div>
      <div class="filter-item">
        <label>开始</label>
        <input type="datetime-local" v-model="filterDateFrom" />
      </div>
      <div class="filter-item">
        <label>结束</label>
        <input type="datetime-local" v-model="filterDateTo" />
      </div>
      <div class="filter-actions">
        <button class="btn btn-primary" @click="search">查询</button>
        <button class="btn btn-secondary" @click="reset">重置</button>
        <button class="btn btn-danger" :disabled="clearing" @click="doClear">
          {{ clearing ? '清除中...' : '清除日志' }}
        </button>
      </div>
    </div>

    <div class="table-container">
      <table v-if="items.length">
        <thead>
          <tr>
            <th>时间</th>
            <th>级别</th>
            <th>类别</th>
            <th>消息</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="log in items" :key="log.logId">
            <td class="time-col">{{ formatDateTime(log.createdAt) }}</td>
            <td>
              <span :class="['badge', levelBadge(log.level)]">{{ levelName(log.level) }}</span>
            </td>
            <td class="cat-col">{{ log.category || '-' }}</td>
            <td class="msg-col">{{ log.message || '-' }}</td>
          </tr>
        </tbody>
      </table>
      <div v-else-if="!loading" class="empty">暂无日志</div>
      <div v-if="loading" class="loading">加载中...</div>
    </div>

    <div class="pagination" v-if="totalCount > pageSize">
      <button :disabled="page <= 1" @click="page--; loadLogs()">上一页</button>
      <span>第 {{ page }} / {{ totalPages() }} 页 (共 {{ totalCount }} 条)</span>
      <button :disabled="page >= totalPages()" @click="page++; loadLogs()">下一页</button>
    </div>
  </div>
</template>

<style scoped>
.logs-page {
  max-width: 1200px;
}

.page-header {
  margin-bottom: 20px;
}

.page-header h2 {
  font-size: 22px;
}

.filter-bar {
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

.filter-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.filter-item label {
  font-size: 12px;
  color: var(--text-secondary);
  font-weight: 500;
}

.filter-item input,
.filter-item select {
  padding: 6px 10px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  font-size: 13px;
  width: 180px;
  outline: none;
}

.filter-item input:focus,
.filter-item select:focus {
  border-color: var(--accent);
}

.filter-keyword input {
  width: 260px;
}

.filter-actions {
  display: flex;
  gap: 8px;
  align-items: flex-end;
  padding-bottom: 1px;
}

.table-container {
  background: var(--card-bg);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  overflow-x: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
}

th {
  text-align: left;
  padding: 12px 14px;
  font-size: 12px;
  font-weight: 600;
  color: var(--text-secondary);
  background: #f9fafb;
  border-bottom: 1px solid var(--border-color);
  white-space: nowrap;
}

td {
  padding: 10px 14px;
  font-size: 13px;
  border-bottom: 1px solid var(--border-color);
}

tbody tr:nth-child(even) {
  background: #f9fafb;
}

.time-col {
  white-space: nowrap;
  font-family: var(--font-mono);
  font-size: 12px;
}

.cat-col {
  max-width: 260px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.msg-col {
  max-width: 500px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.badge {
  display: inline-block;
  padding: 2px 10px;
  border-radius: 12px;
  font-size: 12px;
  font-weight: 500;
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

.badge-gray {
  background: var(--gray-bg);
  color: #4b5563;
}

.btn {
  padding: 6px 14px;
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

.btn-danger {
  background: var(--error-bg);
  color: #991b1b;
}

.btn-danger:hover:not(:disabled) {
  background: #fecaca;
}

.pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  margin-top: 16px;
  padding: 12px 0;
  font-size: 13px;
  color: var(--text-secondary);
}

.pagination button {
  padding: 6px 14px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  background: var(--card-bg);
  cursor: pointer;
  font-size: 13px;
}

.pagination button:disabled {
  opacity: 0.4;
  cursor: not-allowed;
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
</style>
