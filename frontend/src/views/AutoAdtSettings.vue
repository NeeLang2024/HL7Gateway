<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { fetchAutoAdtFeatures, fetchAutoAdtPreflight, updateAutoAdtFeatures } from '../api'
import { formatDateTime } from '../utils/time'

const features = ref<any>({
  autoAdmitEnabled: false,
  autoAdmitConfirmSeconds: 3,
  strictAdmitValidation: true,
  requirePatientName: false,
  requireDateOfBirth: false,
  duplicateSubmitWindowSeconds: 60,
  hisAutoBindingEnabled: false,
  preflightCheckEnabled: true,
})
const preflight = ref<any>(null)
const loading = ref(false)
const saving = ref(false)
const checking = ref(false)
const error = ref('')
const notice = ref('')

async function loadFeatures() {
  loading.value = true
  error.value = ''
  try {
    features.value = { ...features.value, ...(await fetchAutoAdtFeatures()) }
  } catch (err: any) {
    error.value = err.message || '加载功能开关失败'
  } finally {
    loading.value = false
  }
}

async function saveFeatures() {
  saving.value = true
  error.value = ''
  notice.value = ''
  try {
    features.value = await updateAutoAdtFeatures(features.value)
    notice.value = '功能开关已保存（新开关立即生效，不影响已关闭项的现有行为）'
  } catch (err: any) {
    error.value = err.message || '保存失败'
  } finally {
    saving.value = false
  }
}

async function runPreflight() {
  checking.value = true
  error.value = ''
  try {
    preflight.value = await fetchAutoAdtPreflight()
  } catch (err: any) {
    error.value = err.message || '环境自检失败'
  } finally {
    checking.value = false
  }
}

onMounted(async () => {
  await loadFeatures()
  await runPreflight()
})
</script>

<template>
  <div class="auto-page">
    <div class="page-header">
      <h2>Auto ADT 功能开关</h2>
      <p>所有增强功能<strong>默认关闭</strong>。逐项开启并在现场验证后再扩大使用范围。</p>
    </div>

    <div v-if="error" class="error-bar">{{ error }}</div>
    <div v-if="notice" class="notice-bar">{{ notice }}</div>

    <section class="panel">
      <h3>扫码入院</h3>
      <label class="toggle-row">
        <input v-model="features.autoAdmitEnabled" type="checkbox" />
        <span>双码齐备后自动发起入院（带倒计时确认）</span>
      </label>
      <label class="field-row">
        <span>倒计时秒数</span>
        <input v-model.number="features.autoAdmitConfirmSeconds" type="number" min="0" max="30" />
        <small>0 = 立即执行；建议 3 秒</small>
      </label>
      <label class="toggle-row">
        <input v-model="features.strictAdmitValidation" type="checkbox" />
        <span>严格校验（启用下方必填项检查）</span>
      </label>
      <label class="toggle-row sub">
        <input v-model="features.requirePatientName" type="checkbox" :disabled="!features.strictAdmitValidation" />
        <span>要求病人姓名</span>
      </label>
      <label class="toggle-row sub">
        <input v-model="features.requireDateOfBirth" type="checkbox" :disabled="!features.strictAdmitValidation" />
        <span>要求出生日期</span>
      </label>
      <label class="field-row">
        <span>防重复提交窗口（秒）</span>
        <input v-model.number="features.duplicateSubmitWindowSeconds" type="number" min="0" max="600" />
        <small>相同病人+床位+事件在此时间内不可重复提交；0 = 不限制</small>
      </label>
    </section>

    <section class="panel">
      <h3>HIS 集成</h3>
      <label class="toggle-row">
        <input v-model="features.hisAutoBindingEnabled" type="checkbox" />
        <span>HIS 经 MLLP 发来 ADT^A01/A02/A03 时，自动维护床位绑定（看板同步）</span>
      </label>
      <p class="hint">关闭时行为与升级前完全一致：HIS ADT 仍入队转发，但不写 AutoAdtBindings。</p>
    </section>

    <div class="actions">
      <button class="btn btn-primary" :disabled="saving || loading" @click="saveFeatures">保存功能开关</button>
      <button class="btn btn-secondary" :disabled="checking" @click="runPreflight">重新环境自检</button>
    </div>

    <section v-if="preflight" class="panel preflight">
      <h3>环境自检 <span :class="['badge', preflight.ok ? 'ok' : 'warn']">{{ preflight.ok ? '通过' : '有问题' }}</span></h3>
      <p class="hint">检查时间：{{ formatDateTime(preflight.checkedAt) }}</p>
      <ul>
        <li v-for="(item, idx) in preflight.checks" :key="idx" :class="item.ok ? 'ok' : 'bad'">
          <strong>{{ item.name }}</strong> — {{ item.detail }}
        </li>
      </ul>
    </section>
  </div>
</template>

<style scoped>
.auto-page { display: flex; flex-direction: column; gap: 16px; }
.page-header h2 { margin: 0 0 6px; }
.page-header p { margin: 0; color: var(--text-muted); font-size: 14px; }
.panel { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 18px; }
.panel h3 { margin: 0 0 14px; font-size: 16px; }
.toggle-row { display: flex; align-items: flex-start; gap: 10px; margin-bottom: 12px; cursor: pointer; }
.toggle-row.sub { margin-left: 24px; }
.field-row { display: grid; grid-template-columns: 160px 100px 1fr; gap: 10px; align-items: center; margin-bottom: 12px; }
.field-row small { color: var(--text-muted); }
.hint { color: var(--text-muted); font-size: 13px; margin: 8px 0 0; }
.actions { display: flex; gap: 10px; }
.error-bar, .notice-bar { padding: 10px 14px; border-radius: 8px; }
.error-bar { background: #fef2f2; color: #b91c1c; border: 1px solid #fecaca; }
.notice-bar { background: #ecfdf5; color: #047857; border: 1px solid #a7f3d0; }
.preflight ul { list-style: none; padding: 0; margin: 12px 0 0; }
.preflight li { padding: 8px 0; border-bottom: 1px solid var(--border); font-size: 14px; }
.preflight li.ok strong { color: #059669; }
.preflight li.bad strong { color: #dc2626; }
.badge { font-size: 12px; padding: 2px 8px; border-radius: 999px; margin-left: 8px; }
.badge.ok { background: #d1fae5; color: #065f46; }
.badge.warn { background: #fef3c7; color: #92400e; }
@media (max-width: 800px) { .field-row { grid-template-columns: 1fr; } }
</style>
