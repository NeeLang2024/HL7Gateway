<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { createAutoAdtBed, deleteAutoAdtBed, fetchAutoAdtBeds, importAutoAdtBeds, updateAutoAdtBed } from '../api'
import { formatDateTime } from '../utils/time'

const beds = ref<any[]>([])
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const notice = ref('')
const search = ref('')
const editingId = ref<number | null>(null)
const importCsv = ref('')
const importUpdateExisting = ref(false)
const importing = ref(false)

const form = ref(defaultForm())

function defaultForm() {
  return {
    careArea: '',
    room: '',
    bed: '',
    bedLabel: '',
    deviceCode: '',
    deviceBarcode: '',
    bedBarcode: '',
    philipsLocationValue: '',
    isEnabled: true,
    remark: '',
  }
}

async function loadBeds() {
  loading.value = true
  error.value = ''
  try {
    const data = await fetchAutoAdtBeds({ search: search.value, includeDisabled: true })
    beds.value = Array.isArray(data) ? data : data.items || []
  } catch (err: any) {
    error.value = err.message || '加载床位映射失败'
    beds.value = []
  } finally {
    loading.value = false
  }
}

function editBed(bed: any) {
  editingId.value = bed.id
  form.value = {
    careArea: bed.careArea || '',
    room: bed.room || '',
    bed: bed.bed || '',
    bedLabel: bed.bedLabel || '',
    deviceCode: bed.deviceCode || '',
    deviceBarcode: bed.deviceBarcode || '',
    bedBarcode: bed.bedBarcode || '',
    philipsLocationValue: bed.philipsLocationValue || '',
    isEnabled: bed.isEnabled !== false,
    remark: bed.remark || '',
  }
}

function resetForm() {
  editingId.value = null
  form.value = defaultForm()
}

async function saveBed() {
  if (!form.value.philipsLocationValue) {
    error.value = 'PhilipsLocationValue 必填'
    return
  }
  saving.value = true
  error.value = ''
  notice.value = ''
  try {
    if (editingId.value) await updateAutoAdtBed(editingId.value, form.value)
    else await createAutoAdtBed(form.value)
    notice.value = editingId.value ? '床位映射已更新' : '床位映射已新增'
    resetForm()
    await loadBeds()
  } catch (err: any) {
    error.value = err.message || '保存失败'
  } finally {
    saving.value = false
  }
}

async function removeBed(bed: any) {
  if (!confirm(`确定删除床位映射 ${bed.bedLabel || bed.philipsLocationValue}？`)) return
  try {
    await deleteAutoAdtBed(bed.id)
    notice.value = '床位映射已删除'
    await loadBeds()
  } catch (err: any) {
    error.value = err.message || '删除失败'
  }
}

async function importBeds() {
  if (!importCsv.value.trim()) {
    error.value = '请粘贴 CSV 内容'
    return
  }
  importing.value = true
  error.value = ''
  notice.value = ''
  try {
    const res = await importAutoAdtBeds(importCsv.value, importUpdateExisting.value)
    notice.value = `导入完成：新增 ${res.created}，更新 ${res.updated}，跳过 ${res.skipped}`
    if (res.errors?.length) error.value = res.errors.join('；')
    importCsv.value = ''
    await loadBeds()
  } catch (err: any) {
    error.value = err.message || '导入失败'
  } finally {
    importing.value = false
  }
}

onMounted(loadBeds)
</script>

<template>
  <div class="auto-page">
    <div class="page-header">
      <h2>Philips 床位映射</h2>
    </div>

    <div v-if="notice" class="notification">{{ notice }}</div>
    <div v-if="error" class="error-bar">{{ error }}</div>

    <section class="panel">
      <h3>{{ editingId ? '编辑床位映射' : '新增床位映射' }}</h3>
      <div class="form-grid">
        <label>Care Area<input v-model="form.careArea" placeholder="ICU" /></label>
        <label>Room<input v-model="form.room" placeholder="ROOM01" /></label>
        <label>Bed<input v-model="form.bed" placeholder="Bed4" /></label>
        <label>Bed Label<input v-model="form.bedLabel" placeholder="ICU-4" /></label>
        <label>Device Code<input v-model="form.deviceCode" placeholder="MON-ICU-04" /></label>
        <label>Device Barcode<input v-model="form.deviceBarcode" placeholder="扫码设备码" /></label>
        <label>Bed Barcode<input v-model="form.bedBarcode" placeholder="扫码床位码" /></label>
        <label class="wide">PhilipsLocationValue *<input v-model="form.philipsLocationValue" placeholder="ICU^ROOM01^Bed4 或现场确认格式" /></label>
        <label class="wide">备注<input v-model="form.remark" /></label>
        <label class="check"><input type="checkbox" v-model="form.isEnabled" /> 启用</label>
      </div>
      <div class="actions">
        <button class="btn btn-primary" :disabled="saving" @click="saveBed">{{ saving ? '保存中...' : '保存' }}</button>
        <button class="btn btn-secondary" @click="resetForm">清空</button>
      </div>
    </section>

    <section class="panel">
      <h3>批量导入（CSV）</h3>
      <p class="hint">列顺序：CareArea, Room, Bed, BedLabel, DeviceCode, DeviceBarcode, BedBarcode, <strong>PhilipsLocationValue</strong>, IsEnabled, Remark。首行可含表头。</p>
      <textarea v-model="importCsv" rows="6" placeholder="ICU,,Bed1,Bed1,M1,M1,,ICU^^Bed1,true,"></textarea>
      <label class="check"><input v-model="importUpdateExisting" type="checkbox" /> 已存在相同 PhilipsLocationValue 时更新</label>
      <div class="actions">
        <button class="btn btn-primary" :disabled="importing" @click="importBeds">{{ importing ? '导入中...' : '导入 CSV' }}</button>
      </div>
    </section>

    <section class="panel">
      <div class="toolbar">
        <strong>映射列表</strong>
        <div>
          <input v-model="search" placeholder="搜索床位/设备/PhilipsLocationValue" @keyup.enter="loadBeds" />
          <button class="btn btn-secondary" @click="loadBeds">查询</button>
        </div>
      </div>
      <table v-if="beds.length">
        <thead>
          <tr>
            <th>床位</th>
            <th>设备</th>
            <th>条码</th>
            <th>PhilipsLocationValue</th>
            <th>状态</th>
            <th>更新时间</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="bed in beds" :key="bed.id">
            <td>{{ bed.careArea || '-' }} / {{ bed.room || '-' }} / {{ bed.bed || '-' }}<br><small>{{ bed.bedLabel || '-' }}</small></td>
            <td>{{ bed.deviceCode || '-' }}</td>
            <td>设备：{{ bed.deviceBarcode || '-' }}<br>床位：{{ bed.bedBarcode || '-' }}</td>
            <td class="mono">{{ bed.philipsLocationValue }}</td>
            <td><span :class="['badge', bed.isEnabled ? 'ok' : 'off']">{{ bed.isEnabled ? '启用' : '停用' }}</span></td>
            <td>{{ formatDateTime(bed.updatedAt) }}</td>
            <td>
              <button class="btn btn-sm btn-primary" @click="editBed(bed)">编辑</button>
              <button class="btn btn-sm btn-danger" @click="removeBed(bed)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-else-if="!loading" class="empty">暂无床位映射</div>
      <div v-if="loading" class="loading">加载中...</div>
    </section>
  </div>
</template>

<style scoped>
.auto-page { max-width: 1280px; }
.page-header { margin-bottom: 18px; }
.panel { background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); padding: 16px; margin-bottom: 16px; }
.panel h3 { font-size: 16px; margin-bottom: 14px; }
.form-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; }
label { display: flex; flex-direction: column; gap: 6px; font-size: 12px; color: var(--text-secondary); }
input { padding: 8px 10px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 13px; }
.wide { grid-column: span 2; }
.check { flex-direction: row; align-items: center; padding-top: 24px; }
.actions { display: flex; gap: 8px; margin-top: 14px; }
.toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; gap: 12px; }
.toolbar div { display: flex; gap: 8px; }
.toolbar input { min-width: 300px; }
table { width: 100%; border-collapse: collapse; font-size: 13px; }
th, td { padding: 10px 8px; border-bottom: 1px solid var(--border-color); text-align: left; vertical-align: top; }
th { color: var(--text-secondary); background: #f8fafc; }
.mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
.badge { padding: 3px 8px; border-radius: 999px; font-size: 12px; }
.badge.ok { background: #dcfce7; color: #166534; }
.badge.off { background: #e5e7eb; color: #4b5563; }
.notification { background: #ecfdf5; color: #047857; padding: 10px 12px; border-radius: var(--radius-sm); margin-bottom: 12px; }
.error-bar { background: #fef2f2; color: #b91c1c; padding: 10px 12px; border-radius: var(--radius-sm); margin-bottom: 12px; }
.hint { font-size: 13px; color: var(--text-secondary); margin-bottom: 10px; }
textarea { width: 100%; padding: 10px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-family: ui-monospace, monospace; font-size: 12px; margin-bottom: 10px; }
.empty, .loading { padding: 24px; text-align: center; color: var(--text-secondary); }
@media (max-width: 1000px) { .form-grid { grid-template-columns: 1fr 1fr; } .wide { grid-column: span 2; } }
</style>
