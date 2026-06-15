<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { fetchIdentifierMappings, createIdentifierMapping, updateIdentifierMapping, deleteIdentifierMapping } from '../api'

const items = ref<any[]>([])
const loading = ref(false)
const filterSource = ref('')
const showModal = ref(false)
const editingItem = ref<any>(null)
const formData = ref({
  sourceSystem: '',
  sourceCode: '',
  sourceText: '',
  vitalSignType: '',
  vitalSignName: '',
  loincCode: '',
  isActive: true,
})
const saving = ref(false)

async function loadMappings() {
  loading.value = true
  try {
    const params: Record<string, string | undefined> = {}
    if (filterSource.value) params.sourceSystem = filterSource.value
    const data = await fetchIdentifierMappings(params)
    items.value = Array.isArray(data) ? data : data.items || data.records || data.data || []
  } catch {
    items.value = []
  } finally {
    loading.value = false
  }
}

function openAdd() {
  editingItem.value = null
  formData.value = { sourceSystem: '', sourceCode: '', sourceText: '', vitalSignType: '', vitalSignName: '', loincCode: '', isActive: true }
  showModal.value = true
}

function openEdit(item: any) {
  editingItem.value = item
  formData.value = {
    sourceSystem: item.sourceSystem || '',
    sourceCode: item.sourceCode || '',
    sourceText: item.sourceText || '',
    vitalSignType: item.vitalSignType || '',
    vitalSignName: item.vitalSignName || '',
    loincCode: item.loincCode || '',
    isActive: item.isActive ?? true,
  }
  showModal.value = true
}

async function save() {
  saving.value = true
  try {
    if (editingItem.value) {
      await updateIdentifierMapping(editingItem.value.mappingId, formData.value)
    } else {
      await createIdentifierMapping(formData.value)
    }
    showModal.value = false
    await loadMappings()
  } catch (err: any) {
    alert(`保存失败: ${err.message}`)
  } finally {
    saving.value = false
  }
}

async function confirmDelete(id: number) {
  if (!confirm('确定要删除此映射吗？')) return
  try {
    await deleteIdentifierMapping(id)
    await loadMappings()
  } catch (err: any) {
    alert(`删除失败: ${err.message}`)
  }
}

onMounted(loadMappings)
</script>

<template>
  <div class="mappings-page">
    <div class="page-header">
      <h2>标识映射</h2>
      <div class="page-hint">将监护仪设备编码映射为统一的生命体征类型和中文名称</div>
    </div>

    <div class="toolbar">
      <div class="filter-bar">
        <div class="filter-item">
          <label>源系统</label>
          <input v-model="filterSource" placeholder="如 MDIL / MDC" @keyup.enter="loadMappings" />
        </div>
        <button class="btn btn-primary btn-sm" @click="loadMappings">查询</button>
        <button class="btn btn-secondary btn-sm" @click="() => { filterSource = ''; loadMappings() }">重置</button>
      </div>
      <button class="btn btn-primary" @click="openAdd">添加映射</button>
    </div>

    <div class="table-container">
      <table v-if="items.length">
        <thead>
          <tr>
            <th>ID</th>
            <th>源系统</th>
            <th>设备编码</th>
            <th>设备名称</th>
            <th>体征类型</th>
            <th>体征名称</th>
            <th>LOINC</th>
            <th>状态</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in items" :key="item.mappingId">
            <td>{{ item.mappingId }}</td>
            <td>{{ item.sourceSystem }}</td>
            <td><code>{{ item.sourceCode }}</code></td>
            <td>{{ item.sourceText || '-' }}</td>
            <td>{{ item.vitalSignType }}</td>
            <td>{{ item.vitalSignName }}</td>
            <td>{{ item.loincCode || '-' }}</td>
            <td>
              <span :class="['badge', item.isActive ? 'badge-success' : 'badge-gray']">
                {{ item.isActive ? '启用' : '停用' }}
              </span>
            </td>
            <td class="actions">
              <button class="btn btn-sm btn-secondary" @click="openEdit(item)">编辑</button>
              <button class="btn btn-sm btn-danger" @click="confirmDelete(item.mappingId)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-else-if="!loading" class="empty">暂无映射</div>
      <div v-if="loading" class="loading">加载中...</div>
    </div>

    <!-- Modal -->
    <div v-if="showModal" class="modal-overlay" @click.self="showModal = false">
      <div class="modal">
        <h3 class="modal-title">{{ editingItem ? '编辑映射' : '添加映射' }}</h3>
        <div class="modal-body">
          <div class="form-row">
            <div class="form-group">
              <label>源系统 *</label>
              <input v-model="formData.sourceSystem" placeholder="如 MDIL / MDC" />
            </div>
            <div class="form-group">
              <label>设备编码 *</label>
              <input v-model="formData.sourceCode" placeholder="如 0002-4182" />
            </div>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label>设备名称</label>
              <input v-model="formData.sourceText" placeholder="如 HR" />
            </div>
            <div class="form-group">
              <label>LOINC 编码</label>
              <input v-model="formData.loincCode" placeholder="如 8867-4" />
            </div>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label>体征类型 *</label>
              <input v-model="formData.vitalSignType" placeholder="如 HR / SPO2 / NIBP_SYS" />
            </div>
            <div class="form-group">
              <label>体征名称 *</label>
              <input v-model="formData.vitalSignName" placeholder="如 心率" />
            </div>
          </div>
          <div class="form-group">
            <label class="checkbox-label">
              <input type="checkbox" v-model="formData.isActive" />
              启用
            </label>
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="showModal = false">取消</button>
          <button class="btn btn-primary" :disabled="saving" @click="save">
            {{ saving ? '保存中...' : '保存' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.mappings-page {
  max-width: 1200px;
}

.page-header {
  margin-bottom: 20px;
}

.page-header h2 {
  font-size: 22px;
  margin-bottom: 4px;
}

.page-hint {
  font-size: 13px;
  color: var(--text-secondary);
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
  background: var(--card-bg);
  padding: 12px 16px;
  border-radius: var(--radius);
  box-shadow: var(--shadow);
}

.filter-bar {
  display: flex;
  align-items: flex-end;
  gap: 8px;
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

.filter-item input {
  padding: 6px 10px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  font-size: 13px;
  width: 160px;
  outline: none;
}

.filter-item input:focus {
  border-color: var(--accent);
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

code {
  font-family: var(--font-mono);
  font-size: 12px;
  background: #f3f4f6;
  padding: 1px 6px;
  border-radius: 3px;
}

.actions {
  display: flex;
  gap: 6px;
}

.badge {
  display: inline-block;
  padding: 2px 10px;
  border-radius: 12px;
  font-size: 12px;
  font-weight: 500;
}

.badge-success {
  background: var(--success-bg);
  color: #065f46;
}

.badge-error {
  background: var(--error-bg);
  color: #991b1b;
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

.btn-sm {
  padding: 4px 10px;
  font-size: 12px;
}

.btn-danger {
  background: var(--error-bg);
  color: #991b1b;
}

.btn-danger:hover:not(:disabled) {
  background: #fecaca;
}

.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.4);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 200;
}

.modal {
  background: var(--card-bg);
  border-radius: var(--radius);
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.15);
  width: 560px;
  max-width: 90vw;
}

.modal-title {
  font-size: 16px;
  font-weight: 600;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border-color);
  color: var(--text-primary);
}

.modal-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.form-row {
  display: flex;
  gap: 12px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
  flex: 1;
}

.form-group label {
  font-size: 12px;
  color: var(--text-secondary);
  font-weight: 500;
}

.form-group input[type="text"],
.form-group input[type="number"] {
  padding: 8px 10px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  font-size: 13px;
  outline: none;
}

.form-group input:focus {
  border-color: var(--accent);
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  cursor: pointer;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 14px 20px;
  border-top: 1px solid var(--border-color);
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
