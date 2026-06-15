<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { fetchVitalSigns } from '../api'
import { formatDateTime } from '../utils/time'

const items = ref<any[]>([])
const totalCount = ref(0)
const loading = ref(false)
const filterPatientId = ref('')
const filterType = ref('')
const filterDateFrom = ref('')
const filterDateTo = ref('')
const page = ref(1)
const pageSize = ref(20)

const vitalTypes = [
  '心率', '血压', '体温', '呼吸', '血氧',
  '收缩压', '舒张压', '脉率', '体重', '身高',
]

async function loadData() {
  loading.value = true
  try {
    const params: Record<string, string | number | undefined> = {
      page: page.value,
      pageSize: pageSize.value,
      patientId: filterPatientId.value || undefined,
      type: filterType.value || undefined,
      dateFrom: filterDateFrom.value || undefined,
      dateTo: filterDateTo.value || undefined,
    }
    const data = await fetchVitalSigns(params)
    items.value = data.items || data.records || data.data || data || []
    totalCount.value = data.totalCount ?? data.total ?? items.value.length
  } catch {
    items.value = []
  } finally {
    loading.value = false
  }
}

function search() {
  page.value = 1
  loadData()
}

const totalPages = () => Math.max(1, Math.ceil(totalCount.value / pageSize.value))

onMounted(loadData)
</script>

<template>
  <div class="vitals-page">
    <div class="page-header">
      <h2>生命体征</h2>
    </div>

    <div class="filter-bar">
      <div class="filter-item">
        <label>患者 ID</label>
        <input v-model="filterPatientId" placeholder="患者 ID" @keyup.enter="search" />
      </div>
      <div class="filter-item">
        <label>类型</label>
        <select v-model="filterType">
          <option value="">全部</option>
          <option v-for="t in vitalTypes" :key="t" :value="t">{{ t }}</option>
        </select>
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
        <button class="btn btn-secondary" @click="() => { filterPatientId = ''; filterType = ''; filterDateFrom = ''; filterDateTo = ''; search() }">重置</button>
      </div>
    </div>

    <div class="table-container">
      <table v-if="items.length">
        <thead>
          <tr>
            <th>患者</th>
            <th>患者 ID</th>
            <th>床位</th>
            <th>类型</th>
            <th>值</th>
            <th>单位</th>
            <th>记录时间</th>
            <th>参考范围</th>
            <th>异常标志</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="v in items" :key="v.vitalSignId">
            <td>{{ v.patientName || '-' }}</td>
            <td>{{ v.patientId }}</td>
            <td>{{ v.bed || v.ward || '-' }}</td>
            <td>{{ v.vitalSignName || v.vitalSignType }}</td>
            <td :class="{ 'abnormal': v.abnormalFlags }">{{ v.valueNumeric ?? v.valueString }}</td>
            <td>{{ v.units }}</td>
            <td>{{ formatDateTime(v.observationDateTime) }}</td>
            <td>{{ v.referenceRange || '-' }}</td>
            <td>
              <span
                v-if="v.abnormalFlags"
                :class="[
                  'badge',
                  v.abnormalFlags === 'L' || v.abnormalFlags === 'H' ? 'badge-warning' :
                  v.abnormalFlags === 'LL' || v.abnormalFlags === 'HH' ? 'badge-error' :
                  'badge-gray'
                ]"
              >{{ v.abnormalFlags }}</span>
              <span v-else class="text-muted">正常</span>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-else-if="!loading" class="empty">暂无生命体征数据</div>
      <div v-if="loading" class="loading">加载中...</div>
    </div>

    <div class="pagination" v-if="totalCount > pageSize">
      <button :disabled="page <= 1" @click="page--; loadData()">上一页</button>
      <span>第 {{ page }} / {{ totalPages() }} 页 (共 {{ totalCount }} 条)</span>
      <button :disabled="page >= totalPages()" @click="page++; loadData()">下一页</button>
    </div>
  </div>
</template>

<style scoped>
.vitals-page {
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
  width: 150px;
  outline: none;
  transition: border-color 0.2s;
}

.filter-item input:focus,
.filter-item select:focus {
  border-color: var(--accent);
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

tbody tr:hover {
  background: #eef2ff;
}

.abnormal {
  color: var(--error);
  font-weight: 600;
}

.badge {
  display: inline-block;
  padding: 2px 10px;
  border-radius: 12px;
  font-size: 12px;
  font-weight: 500;
}

.badge-warning {
  background: var(--warning-bg);
  color: #92400e;
}

.badge-error {
  background: var(--error-bg);
  color: #991b1b;
}

.badge-gray {
  background: var(--gray-bg);
  color: #4b5563;
}

.text-muted {
  color: var(--text-muted);
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

.btn-primary {
  background: var(--accent);
  color: white;
}

.btn-primary:hover {
  background: var(--accent-hover);
}

.btn-secondary {
  background: var(--gray-bg);
  color: var(--text-primary);
}

.btn-secondary:hover {
  background: #e5e7eb;
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
