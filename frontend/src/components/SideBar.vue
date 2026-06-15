<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getUser, logout as authLogout } from '../utils/auth'
import AppLogo from './AppLogo.vue'

const route = useRoute()
const router = useRouter()
const user = ref(getUser())

const navItems = [
  { path: '/', label: '仪表盘', icon: '📊' },
  { path: '/messages', label: '消息列表', icon: '💬' },
  { path: '/vitalsigns', label: '生命体征', icon: '❤️' },
  { path: '/trends', label: '趋势图', icon: '📈' },
  { path: '/devices', label: '设备连接', icon: '🔌' },
  { path: '/search', label: '全文搜索', icon: '🔍' },
  { path: '/compare', label: '消息对比', icon: '⚖️' },
  { path: '/adt', label: 'ADT 管理', icon: '🔄' },
  { path: '/integration', label: '集成中枢', icon: '🔀' },
  { path: '/auto-adt/scan', label: '扫码入院', icon: '▣' },
  { path: '/auto-adt/board', label: '床位看板', icon: '▦' },
  { path: '/auto-adt/beds', label: '床位映射', icon: '▤' },
  { path: '/auto-adt/scan-rules', label: '扫码规则', icon: '▧' },
  { path: '/auto-adt/settings', label: '功能开关', icon: '⚙' },
  { path: '/auto-adt/logs', label: 'AutoADT 日志', icon: '▥' },
  { path: '/mappings', label: '标识映射', icon: '🔗' },
  { path: '/patients', label: '患者管理', icon: '👤' },
  { path: '/fhir', label: 'FHIR 浏览', icon: '🏥' },
  { path: '/users', label: '用户管理', icon: '👥' },
  { path: '/wsi', label: 'WSI 订阅', icon: '🔔' },
  { path: '/logs', label: '系统日志', icon: '📋' },
  { path: '/monitor', label: '系统监控', icon: '📡' },
  { path: '/settings', label: '系统设置', icon: '⚙️' },
]

function doLogout() {
  authLogout()
  user.value = null
  router.push('/login')
}

onMounted(() => {
  user.value = getUser()
})
</script>

<template>
  <aside class="sidebar">
    <div class="sidebar-header">
      <AppLogo :size="34" variant="light" />
    </div>
    <nav class="sidebar-nav">
      <router-link
        v-for="item in navItems"
        :key="item.path"
        :to="item.path"
        :class="['nav-item', { active: route.path === item.path }]"
      >
        <span class="nav-icon">{{ item.icon }}</span>
        <span class="nav-label">{{ item.label }}</span>
      </router-link>
    </nav>
    <div class="sidebar-footer" v-if="user">
      <div class="user-info">
        <span class="user-avatar">{{ user.displayName?.charAt(0) || 'U' }}</span>
        <span class="user-name">{{ user.displayName || user.username }}</span>
      </div>
      <button class="logout-btn" @click="doLogout" title="退出登录">退出</button>
    </div>
  </aside>
</template>

<style scoped>
.sidebar {
  width: 220px;
  height: 100vh;
  background: #1a1a2e;
  color: #c4c4d4;
  display: flex;
  flex-direction: column;
  position: fixed;
  left: 0;
  top: 0;
  z-index: 100;
}
.sidebar-header {
  padding: 24px 20px 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}
.sidebar-title {
  font-size: 18px;
  font-weight: 600;
  color: #ffffff;
  margin: 0;
  letter-spacing: 0.5px;
}
.sidebar-nav {
  flex: 1;
  padding: 12px 0;
  overflow-y: auto;
}
.nav-item {
  display: flex;
  align-items: center;
  padding: 10px 20px;
  color: #9a9ab0;
  text-decoration: none;
  transition: all 0.2s;
  font-size: 13px;
  border-left: 3px solid transparent;
}
.nav-item:hover {
  background: rgba(255, 255, 255, 0.05);
  color: #e0e0f0;
}
.nav-item.active {
  background: rgba(99, 102, 241, 0.15);
  color: #818cf8;
  border-left-color: #818cf8;
  font-weight: 500;
}
.nav-icon {
  margin-right: 10px;
  font-size: 14px;
  width: 20px;
  text-align: center;
}
.nav-label {
  font-size: 13px;
}
.sidebar-footer {
  padding: 12px 16px;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.user-info {
  display: flex;
  align-items: center;
  gap: 8px;
}
.user-avatar {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background: #6366f1;
  color: #fff;
  font-size: 13px;
  font-weight: 600;
  display: flex;
  align-items: center;
  justify-content: center;
}
.user-name {
  font-size: 13px;
  color: #c4c4d4;
}
.logout-btn {
  padding: 4px 10px;
  border: 1px solid rgba(255,255,255,0.15);
  border-radius: 4px;
  background: transparent;
  color: #9a9ab0;
  font-size: 12px;
  cursor: pointer;
  transition: all 0.2s;
}
.logout-btn:hover {
  background: rgba(239, 68, 68, 0.2);
  border-color: #ef4444;
  color: #fca5a5;
}
</style>
