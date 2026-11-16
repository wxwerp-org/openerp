<script setup lang="ts">
import { computed, nextTick, reactive, ref, watch } from 'vue'
import { useNativeDialog } from '../composables/useNativeDialog'
import { saveProfile, type Account } from './session'

const props = defineProps<{ open: boolean; account: Account }>()
const dialog = useNativeDialog(() => props.open)

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

const lastLogin = computed(() => {
  const timestamp = props.account.lastLoginAt
  return timestamp ? new Date(timestamp).toLocaleString('zh-CN') : ''
})

watch(() => props.open, async (open) => {
  if (!open) return
  name.value = props.account.name
  phone.value = props.account.phone ?? ''
  email.value = props.account.email ?? ''
  Object.assign(errors, { name: false, phone: false, email: false })
  serverError.value = ''
  await nextTick()
  nameInput.value?.focus()
  nameInput.value?.select()
}, { immediate: true })

function cancel(event?: Event): void {
  event?.preventDefault()
  if (dialog.value?.open && !submitting.value) emit('close')
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
    serverError.value = cause instanceof Error && !(cause instanceof TypeError)
      ? cause.message
      : '无法保存个人信息，请稍后重试。'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <dialog id="profileLayer" ref="dialog" class="profile-layer" :class="{ open }" aria-labelledby="profileTitle" @cancel="cancel" @close="emit('closed')">
    <form class="profile-dialog" novalidate @submit.prevent="save">
      <div class="profile-head">
        <div id="profileTitle" class="profile-title">修改个人信息</div>
        <button id="closeProfile" class="form-close" type="button" :disabled="submitting" @click="cancel" title="关闭" aria-label="关闭">
          <svg aria-hidden="true" class="ic"><use href="#i-close" /></svg>
        </button>
      </div>
      <div class="profile-body">
        <div class="profile-avatar-row">
          <span class="profile-avatar"><svg aria-hidden="true" class="ic"><use href="#i-avatar" /></svg></span>
          <div class="profile-avatar-info">
            <b>{{ account.name }}</b>
            <span v-if="account.lastLoginAt">最近登录 <time :datetime="account.lastLoginAt">{{ lastLogin }}</time></span>
          </div>
        </div>
        <div class="profile-form">
          <div class="form-line form-item">
            <label class="form-label" for="profileUsername">用户名</label>
            <div class="form-control-wrap">
              <div class="form-control is-readonly"><input id="profileUsername" :value="account.username" readonly></div>
            </div>
          </div>
          <div class="form-line form-item" :class="{ 'is-error': errors.name }">
            <label class="form-label" for="profileName"><span class="req">*</span>姓名</label>
            <div class="form-control-wrap">
              <div class="form-control">
                <input id="profileName" ref="nameInput" v-model="name" required :aria-invalid="errors.name" aria-describedby="profileNameError" @input="errors.name = false" placeholder="请输入姓名" maxlength="20" :disabled="submitting">
              </div>
              <div id="profileNameError" class="form-error" role="alert">请填写姓名，最多 20 个字符</div>
            </div>
          </div>
          <div class="form-line form-item" :class="{ 'is-error': errors.phone }">
            <label class="form-label" for="profilePhone">手机号</label>
            <div class="form-control-wrap">
              <div class="form-control">
                <input id="profilePhone" ref="phoneInput" v-model="phone" type="tel" autocomplete="tel" :aria-invalid="errors.phone" aria-describedby="profilePhoneError" @input="errors.phone = false" placeholder="请输入手机号" maxlength="20" :disabled="submitting">
              </div>
              <div id="profilePhoneError" class="form-error" role="alert">手机号最多 20 个字符</div>
            </div>
          </div>
          <div class="form-line form-item" :class="{ 'is-error': errors.email }">
            <label class="form-label" for="profileEmail">邮箱</label>
            <div class="form-control-wrap">
              <div class="form-control">
                <input id="profileEmail" ref="emailInput" v-model="email" type="email" autocomplete="email" :aria-invalid="errors.email" aria-describedby="profileEmailError" @input="errors.email = false" placeholder="请输入邮箱" :disabled="submitting">
              </div>
              <div id="profileEmailError" class="form-error" role="alert">请输入有效的邮箱地址</div>
            </div>
          </div>
        </div>
        <p v-if="serverError" class="account-error" role="alert">{{ serverError }}</p>
      </div>
      <div class="profile-foot">
        <button id="cancelProfile" class="btn" type="button" :disabled="submitting" @click="cancel">取消</button>
        <button id="saveProfile" class="btn primary" type="submit" :disabled="submitting">保存</button>
      </div>
    </form>
  </dialog>
</template>
