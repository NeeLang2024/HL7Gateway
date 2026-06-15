<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { fetchPatients, fetchPatientVisits } from '../api'
import { formatDateTime } from '../utils/time'

const patients = ref<any[]>([])
const loading = ref(false)
const searchQuery = ref('')
const selectedPatient = ref<any>(null)
const visits = ref<any[]>([])
const visitsLoading = ref(false)
const showVisits = ref(false)

async function loadPatients() {
  loading.value = true
  try {
    const params: Record<string, string | undefined> = {}
    if (searchQuery.value) params.search = searchQuery.value
    const data = await fetchPatients(params)
    patients.value = Array.isArray(data) ? data : data.items || data.records || data.data || []
  } catch {
    patients.value = []
  } finally {
    loading.value = false
  }
}

function search() {
  selectedPatient.value = null
  showVisits.value = false
  loadPatients()
}

async function viewVisits(patient: any) {
  selectedPatient.value = patient
  showVisits.value = true
  visitsLoading.value = true
  try {
    const data = await fetchPatientVisits(patient.patientId || patient.id)
    visits.value = Array.isArray(data) ? data : data.items || data.records || data.data || []
  } catch {
    visits.value = []
  } finally {
    visitsLoading.value = false
  }
}

function getGenderLabel(gender: string): string {
  if (!gender) return '-'
  const map: Record<string, string> = {
    M: '男', F: '女', O: '其他', U: '未知',
    Male: '男', Female: '女',
  }
  return map[gender] || gender
}

onMounted(loadPatients)
</script>

<template>
  <div class="patients-page">
    <div class="page-header">
      <h2>患者管理</h2>
    </div>

    <div class="search-bar">
      <input
        v-model="searchQuery"
        placeholder="搜索患者 ID..."
        @keyup.enter="search"
      />
      <button class="btn btn-primary" @click="search">搜索</button>
      <button class="btn btn-secondary" @click="() => { searchQuery = ''; search() }">清除</button>
    </div>

    <div class="content-layout">
      <div class="patient-list">
        <div class="table-container">
          <table v-if="patients.length">
            <thead>
              <tr>
                <th>患者 ID</th>
                <th>姓名</th>
                <th>性别</th>
                <th>出生日期</th>
                <th>床位</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="p in patients"
                :key="p.id || p.patientId"
                :class="{ selected: selectedPatient?.patientId === p.patientId }"
              >
                <td>{{ p.patientId }}</td>
                <td>{{ p.name || p.patientName || '-' }}</td>
                <td>{{ getGenderLabel(p.gender) }}</td>
                <td>{{ p.dateOfBirth || p.dob || p.birthDate || '-' }}</td>
                <td>{{ p.currentWard || p.currentBed || '-' }}</td>
                <td>
                  <button class="btn btn-sm btn-primary" @click="viewVisits(p)">就诊记录</button>
                </td>
              </tr>
            </tbody>
          </table>
          <div v-else-if="!loading" class="empty">暂无患者数据</div>
          <div v-if="loading" class="loading">加载中...</div>
        </div>
      </div>

      <div v-if="showVisits" class="visit-panel">
        <div class="visit-header">
          <h3>就诊历史 - {{ selectedPatient?.name || selectedPatient?.patientId }}</h3>
          <button class="btn btn-sm btn-secondary" @click="showVisits = false">关闭</button>
        </div>
        <div class="table-container">
          <table v-if="visits.length">
            <thead>
              <tr>
                <th>就诊 ID</th>
                <th>科室</th>
                <th>病区</th>
                <th>房间</th>
                <th>床位</th>
                <th>入院时间</th>
                <th>出院时间</th>
                <th>诊断</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="v in visits" :key="v.id || v.visitId">
                <td>{{ v.visitId || v.id }}</td>
                <td>{{ v.department || '-' }}</td>
                <td>{{ v.ward || '-' }}</td>
                <td>{{ v.room || '-' }}</td>
                <td>{{ v.bed || '-' }}</td>
                <td>{{ formatDateTime(v.admissionTime || v.admitTime) }}</td>
                <td>{{ formatDateTime(v.dischargeTime) }}</td>
                <td>{{ v.diagnosis || v.diagnose || '-' }}</td>
              </tr>
            </tbody>
          </table>
          <div v-else-if="!visitsLoading" class="empty">暂无就诊记录</div>
          <div v-if="visitsLoading" class="loading">加载中...</div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.patients-page {
  max-width: 1200px;
}

.page-header {
  margin-bottom: 20px;
}

.page-header h2 {
  font-size: 22px;
}

.search-bar {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
  background: var(--card-bg);
  padding: 12px 16px;
  border-radius: var(--radius);
  box-shadow: var(--shadow);
}

.search-bar input {
  flex: 1;
  padding: 8px 12px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  font-size: 13px;
  outline: none;
  max-width: 400px;
}

.search-bar input:focus {
  border-color: var(--accent);
}

.content-layout {
  display: grid;
  grid-template-columns: 1fr;
  gap: 16px;
}

.visit-panel {
  background: var(--card-bg);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  overflow: hidden;
}

.visit-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 16px;
  border-bottom: 1px solid var(--border-color);
  background: #f9fafb;
}

.visit-header h3 {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}

.table-container {
  background: var(--card-bg);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  overflow-x: auto;
  margin-bottom: 16px;
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

tbody tr.selected {
  background: #eef2ff;
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

.btn-sm {
  padding: 4px 10px;
  font-size: 12px;
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
