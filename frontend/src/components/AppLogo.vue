<script setup lang="ts">
withDefaults(defineProps<{
  size?: number
  showText?: boolean
  variant?: 'light' | 'dark'
  title?: string
  subtitle?: string
}>(), {
  size: 32,
  showText: true,
  variant: 'light',
  title: 'HL7 集成网关',
  subtitle: 'Integration Gateway',
})
</script>

<template>
  <div class="app-logo" :class="variant">
    <svg class="mark" :width="size" :height="size" viewBox="0 0 48 48" aria-hidden="true">
      <defs>
        <linearGradient :id="`lg-${size}`" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#818cf8" />
          <stop offset="1" stop-color="#4338ca" />
        </linearGradient>
      </defs>
      <rect x="2" y="2" width="44" height="44" rx="12" :fill="`url(#lg-${size})`" />
      <path
        d="M7 24 H15 L18.5 15 L24 33 L28 21.5 L30.5 24 H41"
        fill="none"
        stroke="#ffffff"
        stroke-width="2.6"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
      <circle cx="7" cy="24" r="2.6" fill="#ffffff" />
      <circle cx="41" cy="24" r="2.6" fill="#a5b4fc" />
    </svg>
    <div v-if="showText" class="text">
      <span class="title">{{ title }}</span>
      <span v-if="subtitle" class="subtitle">{{ subtitle }}</span>
    </div>
  </div>
</template>

<style scoped>
.app-logo {
  display: flex;
  align-items: center;
  gap: 12px;
}
.mark {
  flex-shrink: 0;
  border-radius: 12px;
  box-shadow: 0 4px 12px rgba(67, 56, 202, 0.35);
}
.text {
  display: flex;
  flex-direction: column;
  line-height: 1.2;
}
.title {
  font-size: 16px;
  font-weight: 700;
  letter-spacing: 0.3px;
}
.subtitle {
  font-size: 10px;
  font-weight: 500;
  letter-spacing: 1.5px;
  text-transform: uppercase;
  opacity: 0.6;
}
.app-logo.light .title { color: #ffffff; }
.app-logo.light .subtitle { color: #c4c4d4; }
.app-logo.dark .title { color: var(--text-primary); }
.app-logo.dark .subtitle { color: var(--text-secondary); }
</style>
