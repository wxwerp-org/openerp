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
.instructions {
  margin: 12px 0 28px;
  color: var(--body);
  font-size: 13px;
  line-height: 22px;
}
:deep(.password-form) {
  display: grid;
  gap: 20px;
  padding-bottom: 0;
}
:deep(.password-form .form-line) {
  display: grid;
  min-width: 0;
  gap: 8px;
}
:deep(.password-form .form-line + .form-line) { margin-top: 0; }
:deep(.password-form .form-label) {
  width: auto;
  padding: 0;
  color: var(--text);
  font-size: 13px;
  font-weight: 600;
  line-height: 20px;
  text-align: left;
}
:deep(.password-form .form-label::after) { display: none; }
:deep(.password-form .form-control-wrap) {
  display: grid;
  gap: 6px;
}
:deep(.password-form .form-control) {
  height: 42px;
  border-radius: var(--radius);
  background: var(--surface);
  transition: border-color .15s, box-shadow .15s;
}
:deep(.password-form .form-item:not(.is-error) .form-control:hover) { border-color: #AEB9C9; }
:deep(.password-form .form-control:focus-within) {
  border-color: var(--primary);
  box-shadow: 0 0 0 3px var(--primary-soft);
}
:deep(.password-form .form-control input) {
  height: 40px;
  padding: 0 12px;
  font-size: 14px;
  line-height: 20px;
}
:deep(.password-form .form-control:has(input:disabled)) { background: var(--page-soft); }
:deep(.password-form .form-control input:disabled) {
  color: var(--muted);
  cursor: not-allowed;
}
:deep(.password-form .form-item.is-error .form-control) { border-color: var(--danger); }
:deep(.password-form .form-item.is-error .form-control:focus-within) {
  box-shadow: 0 0 0 3px rgba(229, 46, 46, .10);
}
:deep(.password-form .form-error) {
  position: static;
  padding: 0;
  color: var(--danger);
  font-size: 12px;
  line-height: 18px;
  white-space: normal;
}
:deep(.account-error) {
  margin: 20px 0 0;
  padding: 10px 12px;
  border: 1px solid #FFD4D4;
  border-radius: var(--radius);
  background: #FFF2F2;
  color: var(--danger);
  font-size: 13px;
  line-height: 20px;
}
.actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 12px;
  margin-top: 28px;
  padding-top: 20px;
  border-top: 1px solid var(--line-soft);
}
.actions .btn {
  min-width: 88px;
  min-height: 36px;
  padding: 0 16px;
  font-size: 13px;
}
.actions .btn:focus-visible {
  outline: 2px solid var(--primary);
  outline-offset: 3px;
}
.actions .btn:disabled {
  cursor: not-allowed;
  opacity: .65;
}
</style>
