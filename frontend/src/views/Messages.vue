<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { fetchMessages, deleteMessage, exportMessages } from '../api'
import { formatDateTime } from '../utils/time'

const router = useRouter()

const messages = ref<any[]>([])
const totalCount = ref(0)
const loading = ref(false)
const filterPatientId = ref('')
const filterMessageType = ref('')
const filterSourceIp = ref('')
const filterDateFrom = ref('')
const filterDateTo = ref('')
const page = ref(1)
const pageSize = ref(20)
const deletingId = ref<string | number | null>(null)

function parseStatusText(status: number): string {
  if (status === 0) return '待解析'
  if (status === 1) return '已解析'
  if (status === 2) return '解析失败'
  return '未知'
}
function parseStatusClass(status: number): string {
  if (status === 1) return 'badge-success'
  if (status === 2) return 'badge-error'
  return 'badge-warning'
}

async function loadMessages() {
  loading.value = true
  try {
    const params: Record<string, string | number | undefined> = {
      page: page.value,
      pageSize: pageSize.value,
      patientId: filterPatientId.value || undefined,
      messageType: filterMessageType.value || undefined,
      sourceIp: filterSourceIp.value || undefined,
      dateFrom: filterDateFrom.value || undefined,
      dateTo: filterDateTo.value || undefined,
    }
    const data = await fetchMessages(params)
    messages.value = data.items || data.records || data.data || data || []
    totalCount.value = data.totalCount ?? data.total ?? data.length ?? 0
  } catch {
    messages.value = []
  } finally {
    loading.value = false
  }
}

function confirmDelete(id: number) {
  if (confirm('确定要删除此消息吗？')) {
    deletingId.value = id
    deleteMessage(id)
      .then(() => loadMessages())
      .finally(() => { deletingId.value = null })
  }
}

function goDetail(id: number) {
  router.push(`/messages/${id}`)
}

function search() {
  page.value = 1
  loadMessages()
}

function doExport() {
  const params: Record<string, string | undefined> = {}
  if (filterDateFrom.value) params.from = new Date(filterDateFrom.value).toISOString()
  if (filterDateTo.value) params.to = new Date(filterDateTo.value + 'T23:59:59').toISOString()
  window.open(exportMessages(params.from, params.to), '_blank')
}

const totalPages = () => Math.max(1, Math.ceil(totalCount.value / pageSize.value))

onMounted(loadMessages)
</script>

<template>
  <div class="messages-page">
    <div class="page-header">
      <h2>消息列表</h2>
    </div>

    <div class="filter-bar">
      <div class="filter-item">
        <label>患者 ID</label>
        <input v-model="filterPatientId" placeholder="患者 ID" @keyup.enter="search" />
      </div>
      <div class="filter-item">
        <label>消息类型</label>
        <input v-model="filterMessageType" placeholder="如 ADT^A01" @keyup.enter="search" />
      </div>
      <div class="filter-item">
        <label>来源 IP</label>
        <input v-model="filterSourceIp" placeholder="来源 IP" @keyup.enter="search" />
      </div>
      <div class="filter-item">
        <label>开始日期</label>
        <input type="date" v-model="filterDateFrom" />
      </div>
      <div class="filter-item">
        <label>结束日期</label>
        <input type="date" v-model="filterDateTo" />
      </div>
      <div class="filter-actions">
        <button class="btn btn-primary" @click="search">查询</button>
        <button class="btn btn-secondary" @click="() => { filterPatientId = ''; filterMessageType = ''; filterSourceIp = ''; filterDateFrom = ''; filterDateTo = ''; search() }">重置</button>
        <button class="btn btn-info" @click="doExport">导出 CSV</button>
      </div>
    </div>

    <div class="table-container">
      <table v-if="messages.length">
        <thead>
          <tr>
            <th>控制 ID</th>
            <th>类型</th>
            <th>患者 ID</th>
            <th>床位</th>
            <th>来源 IP</th>
            <th>解析状态</th>
            <th>接收时间</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="msg in messages" :key="msg.messageId" @click="goDetail(msg.messageId)" class="clickable-row">
            <td>{{ msg.messageControlId }}</td>
            <td>{{ msg.messageType }}</td>
            <td>{{ msg.patientId || '—' }}</td>
            <td>{{ msg.patientLocation || '—' }}</td>
            <td>{{ msg.sourceIp }}</td>
            <td>
              <span :class="['badge', parseStatusClass(msg.parseStatus)]">
                {{ parseStatusText(msg.parseStatus) }}
              </span>
            </td>
            <td>{{ formatDateTime(msg.receivedAt) }}</td>
            <td @click.stop>
              <button class="btn btn-sm btn-danger" :disabled="deletingId === msg.messageId" @click="confirmDelete(msg.messageId)">
                {{ deletingId === msg.messageId ? '...' : '删除' }}
              </button>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-else-if="!loading" class="empty">暂无消息</div>
      <div v-if="loading" class="loading">加载中...</div>
    </div>

    <div class="pagination" v-if="totalCount > pageSize">
      <button :disabled="page <= 1" @click="page--; loadMessages()">上一页</button>
      <span>第 {{ page }} / {{ totalPages() }} 页 (共 {{ totalCount }} 条)</span>
      <button :disabled="page >= totalPages()" @click="page++; loadMessages()">下一页</button>
    </div>
  </div>
</template>

<style scoped>
.messages-page { max-width: 1200px; }
.page-header { margin-bottom: 20px; }
.page-header h2 { font-size: 22px; }
.filter-bar {
  display: flex; flex-wrap: wrap; gap: 12px;
  background: var(--card-bg); padding: 16px; border-radius: var(--radius);
  box-shadow: var(--shadow); margin-bottom: 16px; align-items: flex-end;
}
.filter-item { display: flex; flex-direction: column; gap: 4px; }
.filter-item label { font-size: 12px; color: var(--text-secondary); font-weight: 500; }
.filter-item input {
  padding: 6px 10px; border: 1px solid var(--border-color); border-radius: var(--radius-sm);
  font-size: 13px; width: 150px; outline: none; transition: border-color 0.2s;
}
.filter-item input:focus { border-color: var(--accent); }
.filter-actions { display: flex; gap: 8px; align-items: flex-end; padding-bottom: 1px; }
.table-container { background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); overflow-x: auto; }
table { width: 100%; border-collapse: collapse; }
th {
  text-align: left; padding: 12px 14px; font-size: 12px; font-weight: 600; color: var(--text-secondary);
  text-transform: uppercase; letter-spacing: 0.05em; background: #f9fafb; border-bottom: 1px solid var(--border-color);
}
td { padding: 10px 14px; font-size: 13px; border-bottom: 1px solid var(--border-color); }
tbody tr:nth-child(even) { background: #f9fafb; }
tbody tr:hover { background: #eef2ff; }
.clickable-row { cursor: pointer; }
.badge { display: inline-block; padding: 2px 10px; border-radius: 12px; font-size: 12px; font-weight: 500; }
.badge-success { background: var(--success-bg); color: #065f46; }
.badge-error { background: var(--error-bg); color: #991b1b; }
.badge-warning { background: var(--warning-bg); color: #92400e; }
.badge-gray { background: var(--gray-bg); color: #4b5563; }
.btn {
  padding: 6px 14px; border: none; border-radius: var(--radius-sm);
  font-size: 13px; font-weight: 500; cursor: pointer; transition: background 0.2s;
}
.btn:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-primary { background: var(--accent); color: white; }
.btn-primary:hover:not(:disabled) { background: var(--accent-hover); }
.btn-secondary { background: var(--gray-bg); color: var(--text-primary); }
.btn-secondary:hover:not(:disabled) { background: #e5e7eb; }
.btn-info { background: #dbeafe; color: #1e40af; }
.btn-info:hover:not(:disabled) { background: #bfdbfe; }
.btn-sm { padding: 4px 10px; font-size: 12px; }
.btn-danger { background: var(--error-bg); color: #991b1b; }
.btn-danger:hover:not(:disabled) { background: #fecaca; }
.pagination {
  display: flex; align-items: center; justify-content: center; gap: 12px;
  margin-top: 16px; padding: 12px 0; font-size: 13px; color: var(--text-secondary);
}
.pagination button {
  padding: 6px 14px; border: 1px solid var(--border-color); border-radius: var(--radius-sm);
  background: var(--card-bg); cursor: pointer; font-size: 13px;
}
.pagination button:disabled { opacity: 0.4; cursor: not-allowed; }
.loading { text-align: center; padding: 40px; color: var(--text-muted); }
.empty { text-align: center; padding: 40px; color: var(--text-muted); }
</style>
