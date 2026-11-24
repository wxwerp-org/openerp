<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import { initializationCompleted, login } from '../account/session'
import { useAboutInfo } from '../account/aboutInfo'
import AuthLayout from '../components/layout/AuthLayout.vue'

const route = useRoute()
const username = ref('')
const password = ref('')
const error = ref('')
const submitting = ref(false)
const { versionLabel, commitLabel } = useAboutInfo()
const versionText = computed(() =>
  commitLabel.value ? `${versionLabel.value} · ${commitLabel.value}` : versionLabel.value,
)

const labelClass = 'text-13/20 font-semibold text-ink'
const inputClass =
  'min-h-42 w-full min-w-0 rounded-4 border border-input bg-white px-12 py-10 text-14/20 text-ink outline-none outline-0 transition-[border-color,box-shadow] duration-150 ease-[ease] hover:border-input-hover hover:focus-visible:border-input-hover focus-visible:border-primary focus-visible:[box-shadow:0_0_0_3px_var(--color-primary-soft)] disabled:cursor-not-allowed disabled:bg-page-soft disabled:text-muted disabled:hover:border-input'

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
    <h1 class="text-22/32 font-semibold text-ink">登录 OpenERP</h1>
    <p v-if="initializationCompleted" role="status">管理员密码初始化成功，请登录。</p>
    <p v-else-if="route.query.initialized === '1'" role="status">管理员账号已初始化，请登录。</p>
    <form class="mt-28 grid gap-20" @submit.prevent="submit">
      <div class="grid min-w-0 gap-8">
        <label :class="labelClass" for="loginUsername">用户名</label>
        <input
          id="loginUsername"
          v-model="username"
          :class="inputClass"
          autocomplete="username"
          required
          :disabled="submitting"
        />
      </div>

      <div class="grid min-w-0 gap-8">
        <label :class="labelClass" for="loginPassword">密码</label>
        <input
          id="loginPassword"
          v-model="password"
          :class="inputClass"
          type="password"
          autocomplete="current-password"
          required
          :disabled="submitting"
        />
      </div>

      <p
        v-if="error"
        class="m-0 rounded-4 border border-tag-red-border bg-danger-faint px-12 py-10 text-13/20 text-danger"
        role="alert"
      >
        {{ error }}
      </p>
      <button
        type="submit"
        class="mt-4 inline-flex min-h-42 w-full items-center justify-center whitespace-nowrap rounded-4 border border-primary bg-primary px-10 text-14/24 font-semibold text-white hover:border-primary-hover hover:bg-primary-hover focus-visible:outline-2 focus-visible:outline-offset-3 focus-visible:outline-primary disabled:cursor-not-allowed disabled:border-primary-disabled disabled:bg-primary-disabled disabled:opacity-65 disabled:hover:border-primary-disabled disabled:hover:bg-primary-disabled"
        :disabled="submitting"
      >
        {{ submitting ? '正在登录…' : '登录' }}
      </button>
    </form>
    <template #footer>
      <span data-testid="login-version">{{ versionText }}</span>
    </template>
  </AuthLayout>
</template>
