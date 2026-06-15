import { createRouter, createWebHashHistory } from 'vue-router'
import { isLoggedIn } from '../utils/auth'
import Dashboard from '../views/Dashboard.vue'
import Messages from '../views/Messages.vue'
import MessageDetail from '../views/MessageDetail.vue'
import VitalSigns from '../views/VitalSigns.vue'
import Devices from '../views/Devices.vue'
import ADTQueue from '../views/ADTQueue.vue'
import IdentifierMappings from '../views/IdentifierMappings.vue'
import Patients from '../views/Patients.vue'
import SystemLogs from '../views/SystemLogs.vue'
import WSISubscriptions from '../views/WSISubscriptions.vue'
import SystemSettings from '../views/SystemSettings.vue'
import Login from '../views/Login.vue'
import Trends from '../views/Trends.vue'
import MessageSearch from '../views/MessageSearch.vue'
import MessageCompare from '../views/MessageCompare.vue'
import UsersManagement from '../views/UsersManagement.vue'
import Monitor from '../views/Monitor.vue'
import FhirBrowser from '../views/FhirBrowser.vue'
import AutoAdtScan from '../views/AutoAdtScan.vue'
import AutoAdtBoard from '../views/AutoAdtBoard.vue'
import AutoAdtBeds from '../views/AutoAdtBeds.vue'
import AutoAdtLogs from '../views/AutoAdtLogs.vue'
import AutoAdtScanRules from '../views/AutoAdtScanRules.vue'
import AutoAdtSettings from '../views/AutoAdtSettings.vue'
import IntegrationHub from '../views/IntegrationHub.vue'

const routes = [
  { path: '/login', name: 'Login', component: Login, meta: { noAuth: true } },
  { path: '/', name: 'Dashboard', component: Dashboard },
  { path: '/messages', name: 'Messages', component: Messages },
  { path: '/messages/:id', name: 'MessageDetail', component: MessageDetail },
  { path: '/vitalsigns', name: 'VitalSigns', component: VitalSigns },
  { path: '/devices', name: 'Devices', component: Devices },
  { path: '/search', name: 'MessageSearch', component: MessageSearch },
  { path: '/compare', name: 'MessageCompare', component: MessageCompare },
  { path: '/adt', name: 'ADTQueue', component: ADTQueue },
  { path: '/integration', name: 'IntegrationHub', component: IntegrationHub },
  { path: '/auto-adt', redirect: '/auto-adt/scan' },
  { path: '/auto-adt/scan', name: 'AutoAdtScan', component: AutoAdtScan },
  { path: '/auto-adt/board', name: 'AutoAdtBoard', component: AutoAdtBoard },
  { path: '/auto-adt/beds', name: 'AutoAdtBeds', component: AutoAdtBeds },
  { path: '/auto-adt/scan-rules', name: 'AutoAdtScanRules', component: AutoAdtScanRules },
  { path: '/auto-adt/settings', name: 'AutoAdtSettings', component: AutoAdtSettings },
  { path: '/auto-adt/logs', name: 'AutoAdtLogs', component: AutoAdtLogs },
  { path: '/mappings', name: 'IdentifierMappings', component: IdentifierMappings },
  { path: '/patients', name: 'Patients', component: Patients },
  { path: '/fhir', name: 'FhirBrowser', component: FhirBrowser },
  { path: '/users', name: 'UsersManagement', component: UsersManagement },
  { path: '/wsi', name: 'WSISubscriptions', component: WSISubscriptions },
  { path: '/logs', name: 'SystemLogs', component: SystemLogs },
  { path: '/monitor', name: 'Monitor', component: Monitor },
  { path: '/settings', name: 'SystemSettings', component: SystemSettings },
  { path: '/trends', name: 'Trends', component: Trends },
]

const router = createRouter({
  history: createWebHashHistory(),
  routes,
})

router.beforeEach((to, _from, next) => {
  if (to.meta?.noAuth) return next()
  if (!isLoggedIn()) return next('/login')
  next()
})

export default router
