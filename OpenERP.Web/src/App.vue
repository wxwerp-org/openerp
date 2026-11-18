<script setup lang="ts">
import { computed, onMounted, onUnmounted, watch } from 'vue'
import { START_LOCATION, useRoute, useRouter } from 'vue-router'
import { account, checkSession, sessionState } from './account/session'
import AppShell from './components/layout/AppShell.vue'
import AuthLayout from './components/layout/AuthLayout.vue'
import { sessionRedirect } from './router/authRoutes'

const router = useRouter()
const route = useRoute()
const routeReady = computed(() => router.currentRoute.value !== START_LOCATION)

function recheckSession(): void {
  void checkSession()
}

function recheckVisibleSession(): void {
  if (document.visibilityState === 'visible') recheckSession()
}

watch(sessionState, (state) => {
  if (!routeReady.value) return
  const redirect = sessionRedirect(router, route, state)
  if (redirect) void router.replace(redirect)
})

onMounted(() => {
  window.addEventListener('focus', recheckSession)
  window.addEventListener('online', recheckSession)
  document.addEventListener('visibilitychange', recheckVisibleSession)
})
onUnmounted(() => {
  window.removeEventListener('focus', recheckSession)
  window.removeEventListener('online', recheckSession)
  document.removeEventListener('visibilitychange', recheckVisibleSession)
})
</script>

<template>
  <RouterView
    v-if="
      (route.name === 'login' && sessionState === 'anonymous') ||
      (route.name === 'initialize' && sessionState === 'uninitialized')
    "
  />
  <AppShell
    v-else-if="account && routeReady && route.name !== 'login' && route.name !== 'initialize'"
    :account="account"
  />
  <AuthLayout v-else>
    <template v-if="sessionState === 'unavailable'">
      <p role="alert">无法验证登录状态，请稍后重试。</p>
      <button class="btn primary" type="button" @click="checkSession">重试</button>
    </template>
    <p v-else role="status">正在验证登录状态…</p>
  </AuthLayout>
</template>
