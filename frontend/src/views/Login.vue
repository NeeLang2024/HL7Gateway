<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { login } from '../api'
import { setToken, setUser } from '../utils/auth'
import AppLogo from '../components/AppLogo.vue'

const router = useRouter()

const username = ref('')
const password = ref('')
const error = ref('')
const loading = ref(false)

async function doLogin() {
  if (!username.value || !password.value) {
    error.value = '请输入用户名和密码'
    return
  }
  loading.value = true
  error.value = ''
  try {
    const res = await login(username.value, password.value)
    setToken(res.token)
    setUser(res.user)
    router.push('/')
  } catch (err: any) {
    error.value = err?.message?.includes('401') ? '用户名或密码错误' : '登录失败: ' + (err?.message || '未知错误')
  } finally {
    loading.value = false
  }
}

const features = [
  { icon: '💓', text: 'HL7 实时接收与生命体征解析' },
  { icon: '🔄', text: 'ADT 转发 · 扫码自动入出转床' },
  { icon: '🏥', text: '飞利浦 PIC iX 监护集成' },
]
</script>

<template>
  <div class="login-page">
    <!-- 左侧品牌区 -->
    <section class="brand-panel">
      <div class="brand-glow glow-1"></div>
      <div class="brand-glow glow-2"></div>

      <div class="brand-top">
        <AppLogo :size="44" variant="light" subtitle="INTEGRATION GATEWAY" />
      </div>

      <div class="brand-body">
        <h1 class="brand-headline">让医疗数据<br />安全、顺畅地流动</h1>
        <p class="brand-sub">HL7 / FHIR 集成网关 · 一体化监护数据中枢</p>

        <ul class="feature-list">
          <li v-for="f in features" :key="f.text">
            <span class="f-icon">{{ f.icon }}</span>
            <span>{{ f.text }}</span>
          </li>
        </ul>
      </div>

      <svg class="ecg" viewBox="0 0 600 80" preserveAspectRatio="none" aria-hidden="true">
        <path d="M0 40 H120 L150 14 L185 66 L215 30 L240 40 H360 L390 14 L425 66 L455 30 L480 40 H600"
              fill="none" stroke="rgba(255,255,255,0.35)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
      </svg>
    </section>

    <!-- 右侧登录表单 -->
    <section class="form-panel">
      <div class="login-card">
        <div class="mobile-logo">
          <AppLogo :size="40" variant="dark" />
        </div>

        <h2 class="login-title">欢迎回来</h2>
        <p class="login-desc">请登录以继续使用集成网关</p>

        <div v-if="error" class="login-error">{{ error }}</div>

        <form @submit.prevent="doLogin" class="login-form">
          <div class="field">
            <label>用户名</label>
            <div class="input-wrap">
              <span class="input-icon">👤</span>
              <input v-model="username" type="text" placeholder="admin" autocomplete="username" />
            </div>
          </div>
          <div class="field">
            <label>密码</label>
            <div class="input-wrap">
              <span class="input-icon">🔒</span>
              <input v-model="password" type="password" placeholder="••••••••" autocomplete="current-password" />
            </div>
          </div>
          <button type="submit" class="btn btn-primary" :disabled="loading">
            <span v-if="loading" class="spinner"></span>
            {{ loading ? '登录中...' : '登 录' }}
          </button>
        </form>

        <p class="login-foot">HL7 集成网关 · © {{ new Date().getFullYear() }}</p>
      </div>
    </section>
  </div>
</template>

<style scoped>
.login-page {
  display: flex;
  min-height: 100vh;
  margin: -24px -32px;
}

/* ---------- 左侧品牌区 ---------- */
.brand-panel {
  position: relative;
  flex: 1.1;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  padding: 48px 56px;
  overflow: hidden;
  color: #fff;
  background: linear-gradient(150deg, #312e81 0%, #4338ca 45%, #6366f1 100%);
}
.brand-glow {
  position: absolute;
  border-radius: 50%;
  filter: blur(80px);
  opacity: 0.5;
  pointer-events: none;
}
.glow-1 { width: 360px; height: 360px; background: #818cf8; top: -120px; right: -80px; }
.glow-2 { width: 300px; height: 300px; background: #4f46e5; bottom: -100px; left: -60px; }
.brand-top { position: relative; z-index: 1; }
.brand-body { position: relative; z-index: 1; }
.brand-headline {
  font-size: 38px;
  font-weight: 800;
  line-height: 1.25;
  margin: 0 0 16px;
  letter-spacing: 0.5px;
}
.brand-sub {
  font-size: 15px;
  color: rgba(255, 255, 255, 0.8);
  margin-bottom: 36px;
}
.feature-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.feature-list li {
  display: flex;
  align-items: center;
  gap: 14px;
  font-size: 15px;
  color: rgba(255, 255, 255, 0.92);
}
.f-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.12);
  font-size: 18px;
  backdrop-filter: blur(4px);
}
.ecg {
  position: relative;
  z-index: 1;
  width: 100%;
  height: 64px;
  margin-top: 28px;
}

/* ---------- 右侧表单区 ---------- */
.form-panel {
  flex: 0.9;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px;
  background: #f7f8fc;
}
.login-card {
  width: 100%;
  max-width: 380px;
  background: var(--card-bg);
  border-radius: 16px;
  box-shadow: 0 12px 40px rgba(49, 46, 129, 0.12);
  padding: 44px 40px;
}
.mobile-logo { display: none; justify-content: center; margin-bottom: 24px; }
.login-title {
  font-size: 26px;
  font-weight: 700;
  margin: 0 0 6px;
  color: var(--text-primary);
}
.login-desc {
  font-size: 14px;
  color: var(--text-secondary);
  margin: 0 0 28px;
}
.login-error {
  background: var(--error-bg);
  color: #991b1b;
  padding: 10px 14px;
  border-radius: var(--radius-sm);
  font-size: 13px;
  margin-bottom: 18px;
}
.login-form {
  display: flex;
  flex-direction: column;
  gap: 18px;
}
.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.field label {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}
.input-wrap {
  position: relative;
  display: flex;
  align-items: center;
}
.input-icon {
  position: absolute;
  left: 12px;
  font-size: 14px;
  opacity: 0.6;
  pointer-events: none;
}
.input-wrap input {
  width: 100%;
  padding: 12px 14px 12px 38px;
  border: 1px solid var(--border-color);
  border-radius: 10px;
  font-size: 14px;
  outline: none;
  transition: border-color 0.2s, box-shadow 0.2s;
  background: #fcfcfe;
}
.input-wrap input:focus {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.15);
}
.btn {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 12px 20px;
  border: none;
  border-radius: 10px;
  font-size: 15px;
  font-weight: 600;
  cursor: pointer;
  transition: transform 0.1s, box-shadow 0.2s, opacity 0.2s;
  margin-top: 8px;
}
.btn:disabled { opacity: 0.6; cursor: not-allowed; }
.btn-primary {
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: #fff;
  box-shadow: 0 6px 16px rgba(79, 70, 229, 0.35);
}
.btn-primary:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 8px 22px rgba(79, 70, 229, 0.45); }
.btn-primary:active:not(:disabled) { transform: translateY(0); }
.spinner {
  width: 15px;
  height: 15px;
  border: 2px solid rgba(255, 255, 255, 0.4);
  border-top-color: #fff;
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
}
@keyframes spin { to { transform: rotate(360deg); } }
.login-foot {
  text-align: center;
  font-size: 12px;
  color: var(--text-muted);
  margin-top: 28px;
}

/* ---------- 响应式 ---------- */
@media (max-width: 860px) {
  .brand-panel { display: none; }
  .form-panel { flex: 1; }
  .mobile-logo { display: flex; }
}
</style>
