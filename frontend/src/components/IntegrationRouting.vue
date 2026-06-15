<script setup lang="ts">
import { ref, onMounted } from 'vue'
import {
  fetchRoutingSettings,
  saveRoutingSettings,
  fetchRoutingRules,
  createRoutingRule,
  updateRoutingRule,
  deleteRoutingRule,
  testRoutingMatch,
} from '../api'

const settings = ref({ routingEnabled: false, routingTraceEnabled: true })
const routingActive = ref(false)
const rules = ref<any[]>([])
const loading = ref(true)
const saving = ref(false)
const testHl7 = ref(`MSH|^~\\&|HIS|FAC|HL7GW||20260615120000||ADT^A01|T001|P|2.3\rPID|||T001^^^^MR`)
const testResult = ref<any>(null)
const editRule = ref<any>(null)
const error = ref('')

const actionOptions = [
  { value: 'LegacyDefault', label: 'LegacyDefault（与升级前相同）' },
  { value: 'ForwardAdt', label: 'ForwardAdt — 入 ADT 队列' },
  { value: 'SkipForward', label: 'SkipForward — 不转发 ADT' },
  { value: 'Webhook', label: 'Webhook — 仅 HTTP 推送' },
  { value: 'ForwardAdtWebhook', label: 'ForwardAdt + Webhook' },
]

function blankRule() {
  return {
    name: '',
    priority: 100,
    isEnabled: true,
    messageType: 'ADT',
    triggerEvent: '',
    sourceIpPattern: '',
    sendingApp: '',
    sendingFacility: '',
    action: 'ForwardAdt',
    webhookUrl: '',
    transformJson: '',
    remark: '',
  }
}

async function load() {
  loading.value = true
  try {
    const s = await fetchRoutingSettings()
    settings.value = s.settings || settings.value
    routingActive.value = !!s.routingActive
    rules.value = await fetchRoutingRules()
    error.value = ''
  } catch (e: any) {
    error.value = e.message || '加载失败'
  }
  loading.value = false
}

async function saveSettings() {
  saving.value = true
  try {
    const res = await saveRoutingSettings(settings.value)
    settings.value = res.settings
    routingActive.value = !!res.routingActive
    error.value = ''
  } catch (e: any) {
    error.value = e.message || '保存失败'
  }
  saving.value = false
}

function startCreate() {
  editRule.value = blankRule()
}

function startEdit(r: any) {
  editRule.value = { ...r }
}

async function saveRule() {
  if (!editRule.value?.name?.trim()) {
    error.value = '规则名称必填'
    return
  }
  saving.value = true
  try {
    if (editRule.value.id) {
      await updateRoutingRule(editRule.value.id, editRule.value)
    } else {
      await createRoutingRule(editRule.value)
    }
    editRule.value = null
    await load()
  } catch (e: any) {
    error.value = e.message || '保存规则失败'
  }
  saving.value = false
}

async function removeRule(id: number) {
  if (!confirm('删除该路由规则？')) return
  await deleteRoutingRule(id)
  await load()
}

async function runTest() {
  testResult.value = null
  try {
    testResult.value = await testRoutingMatch({ hl7: testHl7.value })
  } catch (e: any) {
    error.value = e.message || '测试失败'
  }
}

onMounted(load)
</script>

<template>
  <section class="routing-module">
    <div class="module-head">
      <div>
        <h3>入站路由（Phase 2 · 模块化）</h3>
        <p class="sub">
          默认<strong>关闭</strong>。未开启或无启用规则时，MLLP/ADT 行为与升级前完全一致。
        </p>
      </div>
      <span class="status-pill" :class="{ on: routingActive }">
        {{ routingActive ? '路由已生效' : '路由未生效' }}
      </span>
    </div>

    <div v-if="error" class="alert">{{ error }}</div>

    <div class="settings-box">
      <label class="check">
        <input v-model="settings.routingEnabled" type="checkbox" @change="saveSettings" />
        启用路由引擎（须至少一条已启用规则）
      </label>
      <label class="check">
        <input v-model="settings.routingTraceEnabled" type="checkbox" @change="saveSettings" />
        路由匹配写入 Trace
      </label>
    </div>

    <div class="toolbar">
      <button class="btn primary" @click="startCreate">新建规则</button>
      <button class="btn" @click="load">刷新</button>
    </div>

    <div v-if="loading" class="muted">加载中…</div>
    <table v-else class="rules-table">
      <thead>
        <tr>
          <th>优先级</th><th>名称</th><th>条件</th><th>动作</th><th>启用</th><th></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="r in rules" :key="r.id">
          <td>{{ r.priority }}</td>
          <td>{{ r.name }}</td>
          <td class="cond">
            <span v-if="r.messageType">{{ r.messageType }}</span>
            <span v-if="r.triggerEvent">^{{ r.triggerEvent }}</span>
            <span v-if="r.sourceIpPattern"> · IP {{ r.sourceIpPattern }}</span>
          </td>
          <td><code>{{ r.action }}</code></td>
          <td>{{ r.isEnabled ? '是' : '否' }}</td>
          <td class="actions">
            <button class="link" @click="startEdit(r)">编辑</button>
            <button class="link danger" @click="removeRule(r.id)">删</button>
          </td>
        </tr>
      </tbody>
    </table>
    <div v-if="!loading && !rules.length" class="muted">暂无规则 — 当前走 Legacy 默认路径</div>

    <div v-if="editRule" class="editor">
      <h4>{{ editRule.id ? '编辑规则' : '新建规则' }}</h4>
      <div class="grid">
        <label>名称<input v-model="editRule.name" /></label>
        <label>优先级<input v-model.number="editRule.priority" type="number" /></label>
        <label>消息类型<input v-model="editRule.messageType" placeholder="ADT 或留空" /></label>
        <label>Trigger<input v-model="editRule.triggerEvent" placeholder="A01 或留空" /></label>
        <label>来源 IP<input v-model="editRule.sourceIpPattern" placeholder="192.168.*" /></label>
        <label>动作
          <select v-model="editRule.action">
            <option v-for="o in actionOptions" :key="o.value" :value="o.value">{{ o.label }}</option>
          </select>
        </label>
        <label class="wide">Webhook URL<input v-model="editRule.webhookUrl" /></label>
        <label class="wide">Transform JSON<textarea v-model="editRule.transformJson" rows="2" placeholder='[{"segment":"MSH","field":4,"value":"NEW"}]' /></label>
        <label class="wide">备注<input v-model="editRule.remark" /></label>
        <label class="check"><input v-model="editRule.isEnabled" type="checkbox" /> 启用</label>
      </div>
      <div class="editor-actions">
        <button class="btn primary" :disabled="saving" @click="saveRule">保存</button>
        <button class="btn" @click="editRule = null">取消</button>
      </div>
    </div>

    <div class="test-box">
      <h4>规则试匹配</h4>
      <textarea v-model="testHl7" rows="4" spellcheck="false" />
      <button class="btn" @click="runTest">测试匹配</button>
      <pre v-if="testResult" class="test-out">{{ JSON.stringify(testResult, null, 2) }}</pre>
    </div>
  </section>
</template>

<style scoped>
.routing-module { margin-top: 8px; }
.module-head { display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; margin-bottom: 12px; }
.module-head h3 { margin: 0 0 4px; }
.sub { margin: 0; color: #6b7280; font-size: 0.85rem; }
.status-pill { font-size: 0.75rem; padding: 4px 10px; border-radius: 999px; background: #f3f4f6; color: #6b7280; white-space: nowrap; }
.status-pill.on { background: #dbeafe; color: #1d4ed8; }
.settings-box { display: flex; flex-wrap: wrap; gap: 16px; margin-bottom: 12px; padding: 10px; background: #f9fafb; border-radius: 8px; }
.check { display: flex; align-items: center; gap: 8px; font-size: 0.9rem; }
.toolbar { display: flex; gap: 8px; margin-bottom: 10px; }
.rules-table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
.rules-table th, .rules-table td { border-bottom: 1px solid #eee; padding: 8px 6px; text-align: left; }
.cond { color: #4b5563; }
.actions { white-space: nowrap; }
.link { border: none; background: none; color: #2563eb; cursor: pointer; margin-right: 8px; }
.link.danger { color: #dc2626; }
.editor { margin-top: 16px; padding: 14px; border: 1px solid #e5e7eb; border-radius: 8px; background: #fafbfc; }
.grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
.grid label { display: flex; flex-direction: column; gap: 4px; font-size: 0.8rem; color: #4b5563; }
.grid .wide { grid-column: 1 / -1; }
.grid input, .grid select, .grid textarea { padding: 6px 8px; border: 1px solid #d1d5db; border-radius: 6px; font-size: 0.85rem; }
.editor-actions { margin-top: 10px; display: flex; gap: 8px; }
.test-box { margin-top: 20px; padding-top: 16px; border-top: 1px dashed #e5e7eb; }
.test-box textarea { width: 100%; box-sizing: border-box; font-family: ui-monospace, monospace; font-size: 0.82rem; margin: 8px 0; }
.test-out { background: #0f172a; color: #e2e8f0; padding: 10px; border-radius: 8px; font-size: 0.78rem; overflow: auto; max-height: 220px; }
.btn { border: 1px solid #d1d5db; background: #fff; border-radius: 6px; padding: 6px 12px; cursor: pointer; }
.btn.primary { background: #2563eb; color: #fff; border-color: #2563eb; }
.alert { background: #fef2f2; color: #991b1b; padding: 8px 10px; border-radius: 6px; margin-bottom: 10px; }
.muted { color: #9ca3af; font-size: 0.85rem; }
</style>
