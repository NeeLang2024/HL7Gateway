<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { dischargeAutoAdt, fetchAutoAdtBoard } from '../api'
import { formatDateTime } from '../utils/time'

const board = ref<any>(null)
const loading = ref(false)
const error = ref('')
const notice = ref('')
const busyBedId = ref<number | null>(null)
const careArea = ref('')
const includeDisabled = ref(false)
const onlyOccupied = ref(false)

async function load() {
  loading.value = true
  error.value = ''
  try {
    board.value = await fetchAutoAdtBoard({
      includeDisabled: includeDisabled.value,
      careArea: careArea.value || undefined,
    })
  } catch (err: any) {
    error.value = err.message || '加载床位看板失败'
    board.value = null
  } finally {
    loading.value = false
  }
}

const careAreas = computed<string[]>(() => board.value?.careAreas || [])

const items = computed<any[]>(() => {
  const list: any[] = board.value?.items || []
  return onlyOccupied.value ? list.filter((i) => i.occupied) : list
})

async function quickDischarge(item: any) {
  if (!item.patientId || !item.visitId) {
    error.value = '该绑定缺少 MRN 或 Visit Number，无法快捷出院'
    return
  }
  if (!confirm(`确认为 ${item.patientName || item.patientId} 办理出院 A03？\n床位：${item.philipsLocationValue}`)) return
  busyBedId.value = item.id
  error.value = ''
  notice.value = ''
  try {
    await dischargeAutoAdt({
      patient: { mrn: item.patientId, visitNumber: item.visitId },
      priority: 0,
    })
    notice.value = `已为 ${item.patientName || item.patientId} 入队出院消息 A03`
    await load()
  } catch (err: any) {
    error.value = err.message || '快捷出院失败'
  } finally {
    busyBedId.value = null
  }
}

onMounted(load)
</script>

<template>
  <div class="auto-page">
    <div class="page-header">
      <h2>Auto ADT 床位看板</h2>
      <button class="btn btn-secondary" :disabled="loading" @click="load">刷新</button>
    </div>

    <div class="status-strip">
      <span class="legend"><span class="dot free"></span> 空闲 {{ board?.free ?? 0 }}</span>
      <span class="legend"><span class="dot busy"></span> 占用 {{ board?.occupied ?? 0 }}</span>
      <span class="legend">床位合计 {{ board?.total ?? 0 }}</span>
      <span class="spacer"></span>
      <label class="inline">
        护理单元
        <select v-model="careArea" @change="load">
          <option value="">全部</option>
          <option v-for="c in careAreas" :key="c" :value="c">{{ c }}</option>
        </select>
      </label>
      <label class="inline check"><input type="checkbox" v-model="onlyOccupied" /> 只看占用</label>
      <label class="inline check"><input type="checkbox" v-model="includeDisabled" @change="load" /> 含停用床位</label>
    </div>

    <div v-if="notice" class="notification">{{ notice }}</div>
    <div v-if="error" class="error-bar">{{ error }}</div>

    <div v-if="items.length" class="bed-grid">
      <div
        v-for="item in items"
        :key="item.id"
        :class="['bed-card', item.occupied ? 'busy' : 'free', { disabled: !item.isEnabled }]"
      >
        <div class="bed-card-head">
          <strong>{{ item.bedLabel || `${item.careArea || '-'} / ${item.room || '-'} / ${item.bed || '-'}` }}</strong>
          <span :class="['badge', item.occupied ? 'busy' : 'free']">{{ item.occupied ? '占用' : '空闲' }}</span>
        </div>
        <div class="bed-loc mono">{{ item.philipsLocationValue }}</div>

        <div v-if="item.occupied" class="bed-patient">
          <div class="pname">{{ item.patientName || '(未填姓名)' }}</div>
          <div class="pmeta">MRN：{{ item.patientId }}</div>
          <div class="pmeta">Visit：{{ item.visitId }}</div>
          <div class="pmeta">入住：{{ formatDateTime(item.bindTime) }}</div>
        </div>
        <div v-else class="bed-empty">空闲</div>

        <div class="bed-card-foot">
          <span class="dev">{{ item.deviceCode || '—' }}</span>
          <button
            v-if="item.occupied"
            class="btn btn-sm btn-danger"
            :disabled="busyBedId === item.id"
            @click="quickDischarge(item)"
          >{{ busyBedId === item.id ? '处理中' : '出院 A03' }}</button>
        </div>
      </div>
    </div>

    <div v-else-if="!loading" class="empty">暂无床位，请先在「床位映射」中维护 Philips 床位</div>
    <div v-if="loading" class="loading">加载中...</div>
  </div>
</template>

<style scoped>
.auto-page { max-width: 1280px; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
.status-strip { display: flex; flex-wrap: wrap; align-items: center; gap: 14px; background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); padding: 12px 16px; margin-bottom: 16px; font-size: 13px; }
.legend { display: inline-flex; align-items: center; gap: 6px; }
.spacer { flex: 1; }
.inline { display: inline-flex; flex-direction: row; align-items: center; gap: 6px; color: var(--text-secondary); }
.inline.check { gap: 4px; }
.inline select { padding: 6px 8px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 13px; }
.dot { width: 10px; height: 10px; border-radius: 50%; display: inline-block; }
.dot.free { background: #22c55e; }
.dot.busy { background: #3b82f6; }
.bed-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(230px, 1fr)); gap: 14px; }
.bed-card { background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); padding: 14px; border-left: 4px solid #22c55e; display: flex; flex-direction: column; gap: 8px; }
.bed-card.busy { border-left-color: #3b82f6; }
.bed-card.disabled { opacity: 0.55; }
.bed-card-head { display: flex; justify-content: space-between; align-items: center; gap: 8px; }
.bed-card-head strong { font-size: 14px; }
.badge { padding: 2px 8px; border-radius: 999px; font-size: 12px; white-space: nowrap; }
.badge.free { background: #dcfce7; color: #166534; }
.badge.busy { background: #dbeafe; color: #1e40af; }
.bed-loc { font-size: 12px; color: var(--text-secondary); word-break: break-all; }
.bed-patient { background: #f8fafc; border: 1px solid var(--border-color); border-radius: var(--radius-sm); padding: 10px; }
.pname { font-size: 15px; font-weight: 600; margin-bottom: 4px; }
.pmeta { font-size: 12px; color: var(--text-secondary); }
.bed-empty { padding: 14px; text-align: center; color: var(--text-secondary); background: #f8fafc; border-radius: var(--radius-sm); }
.bed-card-foot { display: flex; justify-content: space-between; align-items: center; margin-top: auto; }
.dev { font-size: 12px; color: var(--text-secondary); }
.mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
.notification { background: #ecfdf5; color: #047857; padding: 10px 12px; border-radius: var(--radius-sm); margin-bottom: 12px; }
.error-bar { background: #fef2f2; color: #b91c1c; padding: 10px 12px; border-radius: var(--radius-sm); margin-bottom: 12px; }
.empty, .loading { padding: 28px; text-align: center; color: var(--text-secondary); }
</style>
