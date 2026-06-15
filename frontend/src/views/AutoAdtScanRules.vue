<script setup lang="ts">
import { onMounted, ref } from 'vue'
import {
  fetchAutoAdtScanRules,
  createAutoAdtScanRule,
  updateAutoAdtScanRule,
  deleteAutoAdtScanRule,
  testAutoAdtScanRule,
} from '../api'
import { formatDateTime } from '../utils/time'

const rules = ref<any[]>([])
const loading = ref(false)
const error = ref('')
const editing = ref<any>(null)
const saving = ref(false)

const testType = ref<'Patient' | 'Bed'>('Patient')
const testText = ref('')
const testResult = ref<any>(null)
const testing = ref(false)

function emptyRule() {
  return {
    id: 0,
    name: '',
    ruleType: 'Patient',
    pattern: '',
    stripPrefixes: '',
    priority: 100,
    isEnabled: true,
    sample: '',
    remark: '',
  }
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await fetchAutoAdtScanRules()
    rules.value = Array.isArray(data) ? data : (data.items || [])
  } catch (err: any) {
    error.value = err.message || '加载失败'
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editing.value = emptyRule()
}
function openEdit(r: any) {
  editing.value = { ...r }
}
function closeEdit() {
  editing.value = null
}

async function save() {
  if (!editing.value) return
  if (!editing.value.name?.trim()) { error.value = '规则名称必填'; return }
  if (!editing.value.pattern?.trim() && !editing.value.stripPrefixes?.trim()) {
    error.value = '请至少填写正则表达式或去前缀之一'
    return
  }
  saving.value = true
  error.value = ''
  try {
    if (editing.value.id) {
      await updateAutoAdtScanRule(editing.value.id, editing.value)
    } else {
      await createAutoAdtScanRule(editing.value)
    }
    editing.value = null
    await load()
  } catch (err: any) {
    error.value = err.message || '保存失败'
  } finally {
    saving.value = false
  }
}

async function remove(r: any) {
  if (!confirm(`确认删除规则「${r.name}」？`)) return
  error.value = ''
  try {
    await deleteAutoAdtScanRule(r.id)
    await load()
  } catch (err: any) {
    error.value = err.message || '删除失败'
  }
}

async function runTest() {
  if (!testText.value.trim()) { testResult.value = null; return }
  testing.value = true
  try {
    testResult.value = await testAutoAdtScanRule(testType.value, testText.value)
  } catch (err: any) {
    testResult.value = { error: err.message || '测试失败' }
  } finally {
    testing.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="auto-page">
    <div class="page-header">
      <h2>扫码规则配置</h2>
      <div class="header-actions">
        <button class="btn btn-secondary" @click="load">刷新</button>
        <button class="btn btn-primary" @click="openCreate">新增规则</button>
      </div>
    </div>

    <p class="hint">
      扫码时按优先级（数字越小越先）逐条尝试，命中即止；<b>没有任何规则命中时回退到内置默认解析</b>，因此不配置规则也能正常工作。
      腕带规则用命名组 <code>(?&lt;mrn&gt;...)</code> 与可选 <code>(?&lt;visit&gt;...)</code>；床位规则用 <code>(?&lt;code&gt;...)</code> 提取床位/设备编码。
    </p>

    <div v-if="error" class="error-bar">{{ error }}</div>

    <!-- 测试工具 -->
    <section class="panel test-panel">
      <h3>规则测试</h3>
      <div class="test-row">
        <select v-model="testType">
          <option value="Patient">腕带 (Patient)</option>
          <option value="Bed">床位/设备 (Bed)</option>
        </select>
        <input v-model="testText" placeholder="粘贴一个真实条码进行测试" @keyup.enter="runTest" />
        <button class="btn btn-primary" :disabled="testing" @click="runTest">{{ testing ? '测试中' : '测试' }}</button>
      </div>
      <div v-if="testResult" class="test-result" :class="{ ok: testResult.matched, bad: testResult && !testResult.matched && !testResult.error }">
        <template v-if="testResult.error">解析出错：{{ testResult.error }}</template>
        <template v-else-if="testResult.ruleType === 'Bed'">
          提取编码：<b>{{ testResult.code || '(空)' }}</b>
          · 匹配床位：<b>{{ testResult.matchedBed ? (testResult.matchedBed.bedLabel || testResult.matchedBed.philipsLocationValue) : '未匹配到床位映射' }}</b>
        </template>
        <template v-else>
          MRN：<b>{{ testResult.mrn || '(空)' }}</b> · Visit：<b>{{ testResult.visitNumber || '(空)' }}</b>
        </template>
      </div>
    </section>

    <!-- 规则列表 -->
    <section class="panel">
      <table v-if="rules.length">
        <thead>
          <tr>
            <th>启用</th>
            <th>优先级</th>
            <th>类型</th>
            <th>名称</th>
            <th>正则</th>
            <th>去前缀</th>
            <th>示例</th>
            <th>更新时间</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in rules" :key="r.id" :class="{ disabled: !r.isEnabled }">
            <td><span :class="['badge', r.isEnabled ? 'on' : 'off']">{{ r.isEnabled ? '启用' : '停用' }}</span></td>
            <td>{{ r.priority }}</td>
            <td>{{ r.ruleType === 'Bed' ? '床位' : '腕带' }}</td>
            <td>{{ r.name }}</td>
            <td class="mono small">{{ r.pattern || '—' }}</td>
            <td class="mono small">{{ r.stripPrefixes || '—' }}</td>
            <td class="mono small">{{ r.sample || '—' }}</td>
            <td>{{ formatDateTime(r.updatedAt) }}</td>
            <td class="actions">
              <button class="btn btn-sm btn-light" @click="openEdit(r)">编辑</button>
              <button class="btn btn-sm btn-danger" @click="remove(r)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-else-if="!loading" class="empty">暂无规则（当前使用内置默认解析）</div>
      <div v-if="loading" class="loading">加载中...</div>
    </section>

    <!-- 编辑弹窗 -->
    <div v-if="editing" class="modal-mask" @click.self="closeEdit">
      <div class="modal">
        <div class="modal-head">
          <h3>{{ editing.id ? '编辑规则' : '新增规则' }}</h3>
          <button class="modal-close" @click="closeEdit">×</button>
        </div>
        <div class="modal-body">
          <label>规则名称 *<input v-model="editing.name" placeholder="如：本院腕带格式" /></label>
          <div class="grid2">
            <label>类型
              <select v-model="editing.ruleType">
                <option value="Patient">腕带 (Patient)</option>
                <option value="Bed">床位/设备 (Bed)</option>
              </select>
            </label>
            <label>优先级<input v-model.number="editing.priority" type="number" /></label>
          </div>
          <label>正则表达式
            <input v-model="editing.pattern" class="mono" :placeholder="editing.ruleType === 'Bed' ? '^BED-(?&lt;code&gt;.+)$' : '^(?&lt;mrn&gt;\\d+)(?:-(?&lt;visit&gt;\\d+))?$'" />
          </label>
          <label>去前缀（逗号分隔，可选）<input v-model="editing.stripPrefixes" class="mono" placeholder="PAT-,MRN:" /></label>
          <label>示例条码（可选）<input v-model="editing.sample" /></label>
          <label>备注（可选）<input v-model="editing.remark" /></label>
          <label class="check"><input v-model="editing.isEnabled" type="checkbox" /> 启用此规则</label>
        </div>
        <div class="modal-foot">
          <button class="btn btn-light" @click="closeEdit">取消</button>
          <button class="btn btn-primary" :disabled="saving" @click="save">{{ saving ? '保存中' : '保存' }}</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.auto-page { max-width: 1280px; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
.header-actions { display: flex; gap: 8px; }
.hint { font-size: 12px; color: var(--text-secondary); background: #f1f5f9; border-radius: var(--radius-sm); padding: 10px 12px; margin-bottom: 14px; line-height: 1.6; }
.hint code { background: #e2e8f0; padding: 1px 5px; border-radius: 4px; font-family: ui-monospace, Menlo, monospace; }
.panel { background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); padding: 16px; margin-bottom: 16px; }
.panel h3 { font-size: 15px; margin-bottom: 12px; }
.test-row { display: flex; gap: 8px; }
.test-row input { flex: 1; }
.test-result { margin-top: 10px; font-size: 13px; padding: 10px 12px; border-radius: var(--radius-sm); background: #f8fafc; }
.test-result.ok { background: #ecfdf5; color: #047857; }
.test-result.bad { background: #fef2f2; color: #b91c1c; }
table { width: 100%; border-collapse: collapse; font-size: 13px; }
th, td { padding: 9px 8px; border-bottom: 1px solid var(--border-color); text-align: left; vertical-align: top; }
th { color: var(--text-secondary); background: #f8fafc; }
tr.disabled { opacity: 0.55; }
.mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
.small { font-size: 12px; word-break: break-all; max-width: 220px; }
.badge { padding: 2px 8px; border-radius: 10px; font-size: 12px; }
.badge.on { background: #dcfce7; color: #166534; }
.badge.off { background: #f1f5f9; color: #64748b; }
.actions { display: flex; gap: 6px; white-space: nowrap; }
.btn { border: none; border-radius: var(--radius-sm); padding: 7px 14px; cursor: pointer; font-size: 13px; }
.btn-sm { padding: 4px 10px; font-size: 12px; }
.btn-primary { background: var(--accent); color: #fff; }
.btn-secondary { background: #eef2ff; color: #4338ca; }
.btn-light { background: #eef2ff; color: #4338ca; }
.btn-danger { background: #fee2e2; color: #b91c1c; }
.btn:disabled { opacity: 0.6; cursor: not-allowed; }
.empty, .loading { padding: 24px; text-align: center; color: var(--text-secondary); }
.error-bar { background: #fef2f2; color: #b91c1c; padding: 10px 12px; border-radius: var(--radius-sm); margin-bottom: 12px; }
.modal-mask { position: fixed; inset: 0; background: rgba(15,23,42,0.45); display: flex; align-items: center; justify-content: center; z-index: 1000; }
.modal { background: #fff; border-radius: 12px; width: 560px; max-width: 92vw; max-height: 88vh; display: flex; flex-direction: column; box-shadow: 0 20px 60px rgba(0,0,0,0.25); }
.modal-head { display: flex; align-items: center; justify-content: space-between; padding: 14px 18px; border-bottom: 1px solid var(--border-color); }
.modal-head h3 { font-size: 16px; }
.modal-close { border: none; background: transparent; font-size: 22px; cursor: pointer; color: var(--text-secondary); }
.modal-body { padding: 16px 18px; overflow-y: auto; display: flex; flex-direction: column; gap: 12px; }
.modal-foot { display: flex; justify-content: flex-end; gap: 8px; padding: 12px 18px; border-top: 1px solid var(--border-color); }
.grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
label { display: flex; flex-direction: column; gap: 6px; font-size: 12px; color: var(--text-secondary); }
label.check { flex-direction: row; align-items: center; gap: 8px; font-size: 13px; color: var(--text-primary); }
input, select { padding: 8px 10px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 13px; }
</style>
