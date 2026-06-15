<script setup lang="ts">
import { ref } from 'vue'
import { compareMessages } from '../api'

const id1 = ref('')
const id2 = ref('')
const result = ref<any>(null)
const loading = ref(false)
const error = ref('')

async function doCompare() {
  if (!id1.value || !id2.value) return
  loading.value = true
  error.value = ''
  result.value = null
  try {
    result.value = await compareMessages(id1.value, id2.value)
  } catch (e: any) {
    error.value = e.message
  }
  loading.value = false
}

function diffClass(left: any, right: any): string {
  if (left === undefined && right === undefined) return ''
  if (left === right) return 'diff-same'
  if (left === null || left === undefined || left === '') return 'diff-added'
  if (right === null || right === undefined || right === '') return 'diff-removed'
  return 'diff-changed'
}
</script>

<template>
  <div class="compare-page">
    <h2 class="page-title">消息对比</h2>

    <div class="compare-inputs">
      <div>
        <label>消息 ID 1</label>
        <input v-model="id1" class="input" placeholder="输入消息 ID" />
      </div>
      <div>
        <label>消息 ID 2</label>
        <input v-model="id2" class="input" placeholder="输入消息 ID" />
      </div>
      <button class="btn btn-primary" @click="doCompare" :disabled="loading">对比</button>
    </div>

    <div v-if="error" class="error-bar">{{ error }}</div>
    <div v-if="loading" class="loading">对比中...</div>

    <div v-if="result" class="compare-grid">
      <div class="card">
        <h3 class="card-title">消息 #{{ id1 }}</h3>
        <div class="msg-props">
          <div v-for="(v, k) in result.message1" :key="k" :class="['prop-row', diffClass(result.message1?.[k], result.message2?.[k])]">
            <span class="prop-key">{{ k }}</span>
            <span class="prop-val">{{ v ?? '(空)' }}</span>
          </div>
        </div>
      </div>
      <div class="card">
        <h3 class="card-title">消息 #{{ id2 }}</h3>
        <div class="msg-props">
          <div v-for="(v, k) in result.message2" :key="k" :class="['prop-row', diffClass(result.message2?.[k], result.message1?.[k])]">
            <span class="prop-key">{{ k }}</span>
            <span class="prop-val">{{ v ?? '(空)' }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.compare-page { max-width: 1200px; }
.page-title { font-size: 22px; color: var(--text-primary); margin-bottom: 24px; }
.compare-inputs { display: flex; gap: 12px; align-items: flex-end; margin-bottom: 24px; }
.compare-inputs label { display: block; font-size: 12px; color: var(--text-secondary); margin-bottom: 4px; }
.input { padding: 8px 12px; border: 1px solid var(--border-color); border-radius: var(--radius); font-size: 13px; background: var(--card-bg); color: var(--text-primary); width: 200px; }
.btn { padding: 8px 16px; border: 1px solid var(--border-color); border-radius: var(--radius); background: var(--card-bg); color: var(--text-primary); cursor: pointer; font-size: 13px; height: 36px; }
.btn-primary { background: var(--accent); color: #fff; border-color: var(--accent); }
.error-bar { background: #fef2f2; color: #dc2626; padding: 10px 16px; border-radius: var(--radius); margin-bottom: 16px; font-size: 13px; border: 1px solid #fca5a5; }
.loading { text-align: center; padding: 40px 0; color: var(--text-muted); }
.compare-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.card { background: var(--card-bg); border-radius: var(--radius); padding: 20px; box-shadow: var(--shadow); overflow: hidden; }
.card-title { font-size: 15px; font-weight: 600; margin-bottom: 16px; padding-bottom: 12px; border-bottom: 1px solid var(--border-color); color: var(--text-primary); }
.msg-props { font-size: 12px; font-family: monospace; }
.prop-row { display: flex; gap: 8px; padding: 4px 0; border-bottom: 1px solid var(--border-color); }
.prop-row:last-child { border-bottom: none; }
.prop-key { color: var(--text-secondary); min-width: 140px; flex-shrink: 0; }
.prop-val { color: var(--text-primary); word-break: break-all; }
.diff-added { background: #dcfce7; }
.diff-removed { background: #fef2f2; }
.diff-changed { background: #fef9c3; }
.diff-same { background: transparent; }
</style>
