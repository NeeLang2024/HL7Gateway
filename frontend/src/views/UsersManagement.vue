<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { fetchUsers, createUser, updateUser, deleteUser } from '../api'
import { formatDateTime } from '../utils/time'

const users = ref<any[]>([])
const loading = ref(true)
const showForm = ref(false)
const editingId = ref<number | null>(null)
const form = ref({ username: '', password: '', displayName: '', role: 'operator' })
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await fetchUsers()
    users.value = Array.isArray(data)
      ? data.map(normalizeUser)
      : []
  } catch (e: any) {
    error.value = e.message
    users.value = []
  } finally {
    loading.value = false
  }
}

function normalizeUser(u: any) {
  return {
    id: u.id ?? u.userId,
    username: u.username,
    displayName: u.displayName,
    role: u.role,
    isActive: u.isActive,
    createdAt: u.createdAt,
  }
}

function openCreate() {
  editingId.value = null
  form.value = { username: '', password: '', displayName: '', role: 'operator' }
  showForm.value = true
  error.value = ''
}

function openEdit(u: any) {
  editingId.value = u.id
  form.value = { username: u.username, password: '', displayName: u.displayName || '', role: u.role || 'operator' }
  showForm.value = true
  error.value = ''
}

async function save() {
  error.value = ''
  if (!form.value.username.trim()) {
    error.value = '请输入用户名'
    return
  }
  if (!editingId.value && !form.value.password.trim()) {
    error.value = '请输入密码'
    return
  }
  try {
    if (editingId.value) {
      await updateUser(editingId.value, form.value)
    } else {
      await createUser(form.value)
    }
    showForm.value = false
    await load()
  } catch (e: any) {
    error.value = e.message
  }
}

async function doDelete(id: number) {
  if (!confirm('确定删除此用户?')) return
  try {
    await deleteUser(id)
    await load()
  } catch (e: any) {
    error.value = e.message
  }
}

onMounted(load)
</script>

<template>
  <div class="users-page">
    <div class="page-header">
      <h2>用户管理</h2>
      <button class="btn btn-primary" @click="openCreate">新建用户</button>
    </div>

    <div v-if="error" class="error-bar">{{ error }}</div>

    <div v-if="showForm" class="card form-card">
      <h3>{{ editingId ? '编辑用户' : '新建用户' }}</h3>
      <div class="form-grid">
        <label>用户名 <input v-model="form.username" class="input" /></label>
        <label>密码 <input v-model="form.password" class="input" type="password" :placeholder="editingId ? '留空则不修改' : ''" /></label>
        <label>显示名 <input v-model="form.displayName" class="input" /></label>
        <label>角色
          <select v-model="form.role" class="input">
            <option value="admin">管理员</option>
            <option value="operator">操作员</option>
            <option value="viewer">只读</option>
          </select>
        </label>
      </div>
      <div class="form-actions">
        <button class="btn btn-primary" @click="save">保存</button>
        <button class="btn" @click="showForm = false">取消</button>
      </div>
    </div>

    <div v-if="loading" class="loading">加载中...</div>
    <div v-else class="table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>ID</th>
            <th>用户名</th>
            <th>显示名</th>
            <th>角色</th>
            <th>创建时间</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="u in users" :key="u.id">
            <td>{{ u.id }}</td>
            <td>{{ u.username }}</td>
            <td>{{ u.displayName }}</td>
            <td>{{ u.role }}</td>
            <td>{{ formatDateTime(u.createdAt) }}</td>
            <td>
              <button class="btn btn-sm" @click="openEdit(u)">编辑</button>
              <button class="btn btn-sm btn-danger" @click="doDelete(u.id)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-if="!users.length" class="empty">暂无用户</div>
    </div>
  </div>
</template>

<style scoped>
.users-page { max-width: 1000px; }
.page-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 24px; }
.page-header h2 { font-size: 22px; color: var(--text-primary); }
.error-bar { background: #fef2f2; color: #dc2626; padding: 10px 16px; border-radius: var(--radius); margin-bottom: 16px; font-size: 13px; border: 1px solid #fca5a5; }
.form-card { margin-bottom: 24px; }
.form-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 16px; }
.form-grid label { display: flex; flex-direction: column; gap: 4px; font-size: 13px; color: var(--text-secondary); }
.form-actions { display: flex; gap: 8px; }
.loading { text-align: center; padding: 60px 0; color: var(--text-muted); }
.empty { text-align: center; padding: 32px; color: var(--text-muted); font-size: 13px; border-top: 1px solid var(--border-color); }
.table-wrap { background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); overflow: hidden; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 10px 16px; text-align: left; font-size: 13px; border-bottom: 1px solid var(--border-color); }
.table th { background: var(--bg-secondary); font-weight: 600; color: var(--text-secondary); }
.btn { padding: 8px 16px; border: 1px solid var(--border-color); border-radius: var(--radius); background: var(--card-bg); color: var(--text-primary); cursor: pointer; font-size: 13px; }
.btn-primary { background: var(--accent); color: #fff; border-color: var(--accent); }
.btn-danger { color: #dc2626; border-color: #fca5a5; }
.btn-sm { padding: 4px 10px; font-size: 12px; }
</style>
