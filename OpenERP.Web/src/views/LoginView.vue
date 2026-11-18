<script setup lang="ts">
import { ref } from 'vue'
import { useRoute } from 'vue-router'
import { initializationCompleted, login } from '../account/session'
import AuthLayout from '../components/layout/AuthLayout.vue'

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
  } catch (cause) {
    error.value =
      cause instanceof Error && !(cause instanceof TypeError)
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
    <p v-if="initializationCompleted" role="status">管理员密码初始化成功，请登录。</p>
    <p v-else-if="route.query.initialized === '1'" role="status">管理员账号已初始化，请登录。</p>
    <form class="login-form" @submit.prevent="submit">
      <div class="login-field">
        <label for="loginUsername">用户名</label>
        <input
          id="loginUsername"
          v-model="username"
          autocomplete="username"
          required
          :disabled="submitting"
        />
      </div>

      <div class="login-field">
        <label for="loginPassword">密码</label>
        <input
          id="loginPassword"
          v-model="password"
          type="password"
          autocomplete="current-password"
          required
          :disabled="submitting"
        />
      </div>

      <p v-if="error" class="login-error" role="alert">{{ error }}</p>
      <button class="btn primary" type="submit" :disabled="submitting">
        {{ submitting ? '正在登录…' : '登录' }}
      </button>
    </form>
  </AuthLayout>
</template>

<style scoped>
.login-form {
  display: grid;
  gap: 20px;
  margin-top: 28px;
}
.login-field {
  display: grid;
  min-width: 0;
  gap: 8px;
}
.login-form label {
  color: var(--text);
  font-size: 13px;
  font-weight: 600;
  line-height: 20px;
}
.login-form input {
  width: 100%;
  min-height: 42px;
  padding: 10px 12px;
  border: 1px solid var(--input-border);
  border-radius: var(--radius);
  outline: 0;
  background: var(--surface);
  color: var(--text);
  font: inherit;
  font-size: 14px;
  line-height: 20px;
  transition:
    border-color 0.15s,
    box-shadow 0.15s;
}
.login-form input:hover:not(:disabled) {
  border-color: #aeb9c9;
}
.login-form input:focus-visible {
  border-color: var(--primary);
  box-shadow: 0 0 0 3px var(--primary-soft);
}
.login-form input:disabled {
  background: var(--page-soft);
  color: var(--muted);
  cursor: not-allowed;
}
.login-form button {
  width: 100%;
  min-height: 42px;
  margin-top: 4px;
  font-size: 14px;
  font-weight: 600;
  line-height: 24px;
}
.login-form button:focus-visible {
  outline: 2px solid var(--primary);
  outline-offset: 3px;
}
.login-form button:disabled {
  cursor: not-allowed;
  opacity: 0.65;
}
.login-error {
  margin: 0;
  padding: 10px 12px;
  border: 1px solid #ffd4d4;
  border-radius: var(--radius);
  background: #fff2f2;
  color: var(--danger);
  font-size: 13px;
  line-height: 20px;
}
</style>
