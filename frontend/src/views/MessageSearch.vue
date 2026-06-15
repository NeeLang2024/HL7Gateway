<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { searchMessages } from '../api'
import { formatDateTime } from '../utils/time'

const router = useRouter()
const query = ref('')
const results = ref<any[]>([])
const loading = ref(false)
const searched = ref(false)
const error = ref('')

async function doSearch() {
  if (!query.value.trim()) return
  loading.value = true
  searched.value = true
  error.value = ''
  try {
    const res = await searchMessages(query.value)
    results.value = res?.items ?? res ?? []
  } catch (e: any) {
    error.value = e.message
  }
  loading.value = false
}

function viewDetail(id: number) {
  router.push(`/messages/${id}`)
}
</script>

<template>
  <div class="search-page">
    <h2 class="page-title">全文搜索</h2>

    <div class="search-bar">
      <input
        v-model="query"
        class="input search-input"
        placeholder="搜索消息内容、患者 ID、控制 ID..."
        @keyup.enter="doSearch"
      />
      <button class="btn btn-primary" @click="doSearch" :disabled="loading">搜索</button>
    </div>

    <div v-if="error" class="error-bar">{{ error }}</div>

    <div v-if="loading" class="loading">搜索中...</div>

    <div v-else-if="searched && results.length === 0" class="no-data">未找到匹配的消息</div>

    <div v-else-if="results.length" class="table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>ID</th>
            <th>控制 ID</th>
            <th>类型</th>
            <th>患者</th>
            <th>来源</th>
            <th>时间</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="m in results" :key="m.id">
            <td>{{ m.id }}</td>
            <td>{{ m.messageControlId }}</td>
            <td>{{ m.messageType }}{{ m.triggerEvent ? '^' + m.triggerEvent : '' }}</td>
            <td>{{ m.patientId }}</td>
            <td>{{ m.sourceIp }}</td>
            <td>{{ formatDateTime(m.receivedAt) }}</td>
            <td><button class="btn btn-sm" @click="viewDetail(m.id)">详情</button></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style scoped>
.search-page { max-width: 1000px; }
.page-title { font-size: 22px; color: var(--text-primary); margin-bottom: 24px; }
.search-bar { display: flex; gap: 8px; margin-bottom: 24px; }
.search-input { flex: 1; }
.btn { padding: 8px 16px; border: 1px solid var(--border-color); border-radius: var(--radius); background: var(--card-bg); color: var(--text-primary); cursor: pointer; font-size: 13px; }
.btn-primary { background: var(--accent); color: #fff; border-color: var(--accent); }
.btn-sm { padding: 4px 10px; font-size: 12px; }
.input { padding: 8px 12px; border: 1px solid var(--border-color); border-radius: var(--radius); font-size: 13px; background: var(--card-bg); color: var(--text-primary); }
.error-bar { background: #fef2f2; color: #dc2626; padding: 10px 16px; border-radius: var(--radius); margin-bottom: 16px; font-size: 13px; border: 1px solid #fca5a5; }
.loading { text-align: center; padding: 40px 0; color: var(--text-muted); }
.no-data { text-align: center; padding: 40px 0; color: var(--text-muted); }
.table-wrap { background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); overflow: hidden; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 10px 16px; text-align: left; font-size: 13px; border-bottom: 1px solid var(--border-color); }
.table th { background: var(--bg-secondary); font-weight: 600; color: var(--text-secondary); }
</style>
