<script setup lang="ts">
import { computed, nextTick, reactive, ref, watch } from 'vue'
import AppButton from '../ui/AppButton.vue'
import AppDialog from '../ui/AppDialog.vue'
import AppDialogClose from '../ui/AppDialogClose.vue'
import AppField from '../ui/AppField.vue'
import AppIcon from '../ui/AppIcon.vue'
import { saveProfile, type Account } from './session'

const props = defineProps<{ open: boolean; account: Account }>()

const emit = defineEmits<{
  close: []
  closed: []
  feedback: [message: string]
}>()

const name = ref('')
const phone = ref('')
const email = ref('')
const errors = reactive({ name: false, phone: false, email: false })
const serverError = ref('')
const submitting = ref(false)
const nameInput = ref<HTMLInputElement | null>(null)
const phoneInput = ref<HTMLInputElement | null>(null)
const emailInput = ref<HTMLInputElement | null>(null)

const inputBaseClass =
  'h-30 w-full min-w-0 rounded-4 border-0 border-current bg-transparent px-11 py-0 text-12/30 outline-none placeholder:text-placeholder'
const textInputClass = `${inputBaseClass} text-ink`
const readonlyInputClass = `${inputBaseClass} cursor-default text-muted`
const errorClass = 'absolute left-0 top-full pt-3 text-12/16 text-danger whitespace-nowrap'
const hiddenClass = 'hidden'

function controlClass(invalid: boolean): string {
  return invalid
    ? 'flex h-32 w-full items-center rounded-4 border border-danger bg-white hover:border-danger focus-within:border-danger'
    : 'flex h-32 w-full items-center rounded-4 border border-input bg-white hover:border-input-hover focus-within:border-primary'
}

const lastLogin = computed(() => {
  const timestamp = props.account.lastLoginAt
  return timestamp ? new Date(timestamp).toLocaleString('zh-CN') : ''
})

watch(
  () => props.open,
  async (open) => {
    if (!open) return
    name.value = props.account.name
    phone.value = props.account.phone ?? ''
    email.value = props.account.email ?? ''
    Object.assign(errors, { name: false, phone: false, email: false })
    serverError.value = ''
    // AppDialog 在自身的下一拍才调用 showModal，因此再等一拍确保弹窗已打开。
    await nextTick()
    await nextTick()
    nameInput.value?.focus()
    nameInput.value?.select()
  },
  { immediate: true },
)

function cancel(): void {
  if (!submitting.value) emit('close')
}

async function save(): Promise<void> {
  if (submitting.value) return
  serverError.value = ''
  const fields = { name: name.value.trim(), phone: phone.value.trim(), email: email.value.trim() }
  errors.name = !fields.name || fields.name.length > 20
  errors.phone = fields.phone.length > 20
  errors.email = !!fields.email && !/^[^\s@]+@[^\s@]+$/.test(fields.email)
  if (errors.name) nameInput.value?.focus()
  else if (errors.phone) phoneInput.value?.focus()
  else if (errors.email) emailInput.value?.focus()
  if (errors.name || errors.phone || errors.email) return
  submitting.value = true
  try {
    await saveProfile(fields)
    emit('close')
    emit('feedback', '个人信息已保存')
  } catch (cause) {
    serverError.value =
      cause instanceof Error && !(cause instanceof TypeError)
        ? cause.message
        : '无法保存个人信息，请稍后重试。'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <AppDialog
    id="profileLayer"
    :open="open"
    layer="profile"
    backdrop="none"
    aria-labelledby="profileTitle"
    class="bg-mask"
    @close="cancel"
    @closed="emit('closed')"
  >
    <div class="absolute inset-0 flex items-center justify-center">
      <form
        class="w-520 max-w-[calc(100%_-_40px)] overflow-hidden rounded-8 bg-white shadow-dialog"
        novalidate
        @submit.prevent="save"
      >
        <div class="flex h-52 items-center justify-between border-b border-line pr-16 pl-20">
          <div id="profileTitle" class="text-dialog-title font-semibold text-ink">修改个人信息</div>
          <AppDialogClose id="closeProfile" :disabled="submitting" @click="cancel" />
        </div>
        <div class="max-h-[calc(100vh_-_220px)] overflow-y-auto px-24 pt-20 pb-4">
          <div class="mb-18 flex items-center gap-12 border-b border-line-soft pb-16">
            <span
              class="flex h-52 w-52 flex-none items-center justify-center rounded-full bg-nav-active text-primary"
            >
              <AppIcon name="i-avatar" :size="28" />
            </span>
            <div class="min-w-0">
              <b class="block text-14/20 font-semibold text-ink">{{ account.name }}</b>
              <span v-if="account.lastLoginAt" class="text-12/18 text-muted"
                >最近登录 <time :datetime="account.lastLoginAt">{{ lastLogin }}</time></span
              >
            </div>
          </div>
          <div class="flex flex-col gap-14">
            <AppField label="用户名" html-for="profileUsername">
              <div
                class="flex h-32 w-full items-center rounded-4 border border-input bg-tag-gray-bg hover:border-input-hover focus-within:border-primary"
              >
                <input
                  id="profileUsername"
                  :value="account.username"
                  readonly
                  :class="readonlyInputClass"
                />
              </div>
            </AppField>
            <AppField
              label="姓名"
              html-for="profileName"
              required
              :data-invalid="errors.name ? 'true' : undefined"
            >
              <div :class="controlClass(errors.name)">
                <input
                  id="profileName"
                  ref="nameInput"
                  v-model="name"
                  required
                  :aria-invalid="errors.name"
                  aria-describedby="profileNameError"
                  placeholder="请输入姓名"
                  maxlength="20"
                  :disabled="submitting"
                  :class="textInputClass"
                  @input="errors.name = false"
                />
              </div>
              <div
                id="profileNameError"
                role="alert"
                :class="errors.name ? errorClass : hiddenClass"
              >
                请填写姓名，最多 20 个字符
              </div>
            </AppField>
            <AppField
              label="手机号"
              html-for="profilePhone"
              :data-invalid="errors.phone ? 'true' : undefined"
            >
              <div :class="controlClass(errors.phone)">
                <input
                  id="profilePhone"
                  ref="phoneInput"
                  v-model="phone"
                  type="tel"
                  autocomplete="tel"
                  :aria-invalid="errors.phone"
                  aria-describedby="profilePhoneError"
                  placeholder="请输入手机号"
                  maxlength="20"
                  :disabled="submitting"
                  :class="textInputClass"
                  @input="errors.phone = false"
                />
              </div>
              <div
                id="profilePhoneError"
                role="alert"
                :class="errors.phone ? errorClass : hiddenClass"
              >
                手机号最多 20 个字符
              </div>
            </AppField>
            <AppField
              label="邮箱"
              html-for="profileEmail"
              :data-invalid="errors.email ? 'true' : undefined"
            >
              <div :class="controlClass(errors.email)">
                <input
                  id="profileEmail"
                  ref="emailInput"
                  v-model="email"
                  type="email"
                  autocomplete="email"
                  :aria-invalid="errors.email"
                  aria-describedby="profileEmailError"
                  placeholder="请输入邮箱"
                  :disabled="submitting"
                  :class="textInputClass"
                  @input="errors.email = false"
                />
              </div>
              <div
                id="profileEmailError"
                role="alert"
                :class="errors.email ? errorClass : hiddenClass"
              >
                请输入有效的邮箱地址
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
        </div>
        <div class="flex h-60 items-center justify-end gap-8 border-t border-line px-20">
          <AppButton
            id="cancelProfile"
            size="lg"
            class="min-w-64 px-16"
            type="button"
            :disabled="submitting"
            @click="cancel"
          >
            取消
          </AppButton>
          <AppButton
            id="saveProfile"
            size="lg"
            variant="primary"
            type="submit"
            :disabled="submitting"
            class="min-w-64 px-16"
          >
            保存
          </AppButton>
        </div>
      </form>
    </div>
  </AppDialog>
</template>
