<script setup lang="ts">
import { ref } from 'vue'
import { initialize, initializationPending } from '../account/session'
import AuthLayout from '../components/layout/AuthLayout.vue'

const newPassword = ref('')
const confirmPassword = ref('')
const newPasswordInput = ref<HTMLInputElement | null>(null)
const confirmPasswordInput = ref<HTMLInputElement | null>(null)
const invalidPassword = ref(false)
const invalidConfirmation = ref(false)
const error = ref('')
async function submit(): Promise<void> {
  if (initializationPending.value) return
  error.value = ''
  invalidPassword.value = newPassword.value.length < 6 || newPassword.value.length > 20
  invalidConfirmation.value = !confirmPassword.value || newPassword.value !== confirmPassword.value
  if (invalidPassword.value) {
    newPasswordInput.value?.focus()
    return
  }
  if (invalidConfirmation.value) {
    confirmPasswordInput.value?.focus()
    return
  }

  try {
    await initialize(newPassword.value)
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : '初始化失败，请稍后重试。'
  }
}
</script>

<template>
  <AuthLayout>
    <h1>初始化 OpenERP</h1>
    <form class="initialize-form" novalidate @submit.prevent="submit">
      <div class="initialize-field">
        <label for="initializeUsername">用户名</label>
        <input id="initializeUsername" value="admin" autocomplete="username" readonly />
      </div>

      <div class="initialize-field">
        <label for="newPassword">新密码</label>
        <input
          id="newPassword"
          ref="newPasswordInput"
          v-model="newPassword"
          type="password"
          autocomplete="new-password"
          placeholder="6-20 位字符"
          required
          :disabled="initializationPending"
          :aria-invalid="invalidPassword"
          :aria-describedby="invalidPassword ? 'initializePasswordError' : undefined"
          @input="invalidPassword = false"
        />
        <p
          v-if="invalidPassword"
          id="initializePasswordError"
          class="initialize-error"
          role="alert"
        >
          密码长度需为 6-20 位
        </p>
      </div>

      <div class="initialize-field">
        <label for="confirmPassword">确认新密码</label>
        <input
          id="confirmPassword"
          ref="confirmPasswordInput"
          v-model="confirmPassword"
          type="password"
          autocomplete="new-password"
          required
          :disabled="initializationPending"
          :aria-invalid="invalidConfirmation"
          :aria-describedby="invalidConfirmation ? 'initializeConfirmationError' : undefined"
          @input="invalidConfirmation = false"
        />
        <p
          v-if="invalidConfirmation"
          id="initializeConfirmationError"
          class="initialize-error"
          role="alert"
        >
          两次输入的密码不一致
        </p>
      </div>

      <p v-if="error" class="initialize-error" role="alert">{{ error }}</p>
      <button class="btn primary" type="submit" :disabled="initializationPending">
        {{ initializationPending ? '正在初始化…' : '设置管理员密码' }}
      </button>
    </form>
  </AuthLayout>
</template>

<style scoped>
.initialize-form {
  display: grid;
  gap: 20px;
  margin-top: 28px;
}
.initialize-field {
  display: grid;
  min-width: 0;
  gap: 8px;
}
.initialize-form label {
  color: var(--text);
  font-size: 13px;
  font-weight: 600;
  line-height: 20px;
}
.initialize-form input {
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
.initialize-form input:hover:not(:disabled):not([aria-invalid='true']) {
  border-color: #aeb9c9;
}
.initialize-form input:focus-visible {
  border-color: var(--primary);
  box-shadow: 0 0 0 3px var(--primary-soft);
}
.initialize-form input:disabled {
  background: var(--page-soft);
  color: var(--muted);
  cursor: not-allowed;
}
.initialize-form button {
  width: 100%;
  min-height: 42px;
  margin-top: 4px;
  font-size: 14px;
  font-weight: 600;
  line-height: 24px;
}
.initialize-form button:focus-visible {
  outline: 2px solid var(--primary);
  outline-offset: 3px;
}
.initialize-form button:disabled {
  cursor: not-allowed;
  opacity: 0.65;
}
.initialize-error {
  margin: 0;
  padding: 10px 12px;
  border: 1px solid #ffd4d4;
  border-radius: var(--radius);
  background: #fff2f2;
  color: var(--danger);
  font-size: 13px;
  line-height: 20px;
}
.initialize-form input[aria-invalid='true'] {
  border-color: var(--danger);
}
</style>
