<script setup lang="ts">
import { nextTick, reactive, ref, watch } from 'vue'
import { changePassword } from './session'

const props = withDefaults(
  defineProps<{ active?: boolean }>(),
  { active: true },
)
const emit = defineEmits<{ saved: [] }>()

const oldPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const serverError = ref('')
const submitting = ref(false)
const errors = reactive({ old: false, next: false, confirm: false })
const oldPasswordInput = ref<HTMLInputElement | null>(null)
const newPasswordInput = ref<HTMLInputElement | null>(null)
const confirmPasswordInput = ref<HTMLInputElement | null>(null)

watch(() => props.active, async (open) => {
  oldPassword.value = ''
  newPassword.value = ''
  confirmPassword.value = ''
  serverError.value = ''
  Object.assign(errors, { old: false, next: false, confirm: false })
  if (!open) return
  await nextTick()
  oldPasswordInput.value?.focus()
}, { immediate: true })

async function save(): Promise<void> {
  if (submitting.value) return
  serverError.value = ''
  errors.old = !oldPassword.value
  errors.next = newPassword.value.length < 6 || newPassword.value.length > 20
  errors.confirm = !confirmPassword.value || confirmPassword.value !== newPassword.value
  if (errors.old) oldPasswordInput.value?.focus()
  else if (errors.next) newPasswordInput.value?.focus()
  else if (errors.confirm) confirmPasswordInput.value?.focus()
  if (errors.old || errors.next || errors.confirm) return
  submitting.value = true
  try {
    await changePassword({
      oldPassword: oldPassword.value,
      newPassword: newPassword.value,
    })
    emit('saved')
  } catch (cause) {
    serverError.value = cause instanceof Error && !(cause instanceof TypeError)
      ? cause.message
      : '无法修改密码，请稍后重试。'
  } finally {
    submitting.value = false
  }
}

defineExpose({ save, submitting })
</script>

<template>
  <div class="profile-form password-form">
    <div class="form-line form-item" :class="{ 'is-error': errors.old }">
      <label class="form-label" for="oldPassword"><span class="req">*</span>原密码</label>
      <div class="form-control-wrap">
        <div class="form-control">
          <input id="oldPassword" ref="oldPasswordInput" v-model="oldPassword" type="password" required :disabled="submitting" :aria-invalid="errors.old" aria-describedby="oldPasswordError" @input="errors.old = false" placeholder="请输入原密码" autocomplete="current-password">
        </div>
        <div id="oldPasswordError" class="form-error" role="alert">请输入原密码</div>
      </div>
    </div>
    <div class="form-line form-item" :class="{ 'is-error': errors.next }">
      <label class="form-label" for="newPassword"><span class="req">*</span>新密码</label>
      <div class="form-control-wrap">
        <div class="form-control">
          <input id="newPassword" ref="newPasswordInput" v-model="newPassword" type="password" required :disabled="submitting" :aria-invalid="errors.next" aria-describedby="newPasswordError" @input="errors.next = false" placeholder="6-20 位字符" maxlength="20" autocomplete="new-password">
        </div>
        <div id="newPasswordError" class="form-error" role="alert">密码长度需为 6-20 位</div>
      </div>
    </div>
    <div class="form-line form-item" :class="{ 'is-error': errors.confirm }">
      <label class="form-label" for="confirmPassword"><span class="req">*</span>确认新密码</label>
      <div class="form-control-wrap">
        <div class="form-control">
          <input id="confirmPassword" ref="confirmPasswordInput" v-model="confirmPassword" type="password" required :disabled="submitting" :aria-invalid="errors.confirm" aria-describedby="confirmPasswordError" @input="errors.confirm = false" placeholder="请再次输入新密码" maxlength="20" autocomplete="new-password">
        </div>
        <div id="confirmPasswordError" class="form-error" role="alert">两次输入的密码不一致</div>
      </div>
    </div>
  </div>
  <p v-if="serverError" class="account-error" role="alert">{{ serverError }}</p>
</template>
