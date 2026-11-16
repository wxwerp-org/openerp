<script setup lang="ts">
import { ref } from 'vue'
import PasswordChangeFields from '../account/PasswordChangeFields.vue'
import { logout } from '../account/session'
import AuthLayout from '../components/layout/AuthLayout.vue'

const fields = ref<InstanceType<typeof PasswordChangeFields> | null>(null)
const loggingOut = ref(false)
const logoutError = ref('')

async function signOut(): Promise<void> {
  if (loggingOut.value || fields.value?.submitting) return
  logoutError.value = ''
  loggingOut.value = true
  try {
    await logout()
  } catch (cause) {
    logoutError.value = cause instanceof Error && !(cause instanceof TypeError)
      ? cause.message
      : '退出登录失败，请稍后重试。'
  } finally {
    loggingOut.value = false
  }
}

function save(): void {
  logoutError.value = ''
  void fields.value?.save()
}
</script>

<template>
  <AuthLayout>
    <h1>首次登录须修改密码</h1>
    <p class="instructions">请将临时密码换成自己的密码，然后用新密码重新登录。</p>
    <form novalidate @submit.prevent="save">
      <PasswordChangeFields ref="fields" :disabled="loggingOut" :require-old-password="false" />
      <p v-if="logoutError" class="account-error" role="alert">{{ logoutError }}</p>
      <div class="actions">
        <button class="btn" type="button" :disabled="loggingOut || fields?.submitting" @click="signOut">退出登录</button>
        <button class="btn primary" type="submit" :disabled="loggingOut || fields?.submitting">确认修改</button>
      </div>
    </form>
  </AuthLayout>
</template>

<style scoped>
.instructions { margin: 12px 0 24px; }
.actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 8px; margin-top: 12px; }
.actions .btn { min-height: 36px; }
</style>
