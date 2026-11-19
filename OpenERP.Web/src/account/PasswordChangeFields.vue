<script setup lang="ts">
import { nextTick, reactive, ref, watch } from 'vue'
import AppField from '../ui/AppField.vue'
import { changePassword } from './session'

const props = withDefaults(defineProps<{ active?: boolean }>(), { active: true })
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

const inputBaseClass =
  'h-30 w-full min-w-0 rounded-4 border-0 border-current bg-transparent px-11 py-0 text-12/30 text-ink outline-none placeholder:text-placeholder'
const errorClass = 'absolute left-0 top-full pt-3 text-12/16 text-danger whitespace-nowrap'
const hiddenClass = 'hidden'

function controlClass(invalid: boolean): string {
  return invalid
    ? 'flex h-32 w-full items-center rounded-4 border border-danger bg-white hover:border-danger focus-within:border-danger'
    : 'flex h-32 w-full items-center rounded-4 border border-input bg-white hover:border-input-hover focus-within:border-primary'
}

watch(
  () => props.active,
  async (open) => {
    oldPassword.value = ''
    newPassword.value = ''
    confirmPassword.value = ''
    serverError.value = ''
    Object.assign(errors, { old: false, next: false, confirm: false })
    if (!open) return
    // AppDialog 在自身的下一拍才调用 showModal，因此再等一拍确保弹窗已打开。
    await nextTick()
    await nextTick()
    oldPasswordInput.value?.focus()
  },
  { immediate: true },
)

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
    serverError.value =
      cause instanceof Error && !(cause instanceof TypeError)
        ? cause.message
        : '无法修改密码，请稍后重试。'
  } finally {
    submitting.value = false
  }
}

defineExpose({ save, submitting })
</script>

<template>
  <div class="flex flex-col gap-18 pb-16">
    <AppField
      label="原密码"
      html-for="oldPassword"
      required
      :data-invalid="errors.old ? 'true' : undefined"
    >
      <div :class="controlClass(errors.old)">
        <input
          id="oldPassword"
          ref="oldPasswordInput"
          v-model="oldPassword"
          type="password"
          required
          :disabled="submitting"
          :aria-invalid="errors.old"
          aria-describedby="oldPasswordError"
          placeholder="请输入原密码"
          autocomplete="current-password"
          :class="inputBaseClass"
          @input="errors.old = false"
        />
      </div>
      <div id="oldPasswordError" role="alert" :class="errors.old ? errorClass : hiddenClass">
        请输入原密码
      </div>
    </AppField>
    <AppField
      label="新密码"
      html-for="newPassword"
      required
      :data-invalid="errors.next ? 'true' : undefined"
    >
      <div :class="controlClass(errors.next)">
        <input
          id="newPassword"
          ref="newPasswordInput"
          v-model="newPassword"
          type="password"
          required
          :disabled="submitting"
          :aria-invalid="errors.next"
          aria-describedby="newPasswordError"
          placeholder="6-20 位字符"
          maxlength="20"
          autocomplete="new-password"
          :class="inputBaseClass"
          @input="errors.next = false"
        />
      </div>
      <div id="newPasswordError" role="alert" :class="errors.next ? errorClass : hiddenClass">
        密码长度需为 6-20 位
      </div>
    </AppField>
    <AppField
      label="确认新密码"
      html-for="confirmPassword"
      required
      :data-invalid="errors.confirm ? 'true' : undefined"
    >
      <div :class="controlClass(errors.confirm)">
        <input
          id="confirmPassword"
          ref="confirmPasswordInput"
          v-model="confirmPassword"
          type="password"
          required
          :disabled="submitting"
          :aria-invalid="errors.confirm"
          aria-describedby="confirmPasswordError"
          placeholder="请再次输入新密码"
          maxlength="20"
          autocomplete="new-password"
          :class="inputBaseClass"
          @input="errors.confirm = false"
        />
      </div>
      <div
        id="confirmPasswordError"
        role="alert"
        :class="errors.confirm ? errorClass : hiddenClass"
      >
        两次输入的密码不一致
      </div>
    </AppField>
  </div>
  <p
    v-if="serverError"
    role="alert"
    data-testid="account-error"
    class="px-16 py-8 text-12/18 text-danger"
  >
    {{ serverError }}
  </p>
</template>
