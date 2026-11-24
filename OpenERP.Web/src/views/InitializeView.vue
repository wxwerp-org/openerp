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

const labelClass = 'text-13/20 font-semibold text-ink'
const errorClass =
  'm-0 rounded-4 border border-tag-red-border bg-danger-faint px-12 py-10 text-13/20 text-danger'
const inputClass =
  'min-h-42 w-full min-w-0 rounded-4 border bg-white px-12 py-10 text-14/20 text-ink outline-none outline-0 transition-[border-color,box-shadow] duration-150 ease-[ease] focus-visible:[box-shadow:0_0_0_3px_var(--color-primary-soft)] disabled:cursor-not-allowed disabled:bg-page-soft disabled:text-muted'
const validBorderClass =
  'border-input hover:border-input-hover hover:focus-visible:border-input-hover focus-visible:border-primary disabled:hover:border-input'
const invalidBorderClass = 'border-danger'

function inputState(invalid: boolean): string {
  return `${inputClass} ${invalid ? invalidBorderClass : validBorderClass}`
}

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
    <h1 class="text-22/32 font-semibold text-ink">初始化 OpenERP</h1>
    <form class="mt-28 grid gap-20" novalidate @submit.prevent="submit">
      <div class="grid min-w-0 gap-8">
        <label :class="labelClass" for="initializeUsername">用户名</label>
        <input
          id="initializeUsername"
          :class="inputState(false)"
          value="admin"
          autocomplete="username"
          readonly
        />
      </div>

      <div class="grid min-w-0 gap-8">
        <label :class="labelClass" for="newPassword">新密码</label>
        <input
          id="newPassword"
          ref="newPasswordInput"
          v-model="newPassword"
          :class="inputState(invalidPassword)"
          type="password"
          autocomplete="new-password"
          placeholder="6-20 位字符"
          required
          :disabled="initializationPending"
          :aria-invalid="invalidPassword"
          :aria-describedby="invalidPassword ? 'initializePasswordError' : undefined"
          @input="invalidPassword = false"
        />
        <p v-if="invalidPassword" id="initializePasswordError" :class="errorClass" role="alert">
          密码长度需为 6-20 位
        </p>
      </div>

      <div class="grid min-w-0 gap-8">
        <label :class="labelClass" for="confirmPassword">确认新密码</label>
        <input
          id="confirmPassword"
          ref="confirmPasswordInput"
          v-model="confirmPassword"
          :class="inputState(invalidConfirmation)"
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
          :class="errorClass"
          role="alert"
        >
          两次输入的密码不一致
        </p>
      </div>

      <p v-if="error" :class="errorClass" role="alert">{{ error }}</p>
      <button
        type="submit"
        class="mt-4 inline-flex min-h-42 w-full items-center justify-center whitespace-nowrap rounded-4 border border-primary bg-primary px-10 text-14/24 font-semibold text-white hover:border-primary-hover hover:bg-primary-hover focus-visible:outline-2 focus-visible:outline-offset-3 focus-visible:outline-primary disabled:cursor-not-allowed disabled:border-primary-disabled disabled:bg-primary-disabled disabled:opacity-65 disabled:hover:border-primary-disabled disabled:hover:bg-primary-disabled"
        :disabled="initializationPending"
      >
        {{ initializationPending ? '正在初始化…' : '设置管理员密码' }}
      </button>
    </form>
  </AuthLayout>
</template>
