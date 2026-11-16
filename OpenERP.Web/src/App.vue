<script setup lang="ts">
import { ElConfigProvider } from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import { computed, onMounted, onUnmounted, watch } from 'vue'
import { START_LOCATION, useRoute, useRouter } from 'vue-router'
import { account, checkSession, expireSession, sessionState } from './account/session'
import AppShell from './components/layout/AppShell.vue'
import AuthLayout from './components/layout/AuthLayout.vue'
import { isAuthRoute, loginRoute, passwordChangeRoute, returnDestination } from './router/authRoutes'

const router = useRouter()
const route = useRoute()
const routeReady = computed(() => router.currentRoute.value !== START_LOCATION)

function recheckSession(): void {
  if (account.value && !expireSession()) void checkSession()
}

watch(() => account.value?.expiresAt, (expiresAt, _, cleanup) => {
  if (!expiresAt) return
  const deadline = Date.parse(expiresAt)
  let timer: number | undefined
  function schedule(): void {
    if (expireSession()) return
    const remaining = deadline - Date.now()
    timer = window.setTimeout(schedule, Math.min(remaining, 2_147_483_647))
  }
  schedule()
  cleanup(() => window.clearTimeout(timer))
})

watch([sessionState, () => account.value?.mustChangePassword], ([state, mustChangePassword]) => {
  if (!routeReady.value || route.name === 'login') return

  if (state === 'anonymous') {
    const destination = isAuthRoute(route.name) ? returnDestination(router, route.query.returnTo) : route.fullPath
    void router.replace(loginRoute(destination))
  } else if (mustChangePassword && !isAuthRoute(route.name)) {
    void router.replace(passwordChangeRoute(route.fullPath))
  } else if (state === 'authenticated' && !mustChangePassword && route.name === 'required-password-change') {
    void router.replace(returnDestination(router, route.query.returnTo))
  }
})

onMounted(() => window.addEventListener('focus', recheckSession))
onUnmounted(() => window.removeEventListener('focus', recheckSession))
</script>

<template>
  <ElConfigProvider :locale="zhCn">
    <RouterView v-if="route.name === 'login' || (route.name === 'required-password-change' && account?.mustChangePassword)" />
    <AppShell v-else-if="account && !account.mustChangePassword && routeReady" :account="account" />
    <AuthLayout v-else>
      <template v-if="sessionState === 'unavailable'">
        <p role="alert">无法验证登录状态，请稍后重试。</p>
        <button class="btn primary" type="button" @click="checkSession">重试</button>
      </template>
      <p v-else role="status">正在验证登录状态…</p>
    </AuthLayout>
  </ElConfigProvider>
</template>
