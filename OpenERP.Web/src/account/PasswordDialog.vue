<script setup lang="ts">
import { reactive, ref, watch } from 'vue'

const props = defineProps<{ open: boolean }>()
import { useNativeDialog } from '../composables/useNativeDialog'
const dialog = useNativeDialog(() => props.open)
void dialog

const emit = defineEmits<{
  close: []
  feedback: [message: string]
}>()

const oldPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const errors = reactive({ old: false, next: false, confirm: false })
const oldPasswordInput = ref<HTMLInputElement | null>(null)
const newPasswordInput = ref<HTMLInputElement | null>(null)
const confirmPasswordInput = ref<HTMLInputElement | null>(null)

watch(() => props.open, (open) => {
  if (!open) return
  window.setTimeout(() => {
    oldPasswordInput.value?.focus()
  }, 0)
}, { immediate: true })

function save(): void {
  errors.old = !oldPassword.value
  errors.next = newPassword.value.length < 6 || newPassword.value.length > 20
  errors.confirm = !confirmPassword.value || confirmPassword.value !== newPassword.value
  if (errors.old) oldPasswordInput.value?.focus()
  else if (errors.next) newPasswordInput.value?.focus()
  else if (errors.confirm) confirmPasswordInput.value?.focus()
  if (errors.old || errors.next || errors.confirm) return
  emit('close')
  emit('feedback', '原型演示：密码修改成功')
}
</script>

<template>
<dialog ref="dialog" class="profile-layer" id="passwordLayer" :class="{ open }" aria-labelledby="passwordTitle" @close="emit('close')">
        <div class="profile-dialog password-dialog" aria-labelledby="passwordTitle">
          <div class="profile-head">
            <div class="profile-title" id="passwordTitle">修改密码</div>
            <button class="form-close" type="button" id="closePassword" @click="emit('close')" title="关闭" aria-label="关闭"><svg aria-hidden="true" class="ic"><use href="#i-close"/></svg></button>
          </div>
          <div class="profile-body">
            <div class="profile-form password-form">
              <div class="form-line form-item" :class="{ 'is-error': errors.old }">
                <label class="form-label" for="oldPassword"><span class="req">*</span>原密码</label>
                <div class="form-control-wrap">
                  <div class="form-control"><input id="oldPassword" ref="oldPasswordInput" v-model="oldPassword" type="password" required :aria-invalid="errors.old" aria-describedby="oldPasswordError" @input="errors.old = false" placeholder="请输入原密码" maxlength="20" autocomplete="current-password"></div>
                  <div id="oldPasswordError" class="form-error" role="alert">请输入原密码</div>
                </div>
              </div>
              <div class="form-line form-item" :class="{ 'is-error': errors.next }">
                <label class="form-label" for="newPassword"><span class="req">*</span>新密码</label>
                <div class="form-control-wrap">
                  <div class="form-control"><input id="newPassword" ref="newPasswordInput" v-model="newPassword" type="password" required :aria-invalid="errors.next" aria-describedby="newPasswordError" @input="errors.next = false" placeholder="6-20 位字符" maxlength="20" autocomplete="new-password"></div>
                  <div id="newPasswordError" class="form-error" role="alert">密码长度需为 6-20 位</div>
                </div>
              </div>
              <div class="form-line form-item" :class="{ 'is-error': errors.confirm }">
                <label class="form-label" for="confirmPassword"><span class="req">*</span>确认新密码</label>
                <div class="form-control-wrap">
                  <div class="form-control"><input id="confirmPassword" ref="confirmPasswordInput" v-model="confirmPassword" type="password" required :aria-invalid="errors.confirm" aria-describedby="confirmPasswordError" @input="errors.confirm = false" placeholder="请再次输入新密码" maxlength="20" autocomplete="new-password"></div>
                  <div id="confirmPasswordError" class="form-error" role="alert">两次输入的密码不一致</div>
                </div>
              </div>
            </div>
          </div>
          <div class="profile-foot">
            <button class="btn" type="button" id="cancelPassword" @click="emit('close')">取消</button>
            <button class="btn primary" type="button" id="savePassword" @click="save">确认修改</button>
          </div>
        </div>
      </dialog>
</template>
