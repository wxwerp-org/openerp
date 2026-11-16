<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { login } from '../account/session'
import AuthLayout from '../components/layout/AuthLayout.vue'
import { returnDestination } from '../router/authRoutes'

const router = useRouter()
const route = useRoute()
const username = ref('')
const password = ref('')
const error = ref('')
const submitting = ref(false)

async function submit(): Promise<void> {
  if (submitting.value || !username.value.trim() || !password.value) return
  submitting.value = true
  error.value = ''
  try {
    await login(username.value.trim(), password.value)
    await router.replace(returnDestination(router, route.query.returnTo))
  } catch (cause) {
    error.value = cause instanceof Error && !(cause instanceof TypeError)
      ? cause.message
      : '无法连接登录服务，请稍后重试。'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AuthLayout>
    <h1>登录 OpenERP</h1>
    <form class="login-form" @submit.prevent="submit">
      <label for="loginUsername">用户名</label>
      <input id="loginUsername" v-model="username" autocomplete="username" required :disabled="submitting">

      <label for="loginPassword">密码</label>
      <input id="loginPassword" v-model="password" type="password" autocomplete="current-password" required :disabled="submitting">

      <p v-if="error" class="login-error" role="alert">{{ error }}</p>
      <button class="btn primary" type="submit" :disabled="submitting">{{ submitting ? '正在登录…' : '登录' }}</button>
    </form>
  </AuthLayout>
</template>

<style scoped>
.login-form { display: grid; gap: 8px; margin-top: 24px; }
.login-form label { font-weight: 600; }
.login-form input {
  width: 100%;
  min-height: 42px;
  margin-bottom: 10px;
  padding: 8px 12px;
  border: 1px solid var(--line);
  border-radius: 6px;
  font: inherit;
}
.login-form button { margin-top: 8px; min-height: 42px; }
.login-error { margin: 0; color: #b42318; }
</style>
