<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { fetchWsiSubscriptions, subscribeWsi, unsubscribeWsi } from '../api'
import { formatDateTime } from '../utils/time'

const subs = ref<any[]>([])
const loading = ref(false)
const error = ref('')
const notification = ref('')
let notifTimer: number | undefined

const showForm = ref(false)
const form = ref({
  notificationUri: '',
  clientId: '',
})
const subscribing = ref(false)
const subError = ref('')

function notify(msg: string) {
  notification.value = msg
  if (notifTimer) clearTimeout(notifTimer)
  notifTimer = window.setTimeout(() => { notification.value = '' }, 4000)
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await fetchWsiSubscriptions()
    subs.value = Array.isArray(data) ? data : data.items || []
  } catch (err: any) {
    error.value = err.message || '加载失败'
    subs.value = []
  } finally {
    loading.value = false
  }
}

async function doSubscribe() {
  if (!form.value.notificationUri) { subError.value = '请输入回调 URL'; return }
  subscribing.value = true
  subError.value = ''
  try {
    await subscribeWsi({
      notificationUri: form.value.notificationUri,
      clientId: form.value.clientId || undefined,
    })
    notify('订阅成功')
    showForm.value = false
    form.value.notificationUri = ''
    form.value.clientId = ''
    load()
  } catch (err: any) {
    subError.value = err.message || '订阅失败'
  } finally {
    subscribing.value = false
  }
}

async function doUnsubscribe(id: number) {
  if (!confirm('确定取消此订阅？')) return
  try {
    await unsubscribeWsi(id)
    notify('已取消订阅')
    load()
  } catch { notify('取消失败') }
}

const statusLabel = (s: any) => {
  if (!s.isActive) return '已停用'
  if (s.failedCount >= 3) return '失败停用'
  return '活跃'
}

const statusClass = (s: any) => {
  if (!s.isActive || s.failedCount >= 3) return 'badge-error'
  return 'badge-success'
}

onMounted(load)
</script>

<template>
  <div class="wsi-page">
    <div class="page-header">
      <h2>WSI 订阅管理</h2>
      <button class="btn btn-primary" @click="showForm = !showForm">
        {{ showForm ? '取消' : '新建订阅' }}
      </button>
    </div>

    <div v-if="notification" class="notification">{{ notification }}</div>

    <!-- Subscribe form -->
    <div v-if="showForm" class="card form-card">
      <h3 class="form-title">新建 WSI 订阅</h3>
      <div class="form-row">
        <div class="form-group">
          <label>回调 URL *</label>
          <input v-model="form.notificationUri" placeholder="如 http://192.168.1.100:8080/wsi" />
        </div>
        <div class="form-group">
          <label>客户端标识</label>
          <input v-model="form.clientId" placeholder="如 PIC-iX-01" />
        </div>
      </div>
      <div v-if="subError" class="form-error">{{ subError }}</div>
      <div class="form-actions">
        <button class="btn btn-primary" :disabled="subscribing" @click="doSubscribe">
          {{ subscribing ? '提交中...' : '订阅' }}
        </button>
      </div>
    </div>

    <!-- Subscription list -->
    <div class="card">
      <div class="card-header">
        <span>订阅列表 ({{ subs.length }})</span>
        <button class="btn btn-sm btn-secondary" @click="load">刷新</button>
      </div>

      <table v-if="subs.length">
        <thead>
          <tr>
            <th>ID</th>
            <th>回调 URL</th>
            <th>客户端</th>
            <th>状态</th>
            <th>通知次数</th>
            <th>失败次数</th>
            <th>最后通知</th>
            <th>创建时间</th>
            <th>过期时间</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="s in subs" :key="s.subscriptionId">
            <td>{{ s.subscriptionId }}</td>
            <td class="url-cell" :title="s.notificationUri">{{ s.notificationUri }}</td>
            <td>{{ s.clientId || '-' }}</td>
            <td><span :class="['badge', statusClass(s)]">{{ statusLabel(s) }}</span></td>
            <td>{{ s.notifyCount }}</td>
            <td>{{ s.failedCount }}</td>
            <td>{{ formatDateTime(s.lastNotifiedAt) }}</td>
            <td>{{ formatDateTime(s.createdAt) }}</td>
            <td>{{ formatDateTime(s.expiresAt) }}</td>
            <td>
              <button class="btn btn-sm btn-danger" @click="doUnsubscribe(s.subscriptionId)" :disabled="!s.isActive">取消</button>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-else-if="!loading && !error" class="empty">暂无订阅</div>
      <div v-if="error" class="empty error">{{ error }}</div>
      <div v-if="loading" class="loading">加载中...</div>
    </div>
  </div>
</template>

<style scoped>
.wsi-page { max-width: 1200px; }
.page-header {
  display: flex; align-items: center; justify-content: space-between; margin-bottom: 20px;
}
.page-header h2 { font-size: 22px; }
.card {
  background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); padding: 20px; margin-bottom: 16px;
}
.card-header {
  display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; font-weight: 600; font-size: 14px;
}
.form-card { margin-bottom: 16px; }
.form-title { font-size: 15px; font-weight: 600; margin-bottom: 16px; }
.form-row { display: flex; gap: 12px; margin-bottom: 12px; }
.form-group { display: flex; flex-direction: column; gap: 4px; flex: 1; }
.form-group label { font-size: 12px; color: var(--text-secondary); font-weight: 500; }
.form-group input { padding: 8px 10px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 13px; outline: none; }
.form-group input:focus { border-color: var(--accent); }
.form-error { color: #991b1b; font-size: 13px; margin-bottom: 8px; }
.form-actions { margin-top: 8px; }
.notification { background: var(--info-bg); color: #1e40af; padding: 10px 16px; border-radius: var(--radius); margin-bottom: 16px; font-size: 13px; border: 1px solid #bfdbfe; }
table { width: 100%; border-collapse: collapse; }
th { text-align: left; padding: 10px 8px; font-size: 12px; font-weight: 600; color: var(--text-secondary); background: #f9fafb; border-bottom: 1px solid var(--border-color); }
td { padding: 8px; font-size: 13px; border-bottom: 1px solid var(--border-color); }
.url-cell { max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.badge { display: inline-block; padding: 2px 10px; border-radius: 12px; font-size: 12px; font-weight: 500; }
.badge-success { background: var(--success-bg); color: #065f46; }
.badge-error { background: var(--error-bg); color: #991b1b; }
.btn { padding: 8px 20px; border: none; border-radius: var(--radius-sm); font-size: 13px; font-weight: 500; cursor: pointer; transition: background 0.2s; }
.btn:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-primary { background: var(--accent); color: white; }
.btn-primary:hover:not(:disabled) { background: var(--accent-hover); }
.btn-secondary { background: var(--gray-bg); color: var(--text-primary); }
.btn-secondary:hover:not(:disabled) { background: #e5e7eb; }
.btn-sm { padding: 4px 12px; font-size: 12px; }
.btn-danger { background: var(--error-bg); color: #991b1b; }
.btn-danger:hover:not(:disabled) { background: #fecaca; }
.loading { text-align: center; padding: 40px; color: var(--text-muted); }
.empty { text-align: center; padding: 40px; color: var(--text-muted); }
.empty.error { color: #991b1b; }
</style>
