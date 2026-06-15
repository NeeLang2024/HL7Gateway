import { getToken, logout } from '../utils/auth'

const API_BASE = '/api'

function buildQuery(params?: Record<string, string | number | boolean | undefined | null>): string {
  if (!params) return ''
  const parts: string[] = []
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== '' && v !== null) {
      parts.push(`${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
    }
  }
  return parts.length ? '?' + parts.join('&') : ''
}

function getHeaders(): Record<string, string> {
  const h: Record<string, string> = { 'Content-Type': 'application/json' }
  const token = getToken()
  if (token) h['Authorization'] = `Bearer ${token}`
  return h
}

async function request<T>(url: string, options?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    headers: getHeaders(),
    ...options,
  })
  if (res.status === 401) {
    logout()
    window.location.hash = '#/login'
    throw new Error('401 Unauthorized')
  }
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`HTTP ${res.status}: ${text}`)
  }
  const ct = res.headers.get('content-type') || ''
  if (ct.includes('json')) return res.json()
  return res.text() as unknown as T
}

// Auth
export function login(username: string, password: string): Promise<any> {
  return request(`${API_BASE}/auth/login`, {
    method: 'POST',
    body: JSON.stringify({ username, password }),
  })
}
export function fetchMe(): Promise<any> {
  return request(`${API_BASE}/auth/me`)
}

// Dashboard
export function fetchDashboard(): Promise<any> {
  return request(`${API_BASE}/dashboard`)
}

// Messages
export function fetchMessages(params?: Record<string, string | number | undefined>): Promise<any> {
  return request(`${API_BASE}/messages${buildQuery(params)}`)
}
export function fetchMessage(id: string | number): Promise<any> {
  return request(`${API_BASE}/messages/${id}`)
}
export function fetchMessageRaw(id: string | number): Promise<any> {
  return request(`${API_BASE}/messages/${id}/raw`)
}
export function deleteMessage(id: string | number): Promise<any> {
  return request(`${API_BASE}/messages/${id}`, { method: 'DELETE' })
}
export function reparseMessage(id: string | number): Promise<any> {
  return request(`${API_BASE}/messages/${id}/reparse`, { method: 'POST' })
}

// VitalSigns
export function fetchVitalSigns(params?: Record<string, string | number | undefined>): Promise<any> {
  return request(`${API_BASE}/vitalsigns${buildQuery(params)}`)
}
export function fetchVitalSignTrends(patientId: string, type: string, from?: string, to?: string): Promise<any> {
  return request(`${API_BASE}/vitalsigns/trends${buildQuery({ patientId, type, from, to })}`)
}

// Devices
export function fetchDevices(): Promise<any> {
  return request(`${API_BASE}/devices`)
}
export function fetchDeviceStats(sourceIp?: string, hours?: number): Promise<any> {
  return request(`${API_BASE}/devices/stats${buildQuery({ sourceIp, hours })}`)
}

// Patients
export function fetchPatients(params?: Record<string, string | number | undefined>): Promise<any> {
  return request(`${API_BASE}/patients${buildQuery(params)}`)
}
export function fetchPatient(id: string | number): Promise<any> {
  return request(`${API_BASE}/patients/${id}`)
}
export function fetchPatientVisits(id: string | number): Promise<any> {
  return request(`${API_BASE}/patients/${id}/visits`)
}

// Identifier Mappings
export function fetchIdentifierMappings(params?: Record<string, string | number | undefined>): Promise<any> {
  return request(`${API_BASE}/identifierMappings${buildQuery(params)}`)
}
export function createIdentifierMapping(data: any): Promise<any> {
  return request(`${API_BASE}/identifierMappings`, { method: 'POST', body: JSON.stringify(data) })
}
export function updateIdentifierMapping(id: string | number, data: any): Promise<any> {
  return request(`${API_BASE}/identifierMappings/${id}`, { method: 'PUT', body: JSON.stringify(data) })
}
export function deleteIdentifierMapping(id: string | number): Promise<any> {
  return request(`${API_BASE}/identifierMappings/${id}`, { method: 'DELETE' })
}

// ADT
export function fetchAdtQueue(status?: string): Promise<any> {
  const q = status ? `?status=${encodeURIComponent(status)}` : ''
  return request(`${API_BASE}/adt/queue${q}`)
}
export function fetchAdtLogs(page?: number, pageSize?: number): Promise<any> {
  return request(`${API_BASE}/adt/logs${buildQuery({ page, pageSize })}`)
}
export function fetchAdtBridgeStatus(): Promise<any> {
  return request(`${API_BASE}/adt/bridge-status`)
}
export function fetchAdtBridgeLogs(sinceId?: number, take?: number): Promise<any> {
  return request(`${API_BASE}/adt/bridge-logs${buildQuery({ sinceId, take })}`)
}
export function sendAdtMessage(data: any): Promise<any> {
  return request(`${API_BASE}/adt/send`, { method: 'POST', body: JSON.stringify(data) })
}
export function composeAdtMessage(data: any): Promise<any> {
  return request(`${API_BASE}/adt/compose`, { method: 'POST', body: JSON.stringify(data) })
}
export function deleteAdtQueueItem(id: number): Promise<any> {
  return request(`${API_BASE}/adt/queue/${id}`, { method: 'DELETE' })
}
export function retryAdtQueueItem(id: number): Promise<any> {
  return request(`${API_BASE}/adt/queue/${id}/retry`, { method: 'POST' })
}

// Auto ADT
export function fetchAutoAdtBeds(params?: Record<string, string | number | boolean | undefined>): Promise<any> {
  return request(`${API_BASE}/auto-adt/beds${buildQuery(params as any)}`)
}
export function createAutoAdtBed(data: any): Promise<any> {
  return request(`${API_BASE}/auto-adt/beds`, { method: 'POST', body: JSON.stringify(data) })
}
export function updateAutoAdtBed(id: string | number, data: any): Promise<any> {
  return request(`${API_BASE}/auto-adt/beds/${id}`, { method: 'PUT', body: JSON.stringify(data) })
}
export function deleteAutoAdtBed(id: string | number): Promise<any> {
  return request(`${API_BASE}/auto-adt/beds/${id}`, { method: 'DELETE' })
}
export function upsertAutoAdtPatient(data: any): Promise<any> {
  return request(`${API_BASE}/auto-adt/patients`, { method: 'POST', body: JSON.stringify(data) })
}
export function scanAutoAdtPatient(rawText: string): Promise<any> {
  return request(`${API_BASE}/auto-adt/scan/patient`, { method: 'POST', body: JSON.stringify({ rawText }) })
}
export function scanAutoAdtBed(rawText: string): Promise<any> {
  return request(`${API_BASE}/auto-adt/scan/bed`, { method: 'POST', body: JSON.stringify({ rawText }) })
}
export function admitAutoAdt(data: any): Promise<any> {
  return request(`${API_BASE}/auto-adt/admit`, { method: 'POST', body: JSON.stringify(data) })
}
export function updateAutoAdtPatient(data: any): Promise<any> {
  return request(`${API_BASE}/auto-adt/update`, { method: 'POST', body: JSON.stringify(data) })
}
export function transferAutoAdt(data: any): Promise<any> {
  return request(`${API_BASE}/auto-adt/transfer`, { method: 'POST', body: JSON.stringify(data) })
}
export function dischargeAutoAdt(data: any): Promise<any> {
  return request(`${API_BASE}/auto-adt/discharge`, { method: 'POST', body: JSON.stringify(data) })
}
export function resendAutoAdtMessage(id: string | number): Promise<any> {
  return request(`${API_BASE}/auto-adt/messages/${id}/resend`, { method: 'POST' })
}
export function fetchAutoAdtDashboard(): Promise<any> {
  return request(`${API_BASE}/auto-adt/dashboard`)
}
export function fetchAutoAdtBindings(activeOnly = true): Promise<any> {
  return request(`${API_BASE}/auto-adt/bindings${buildQuery({ activeOnly })}`)
}
export function fetchAutoAdtBoard(params?: { includeDisabled?: boolean; careArea?: string }): Promise<any> {
  return request(`${API_BASE}/auto-adt/board${buildQuery(params as any)}`)
}
export function fetchAutoAdtEvents(page?: number, pageSize?: number): Promise<any> {
  return request(`${API_BASE}/auto-adt/events${buildQuery({ page, pageSize })}`)
}
export function fetchAutoAdtMessages(page?: number, pageSize?: number): Promise<any> {
  return request(`${API_BASE}/auto-adt/messages${buildQuery({ page, pageSize })}`)
}
export function fetchAutoAdtScanRules(type?: string): Promise<any> {
  return request(`${API_BASE}/auto-adt/scan-rules${buildQuery({ type })}`)
}
export function createAutoAdtScanRule(data: any): Promise<any> {
  return request(`${API_BASE}/auto-adt/scan-rules`, { method: 'POST', body: JSON.stringify(data) })
}
export function updateAutoAdtScanRule(id: number, data: any): Promise<any> {
  return request(`${API_BASE}/auto-adt/scan-rules/${id}`, { method: 'PUT', body: JSON.stringify(data) })
}
export function deleteAutoAdtScanRule(id: number): Promise<any> {
  return request(`${API_BASE}/auto-adt/scan-rules/${id}`, { method: 'DELETE' })
}
export function testAutoAdtScanRule(ruleType: string, rawText: string): Promise<any> {
  return request(`${API_BASE}/auto-adt/scan-rules/test`, { method: 'POST', body: JSON.stringify({ ruleType, rawText }) })
}
export function fetchAutoAdtFeatures(): Promise<any> {
  return request(`${API_BASE}/auto-adt/features`)
}
export function updateAutoAdtFeatures(data: any): Promise<any> {
  return request(`${API_BASE}/auto-adt/features`, { method: 'PUT', body: JSON.stringify(data) })
}
export function fetchAutoAdtPreflight(): Promise<any> {
  return request(`${API_BASE}/auto-adt/preflight`)
}
export function importAutoAdtBeds(csv: string, updateExisting = false): Promise<any> {
  return request(`${API_BASE}/auto-adt/beds/import`, { method: 'POST', body: JSON.stringify({ csv, updateExisting }) })
}

// System Logs
export function fetchSystemLogs(params?: Record<string, string | number | undefined>): Promise<any> {
  return request(`${API_BASE}/systemlogs${buildQuery(params)}`)
}
export function clearSystemLogs(before?: string): Promise<any> {
  const q = before ? `?before=${encodeURIComponent(before)}` : ''
  return request(`${API_BASE}/systemlogs${q}`, { method: 'DELETE' })
}

// System Settings
export function fetchSystemSettings(): Promise<any> {
  return request(`${API_BASE}/system/settings`)
}
export function updateSystemSettings(data: any): Promise<any> {
  return request(`${API_BASE}/system/settings`, { method: 'PUT', body: JSON.stringify(data) })
}
export function restartService(): Promise<any> {
  return request(`${API_BASE}/system/restart-service`, { method: 'POST' })
}

// WSI Subscriptions
export function fetchWsiSubscriptions(): Promise<any> {
  return request(`${API_BASE}/wsi/subscriptions`)
}
export function subscribeWsi(data: any): Promise<any> {
  return request(`${API_BASE}/wsi/subscribe`, { method: 'POST', body: JSON.stringify(data) })
}
export function unsubscribeWsi(subscriptionId: number): Promise<any> {
  return request(`${API_BASE}/wsi/unsubscribe`, { method: 'POST', body: JSON.stringify({ subscriptionId }) })
}

// Export
export function exportMessages(from?: string, to?: string): string {
  const q = buildQuery({ from, to })
  return `${API_BASE}/export/messages${q}`
}
export function exportVitals(patientId?: string, from?: string, to?: string): string {
  const q = buildQuery({ patientId, from, to })
  return `${API_BASE}/export/vitals${q}`
}
export function exportAdtLogs(): string {
  return `${API_BASE}/export/adt`
}

// Config backup/restore
export function exportConfig(): Promise<any> {
  return request(`${API_BASE}/config/export`)
}
export function importConfig(data: any): Promise<any> {
  return request(`${API_BASE}/config/import`, { method: 'POST', body: JSON.stringify(data) })
}

// Validation
export function validateMessage(messageId: string | number): Promise<any> {
  return request(`${API_BASE}/validation/${messageId}`)
}

// Search
export function searchMessages(q: string, page?: number, pageSize?: number): Promise<any> {
  return request(`${API_BASE}/messages/search${buildQuery({ q, page, pageSize })}`)
}

// Compare
export function compareMessages(id1: string | number, id2: string | number): Promise<any> {
  return request(`${API_BASE}/messages/compare${buildQuery({ id1, id2 })}`)
}

// Users
export function fetchUsers(): Promise<any> {
  return request(`${API_BASE}/users`)
}
export function createUser(data: any): Promise<any> {
  return request(`${API_BASE}/users`, { method: 'POST', body: JSON.stringify(data) })
}
export function updateUser(id: number, data: any): Promise<any> {
  return request(`${API_BASE}/users/${id}`, { method: 'PUT', body: JSON.stringify(data) })
}
export function deleteUser(id: number): Promise<any> {
  return request(`${API_BASE}/users/${id}`, { method: 'DELETE' })
}
export function changePassword(data: any): Promise<any> {
  return request(`${API_BASE}/users/change-password`, { method: 'POST', body: JSON.stringify(data) })
}

// Monitor
export function fetchMonitor(): Promise<any> {
  return request(`${API_BASE}/monitor`)
}

// Integration Hub
export function fetchIntegrationPartners(): Promise<any> {
  return request(`${API_BASE}/integration/partners`)
}
export function fetchIntegrationTraces(params?: { traceId?: string; limit?: number; recent?: number }): Promise<any> {
  return request(`${API_BASE}/integration/traces${buildQuery(params as any)}`)
}
export function injectIntegrationHl7(hl7: string, mode: 'adt-queue' | 'bridge-direct' = 'adt-queue'): Promise<any> {
  return request(`${API_BASE}/integration/simulate/inject`, {
    method: 'POST',
    body: JSON.stringify({ hl7, mode }),
  })
}
export function replayIntegrationMessage(messageId: number): Promise<any> {
  return request(`${API_BASE}/integration/simulate/replay/${messageId}`, { method: 'POST' })
}

export function fetchRoutingSettings(): Promise<any> {
  return request(`${API_BASE}/integration/routing/settings`)
}
export function saveRoutingSettings(settings: any): Promise<any> {
  return request(`${API_BASE}/integration/routing/settings`, {
    method: 'PUT',
    body: JSON.stringify(settings),
  })
}
export function fetchRoutingRules(): Promise<any> {
  return request(`${API_BASE}/integration/routing/rules`)
}
export function createRoutingRule(rule: any): Promise<any> {
  return request(`${API_BASE}/integration/routing/rules`, {
    method: 'POST',
    body: JSON.stringify(rule),
  })
}
export function updateRoutingRule(id: number, rule: any): Promise<any> {
  return request(`${API_BASE}/integration/routing/rules/${id}`, {
    method: 'PUT',
    body: JSON.stringify(rule),
  })
}
export function deleteRoutingRule(id: number): Promise<any> {
  return request(`${API_BASE}/integration/routing/rules/${id}`, { method: 'DELETE' })
}
export function testRoutingMatch(body: any): Promise<any> {
  return request(`${API_BASE}/integration/routing/test`, {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

// FHIR
export function fhirPatientSearch(params: Record<string, string>): Promise<any> {
  return request(`${API_BASE}/fhir/Patient${buildQuery(params)}`)
}
export function fhirPatientRead(id: string): Promise<any> {
  return request(`${API_BASE}/fhir/Patient/${id}`)
}
export function fhirObservationSearch(params: Record<string, string>): Promise<any> {
  return request(`${API_BASE}/fhir/Observation${buildQuery(params)}`)
}
export function fhirObservationRead(id: string): Promise<any> {
  return request(`${API_BASE}/fhir/Observation/${id}`)
}
