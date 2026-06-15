<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import SideBar from './components/SideBar.vue'

const route = useRoute()
const isLoginPage = computed(() => route.path === '/login')
</script>

<template>
  <div class="app-layout">
    <SideBar v-if="!isLoginPage" />
    <main :class="['main-content', { 'no-sidebar': isLoginPage }]">
      <router-view />
    </main>
  </div>
</template>

<style scoped>
.app-layout {
  display: flex;
  min-height: 100vh;
}
.main-content {
  margin-left: 220px;
  flex: 1;
  background: #f5f5f5;
  min-height: 100vh;
  padding: 24px 32px;
  box-sizing: border-box;
}
.main-content.no-sidebar {
  margin-left: 0;
  padding: 0;
}
</style>
