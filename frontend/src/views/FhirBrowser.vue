<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  fhirObservationRead,
  fhirObservationSearch,
  fhirPatientRead,
  fhirPatientSearch,
} from '../api'
import { formatDateTime } from '../utils/time'

type ResourceType = 'Patient' | 'Observation'
type ViewMode = 'summary' | 'json'

const resourceType = ref<ResourceType>('Patient')
const mode = ref<ViewMode>('summary')
const patientIdentifier = ref('')
const patientName = ref('')
const patientId = ref('')
const observationPatient = ref('')
const observationCode = ref('')
const observationDate = ref('')
const directId = ref('')
const bundle = ref<any>(null)
const selected = ref<any>(null)
const loading = ref(false)
const loadingDetail = ref(false)
const error = ref('')

const entries = computed(() => bundle.value?.entry ?? [])
const total = computed(() => bundle.value?.total ?? entries.value.length)

watch(resourceType, () => {
  error.value = ''
  bundle.value = null
  selected.value = null
  directId.value = ''
})

function resourceOf(entry: any) {
  return entry?.resource ?? entry
}

function patientTitle(p: any) {
  const name = p?.name?.[0]
  const given = Array.isArray(name?.given) ? name.given.join('') : ''
  return name ? `${name.family ?? ''}${given}` || p.id : p?.id ?? '-'
}

function patientMeta(p: any) {
  const parts = [p?.gender, p?.birthDate, p?.identifier?.[0]?.value].filter(Boolean)
  return parts.length ? parts.join(' / ') : 'Patient'
}

function observationTitle(o: any) {
  return o?.code?.text || o?.code?.coding?.[0]?.display || o?.code?.coding?.[0]?.code || o?.id || '-'
}

function observationValue(o: any) {
  if (o?.valueQuantity) {
    return `${o.valueQuantity.value ?? ''} ${o.valueQuantity.unit ?? ''}`.trim()
  }
  return o?.valueString ?? '-'
}

function selectEntry(entry: any) {
  selected.value = resourceOf(entry)
}

async function search() {
  loading.value = true
  error.value = ''
  bundle.value = null
  selected.value = null
  try {
    if (resourceType.value === 'Patient') {
      bundle.value = await fhirPatientSearch({
        _id: patientId.value,
        identifier: patientIdentifier.value,
        name: patientName.value,
      })
    } else {
      bundle.value = await fhirObservationSearch({
        patient: observationPatient.value,
        code: observationCode.value,
        date: observationDate.value,
      })
    }
    if (entries.value.length) selectEntry(entries.value[0])
  } catch (e: any) {
    error.value = e.message || '查询失败'
  } finally {
    loading.value = false
  }
}

async function readById() {
  const id = directId.value.trim()
  if (!id) {
    error.value = '请输入资源 ID'
    return
  }
  loadingDetail.value = true
  error.value = ''
  try {
    selected.value = resourceType.value === 'Patient'
      ? await fhirPatientRead(id)
      : await fhirObservationRead(id)
    bundle.value = {
      resourceType: 'Bundle',
      type: 'searchset',
      total: 1,
      entry: [{ resource: selected.value }],
    }
  } catch (e: any) {
    error.value = e.message || '读取失败'
  } finally {
    loadingDetail.value = false
  }
}

function clearSearch() {
  patientIdentifier.value = ''
  patientName.value = ''
  patientId.value = ''
  observationPatient.value = ''
  observationCode.value = ''
  observationDate.value = ''
  directId.value = ''
  bundle.value = null
  selected.value = null
  error.value = ''
}
</script>

<template>
  <div class="fhir-page">
    <div class="page-header">
      <div>
        <h2>FHIR R4 浏览器</h2>
        <p>Patient / Observation</p>
      </div>
      <div class="mode-switch">
        <button :class="{ active: mode === 'summary' }" @click="mode = 'summary'">摘要</button>
        <button :class="{ active: mode === 'json' }" @click="mode = 'json'">JSON</button>
      </div>
    </div>

    <section class="query-panel">
      <div class="toolbar">
        <button :class="['resource-tab', { active: resourceType === 'Patient' }]" @click="resourceType = 'Patient'">Patient</button>
        <button :class="['resource-tab', { active: resourceType === 'Observation' }]" @click="resourceType = 'Observation'">Observation</button>
      </div>

      <div v-if="resourceType === 'Patient'" class="query-grid">
        <label>Patient ID <input v-model="patientId" class="input" placeholder="_id" @keyup.enter="search" /></label>
        <label>Identifier <input v-model="patientIdentifier" class="input" placeholder="MRN / PID" @keyup.enter="search" /></label>
        <label>Name <input v-model="patientName" class="input" placeholder="姓名" @keyup.enter="search" /></label>
      </div>

      <div v-else class="query-grid">
        <label>Patient <input v-model="observationPatient" class="input" placeholder="Patient ID" @keyup.enter="search" /></label>
        <label>Code <input v-model="observationCode" class="input" placeholder="HR / SPO2 / 0002-4182" @keyup.enter="search" /></label>
        <label>Date <input v-model="observationDate" class="input" type="date" @keyup.enter="search" /></label>
      </div>

      <div class="direct-row">
        <label>Read ID <input v-model="directId" class="input" :placeholder="resourceType === 'Patient' ? 'Patient ID' : 'Observation ID'" @keyup.enter="readById" /></label>
        <div class="actions">
          <button class="btn btn-secondary" @click="clearSearch">清空</button>
          <button class="btn" :disabled="loadingDetail" @click="readById">{{ loadingDetail ? '读取中...' : '读取' }}</button>
          <button class="btn btn-primary" :disabled="loading" @click="search">{{ loading ? '查询中...' : '查询' }}</button>
        </div>
      </div>
    </section>

    <div v-if="error" class="error-bar">{{ error }}</div>

    <div class="browser-grid">
      <section class="result-list">
        <div class="list-head">
          <span>结果</span>
          <strong>{{ total }}</strong>
        </div>
        <div v-if="loading" class="empty">查询中...</div>
        <div v-else-if="!entries.length" class="empty">暂无资源</div>
        <template v-else>
          <button
            v-for="entry in entries"
            :key="resourceOf(entry).resourceType + '-' + resourceOf(entry).id"
            :class="['result-item', { active: selected?.id === resourceOf(entry).id }]"
            @click="selectEntry(entry)"
          >
            <template v-if="resourceOf(entry).resourceType === 'Patient'">
              <span class="item-title">{{ patientTitle(resourceOf(entry)) }}</span>
              <span class="item-meta">{{ patientMeta(resourceOf(entry)) }}</span>
            </template>
            <template v-else>
              <span class="item-title">{{ observationTitle(resourceOf(entry)) }}</span>
              <span class="item-meta">{{ observationValue(resourceOf(entry)) }} · {{ resourceOf(entry).subject?.reference }}</span>
            </template>
          </button>
        </template>
      </section>

      <section class="detail-panel">
        <div v-if="!selected" class="empty detail-empty">请选择资源</div>

        <template v-else-if="mode === 'summary'">
          <div class="detail-head">
            <div>
              <span class="resource-type">{{ selected.resourceType }}</span>
              <h3>{{ selected.resourceType === 'Patient' ? patientTitle(selected) : observationTitle(selected) }}</h3>
            </div>
            <code>{{ selected.id }}</code>
          </div>

          <div v-if="selected.resourceType === 'Patient'" class="summary-grid">
            <div><span>Identifier</span><strong>{{ selected.identifier?.[0]?.value || '-' }}</strong></div>
            <div><span>Gender</span><strong>{{ selected.gender || '-' }}</strong></div>
            <div><span>Birth Date</span><strong>{{ selected.birthDate || '-' }}</strong></div>
            <div><span>Name</span><strong>{{ patientTitle(selected) }}</strong></div>
          </div>

          <div v-else class="summary-grid">
            <div><span>Value</span><strong>{{ observationValue(selected) }}</strong></div>
            <div><span>Status</span><strong>{{ selected.status || '-' }}</strong></div>
            <div><span>Subject</span><strong>{{ selected.subject?.reference || '-' }}</strong></div>
            <div><span>Time</span><strong>{{ formatDateTime(selected.effectiveDateTime) }}</strong></div>
            <div><span>Code</span><strong>{{ selected.code?.coding?.[0]?.code || '-' }}</strong></div>
            <div><span>System</span><strong>{{ selected.code?.coding?.[0]?.system || '-' }}</strong></div>
          </div>
        </template>

        <pre v-else class="json-view">{{ JSON.stringify(selected, null, 2) }}</pre>
      </section>
    </div>
  </div>
</template>

<style scoped>
.fhir-page { max-width: 1280px; }
.page-header { display: flex; align-items: flex-start; justify-content: space-between; margin-bottom: 16px; }
.page-header h2 { font-size: 22px; color: var(--text-primary); margin: 0 0 4px; }
.page-header p { margin: 0; color: var(--text-muted); font-size: 13px; }
.mode-switch { display: inline-flex; border: 1px solid var(--border-color); border-radius: var(--radius-sm); overflow: hidden; }
.mode-switch button, .resource-tab { border: 0; background: var(--card-bg); color: var(--text-secondary); padding: 8px 14px; cursor: pointer; font-size: 13px; }
.mode-switch button.active, .resource-tab.active { background: var(--accent); color: #fff; }
.query-panel { background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); padding: 16px; margin-bottom: 16px; }
.toolbar { display: flex; margin-bottom: 14px; }
.resource-tab { border: 1px solid var(--border-color); }
.resource-tab:first-child { border-radius: var(--radius-sm) 0 0 var(--radius-sm); }
.resource-tab:last-child { border-left: 0; border-radius: 0 var(--radius-sm) var(--radius-sm) 0; }
.query-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; margin-bottom: 12px; }
.direct-row { display: grid; grid-template-columns: minmax(220px, 1fr) auto; gap: 12px; align-items: end; }
label { display: flex; flex-direction: column; gap: 4px; font-size: 12px; color: var(--text-secondary); font-weight: 500; }
.input { height: 36px; padding: 7px 10px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 13px; background: var(--card-bg); color: var(--text-primary); }
.actions { display: flex; gap: 8px; }
.btn { height: 36px; padding: 0 16px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); background: var(--card-bg); color: var(--text-primary); cursor: pointer; font-size: 13px; }
.btn:disabled { opacity: .55; cursor: not-allowed; }
.btn-primary { background: var(--accent); color: #fff; border-color: var(--accent); }
.btn-secondary { background: var(--gray-bg); }
.error-bar { background: #fef2f2; color: #dc2626; padding: 10px 14px; border-radius: var(--radius); margin-bottom: 16px; font-size: 13px; border: 1px solid #fca5a5; }
.browser-grid { display: grid; grid-template-columns: 360px minmax(0, 1fr); gap: 16px; }
.result-list, .detail-panel { background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); min-height: 420px; overflow: hidden; }
.list-head { display: flex; justify-content: space-between; padding: 12px 14px; border-bottom: 1px solid var(--border-color); color: var(--text-secondary); font-size: 13px; }
.result-item { width: 100%; display: flex; flex-direction: column; gap: 4px; text-align: left; border: 0; border-bottom: 1px solid var(--border-color); background: transparent; padding: 12px 14px; cursor: pointer; }
.result-item:hover, .result-item.active { background: #eef2ff; }
.item-title { color: var(--text-primary); font-size: 14px; font-weight: 600; }
.item-meta { color: var(--text-muted); font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.empty { text-align: center; padding: 40px 16px; color: var(--text-muted); font-size: 13px; }
.detail-empty { padding-top: 160px; }
.detail-head { display: flex; justify-content: space-between; gap: 12px; padding: 18px; border-bottom: 1px solid var(--border-color); }
.detail-head h3 { margin: 4px 0 0; font-size: 18px; color: var(--text-primary); }
.detail-head code { align-self: flex-start; background: var(--gray-bg); padding: 4px 8px; border-radius: var(--radius-sm); font-size: 12px; color: var(--text-secondary); }
.resource-type { color: var(--accent); font-weight: 700; font-size: 12px; }
.summary-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; padding: 18px; }
.summary-grid div { border: 1px solid var(--border-color); border-radius: var(--radius-sm); padding: 12px; min-height: 68px; }
.summary-grid span { display: block; color: var(--text-muted); font-size: 12px; margin-bottom: 8px; }
.summary-grid strong { color: var(--text-primary); font-size: 14px; overflow-wrap: anywhere; }
.json-view { margin: 0; padding: 16px; max-height: 620px; overflow: auto; font-size: 12px; line-height: 1.5; color: var(--text-primary); }
@media (max-width: 900px) {
  .query-grid, .direct-row, .browser-grid { grid-template-columns: 1fr; }
  .actions { justify-content: flex-end; }
}
</style>
