<script setup lang="ts">
import { ref, onMounted } from 'vue'
import {
  fetchSystemSettings, updateSystemSettings, restartService,
  exportConfig, importConfig
} from '../api'

const loading = ref(true)
const saving = ref(false)
const restarting = ref(false)
const configExportLoading = ref(false)
const configImportLoading = ref(false)
const message = ref('')
const messageType = ref<'success' | 'error' | 'info'>('info')

const adtListenerPort = ref(9912)
const adtListenerEnabled = ref(false)
const adtTargetHost = ref('')
const adtTargetPort = ref('')
const hifBridgeEnabled = ref(true)
const hifBridgeBaseUrl = ref('http://localhost:5080/')
const hifBridgeTimeoutSeconds = ref(10)
const adtWcfSendMode = ref('ServiceExecute')
const adtCallbackOperation = ref('OnPIChange')
const adtCallbackAction = ref('')

const importedFile = ref<File | null>(null)

onMounted(async () => {
  try {
    const data = await fetchSystemSettings()
    adtListenerPort.value = data.adtListenerPort ?? 9912
    adtListenerEnabled.value = data.adtListenerEnabled ?? false
    adtTargetHost.value = data.adtTargetHost ?? ''
    adtTargetPort.value = data.adtTargetPort ?? ''
    hifBridgeEnabled.value = data.hifBridgeEnabled ?? true
    hifBridgeBaseUrl.value = data.hifBridgeBaseUrl ?? 'http://localhost:5080/'
    hifBridgeTimeoutSeconds.value = data.hifBridgeTimeoutSeconds ?? 10
    adtWcfSendMode.value = data.adtWcfSendMode ?? 'ServiceExecute'
    adtCallbackOperation.value = data.adtCallbackOperation ?? 'OnPIChange'
    adtCallbackAction.value = data.adtCallbackAction ?? ''
  } catch (err: any) {
    showMessage('读取配置失败: ' + (err?.message || '未知错误'), 'error')
  } finally {
    loading.value = false
  }
})

function showMessage(msg: string, type: 'success' | 'error' | 'info') {
  message.value = msg
  messageType.value = type
  setTimeout(() => { message.value = '' }, 6000)
}

async function saveSettings() {
  saving.value = true
  message.value = ''
  try {
    const data: any = {}
    data.adtListenerPort = adtListenerPort.value
    data.adtListenerEnabled = adtListenerEnabled.value
    if (adtTargetHost.value) data.adtTargetHost = adtTargetHost.value
    data.adtTargetPort = String(adtTargetPort.value)
    data.hifBridgeEnabled = hifBridgeEnabled.value
    data.hifBridgeBaseUrl = hifBridgeBaseUrl.value
    data.hifBridgeTimeoutSeconds = hifBridgeTimeoutSeconds.value
    data.adtWcfSendMode = adtWcfSendMode.value
    data.adtCallbackOperation = adtCallbackOperation.value
    data.adtCallbackAction = adtCallbackAction.value
    await updateSystemSettings(data)
    showMessage('配置已保存，重启 HL7GatewayService 后生效', 'success')
  } catch (err: any) {
    showMessage('保存失败: ' + (err?.message || '未知错误'), 'error')
  } finally {
    saving.value = false
  }
}

async function doRestart() {
  if (!confirm('确定要重启 HL7GatewayService 吗？这会导致 MLLP 监听和 ADT 推送暂时中断约 10 秒。')) return
  restarting.value = true
  message.value = ''
  try {
    const result = await restartService()
    if (result.restarted) showMessage('HL7GatewayService 重启成功', 'success')
  } catch (err: any) {
    const detail = err?.message || '未知错误'
    const suggestion = detail.includes('admin') || detail.includes('拒绝') || detail.includes('access')
      ? '<br>请以管理员身份运行: <code>sc stop HL7GatewayService && sc start HL7GatewayService</code>'
      : ''
    showMessage(`重启失败: ${detail}${suggestion}`, 'error')
  } finally {
    restarting.value = false
  }
}

async function doExportConfig() {
  configExportLoading.value = true
  try {
    const data = await exportConfig()
    const json = JSON.stringify(data, null, 2)
    const blob = new Blob([json], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `hl7_config_${new Date().toISOString().slice(0, 10)}.json`
    a.click()
    URL.revokeObjectURL(url)
    showMessage('配置已导出', 'success')
  } catch (err: any) {
    showMessage('导出失败: ' + (err?.message || '未知错误'), 'error')
  } finally {
    configExportLoading.value = false
  }
}

function onFileSelected(e: Event) {
  const input = e.target as HTMLInputElement
  if (input.files && input.files[0]) {
    importedFile.value = input.files[0]
  }
}

async function doImportConfig() {
  if (!importedFile.value) {
    showMessage('请选择配置文件', 'error')
    return
  }
  if (!confirm('导入将覆盖现有标识映射和 WSI 订阅配置，确定继续吗？')) return

  configImportLoading.value = true
  try {
    const text = await importedFile.value.text()
    const data = JSON.parse(text)
    await importConfig(data)
    showMessage('配置已导入', 'success')
    importedFile.value = null
  } catch (err: any) {
    showMessage('导入失败: ' + (err?.message || '文件格式错误'), 'error')
  } finally {
    configImportLoading.value = false
  }
}
</script>

<template>
  <div class="settings-page">
    <div class="page-header">
      <h2>系统设置</h2>
    </div>

    <div v-if="loading" class="loading">加载中...</div>

    <div v-else class="settings-form">
      <div v-if="message" :class="['msg', 'msg-' + messageType]" v-html="message"></div>

      <section class="section">
        <h3>Philips HIF / PPIS 桥接</h3>
        <p class="desc">主服务通过 HTTP 调用独立桥接件；PIC iX 订阅的是桥接件的 9912 端口。</p>
        <label class="toggle-row">
          <input type="checkbox" v-model="hifBridgeEnabled" />
          <span>启用桥接件发送 ADT</span>
        </label>
        <div class="field-row">
          <div class="field field-wide">
            <label>桥接件 HTTP 地址</label>
            <input v-model="hifBridgeBaseUrl" placeholder="http://localhost:5080/" />
          </div>
          <div class="field">
            <label>超时秒数</label>
            <input type="number" v-model.number="hifBridgeTimeoutSeconds" min="1" max="60" />
          </div>
        </div>
      </section>

      <section class="section">
        <h3>旧版 PIC iX 监听</h3>
        <p class="desc">备用诊断功能。启用桥接件时通常应关闭，否则可能抢占 9912 端口。</p>
        <label class="toggle-row">
          <input type="checkbox" v-model="adtListenerEnabled" />
          <span>启用旧版手写 WCF 监听</span>
        </label>
        <div class="field">
          <label>端口号</label>
          <input type="number" v-model.number="adtListenerPort" min="1024" max="65535" />
        </div>
      </section>

      <section class="section">
        <h3>传统 MLLP 发送目标</h3>
        <p class="desc">备用路径。桥接模式下可以留空。</p>
        <div class="field-row">
          <div class="field">
            <label>主机</label>
            <input v-model="adtTargetHost" placeholder="127.0.0.1" />
          </div>
          <div class="field">
            <label>端口</label>
            <input type="number" v-model.number="adtTargetPort" min="1" max="65535" />
          </div>
        </div>
      </section>

      <section class="section">
        <h3>旧版 WCF 回调参数</h3>
        <p class="desc">仅用于旧手写 WCF 诊断路径；桥接件路径不使用这些字段。</p>
        <div class="field field-wide">
          <label>发送模式</label>
          <select v-model="adtWcfSendMode">
            <option value="ServiceExecute">ServiceExecute - 调用 IPIDuplexService/Execute</option>
            <option value="CallbackOnPIChange">CallbackOnPIChange - 回调 IPIClientCallback/OnPIChange</option>
          </select>
        </div>
        <div class="field-row">
          <div class="field field-wide">
            <label>操作名</label>
            <input v-model="adtCallbackOperation" placeholder="OnPIChange" />
          </div>
          <div class="field field-full">
            <label>完整 Action</label>
            <input v-model="adtCallbackAction" placeholder="留空则按订阅前缀 + 操作名自动生成" />
          </div>
        </div>
      </section>

      <section class="section">
        <h3>配置备份与恢复</h3>
        <p class="desc">导出或导入标识映射和 WSI 订阅配置</p>
        <div class="config-actions">
          <button class="btn btn-secondary" :disabled="configExportLoading" @click="doExportConfig">
            {{ configExportLoading ? '导出中...' : '导出配置' }}
          </button>
        </div>
        <div class="import-row">
          <input type="file" accept=".json" @change="onFileSelected" class="file-input" />
          <button class="btn btn-warning" :disabled="configImportLoading || !importedFile" @click="doImportConfig">
            {{ configImportLoading ? '导入中...' : '导入配置' }}
          </button>
        </div>
      </section>

      <div class="actions">
        <button class="btn btn-primary" :disabled="saving" @click="saveSettings">
          {{ saving ? '保存中...' : '保存配置' }}
        </button>
        <button class="btn btn-danger" :disabled="restarting" @click="doRestart">
          {{ restarting ? '重启中...' : '重启服务 (HL7GatewayService)' }}
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.settings-page { max-width: 700px; }
.page-header { margin-bottom: 24px; }
.page-header h2 { font-size: 22px; }
.loading { text-align: center; padding: 60px; color: var(--text-muted); }
.settings-form { display: flex; flex-direction: column; gap: 20px; }
.section {
  background: var(--card-bg); border-radius: var(--radius); box-shadow: var(--shadow); padding: 20px;
}
.section h3 { margin: 0 0 6px; font-size: 16px; }
.desc { font-size: 13px; color: var(--text-secondary); margin: 0 0 16px; }
.field { display: flex; flex-direction: column; gap: 4px; }
.field label { font-size: 12px; color: var(--text-secondary); font-weight: 500; }
.field input,
.field select {
  padding: 8px 12px; border: 1px solid var(--border-color); border-radius: var(--radius-sm);
  font-size: 14px; outline: none; transition: border-color 0.2s; max-width: 250px;
  background: white;
}
.field input:focus,
.field select:focus { border-color: var(--accent); }
.field-row { display: flex; gap: 16px; }
.toggle-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 14px;
  color: var(--text-primary);
  font-size: 14px;
}
.toggle-row input {
  width: 16px;
  height: 16px;
}
.field-wide input,
.field-wide select { max-width: 420px; }
.field-full { flex: 1; }
.field-full input { max-width: none; width: 100%; box-sizing: border-box; }
.config-actions { display: flex; gap: 8px; margin-bottom: 12px; }
.import-row { display: flex; gap: 8px; align-items: center; }
.file-input { font-size: 13px; }
.actions { display: flex; gap: 12px; flex-wrap: wrap; }
.btn {
  padding: 8px 20px; border: none; border-radius: var(--radius-sm);
  font-size: 14px; font-weight: 500; cursor: pointer; transition: background 0.2s;
}
.btn:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-primary { background: var(--accent); color: white; }
.btn-primary:hover:not(:disabled) { background: var(--accent-hover); }
.btn-secondary { background: var(--gray-bg); color: var(--text-primary); }
.btn-secondary:hover:not(:disabled) { background: #e5e7eb; }
.btn-warning { background: var(--warning-bg); color: #92400e; }
.btn-warning:hover:not(:disabled) { background: #fde68a; }
.btn-danger { background: var(--error-bg); color: #991b1b; }
.btn-danger:hover:not(:disabled) { background: #fecaca; }
.msg {
  padding: 12px 16px; border-radius: var(--radius-sm); font-size: 13px;
}
.msg-success { background: var(--success-bg); color: #065f46; }
.msg-error { background: var(--error-bg); color: #991b1b; }
.msg-info { background: #eff6ff; color: #1e40af; }
.msg code { background: rgba(0,0,0,0.06); padding: 2px 6px; border-radius: 3px; font-size: 12px; }
</style>
