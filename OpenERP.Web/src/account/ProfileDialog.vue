<script setup lang="ts">
import { ref, watch } from 'vue'

const props = defineProps<{ open: boolean }>()
import { useNativeDialog } from '../composables/useNativeDialog'
const dialog = useNativeDialog(() => props.open)
void dialog

const emit = defineEmits<{
  close: []
  feedback: [message: string]
}>()

const name = ref('张明')
const phone = ref('138 0013 8000')
const email = ref('admin@openerp.com')
const title = ref('系统管理员')
const nameError = ref(false)
const nameInput = ref<HTMLInputElement | null>(null)

watch(() => props.open, (open) => {
  if (!open) return
  window.setTimeout(() => {
    nameInput.value?.focus()
    nameInput.value?.select()
  }, 0)
}, { immediate: true })

function save(): void {
  if (!name.value.trim()) {
    nameError.value = true
    nameInput.value?.focus()
    return
  }
  emit('close')
  emit('feedback', '原型演示：个人信息已保存')
}
</script>

<template>
<dialog ref="dialog" class="profile-layer" id="profileLayer" :class="{ open }" aria-labelledby="profileTitle" @close="emit('close')">
        <div class="profile-dialog" aria-labelledby="profileTitle">
          <div class="profile-head">
            <div class="profile-title" id="profileTitle">修改个人信息</div>
            <button class="form-close" type="button" id="closeProfile" @click="emit('close')" title="关闭" aria-label="关闭"><svg aria-hidden="true" class="ic"><use href="#i-close"/></svg></button>
          </div>
          <div class="profile-body">
            <div class="profile-avatar-row">
              <span class="profile-avatar"><svg aria-hidden="true" class="ic"><use href="#i-avatar"/></svg></span>
              <div class="profile-avatar-info">
                <b>超级管理员</b>
                <span>上次登录：2026-11-13 09:16</span>
              </div>
              <button class="btn" type="button" id="changeAvatar" @click="emit('feedback', '原型演示：头像上传暂未接入')">更换头像</button>
            </div>
            <div class="profile-form">
              <div class="form-line form-item" :class="{ 'is-error': nameError }">
                <label class="form-label" for="profileName"><span class="req">*</span>姓名</label>
                <div class="form-control-wrap">
                  <div class="form-control"><input id="profileName" ref="nameInput" v-model="name" type="text" required :aria-invalid="nameError" aria-describedby="profileNameError" @input="nameError = false" placeholder="请输入姓名" maxlength="20"></div>
                  <div id="profileNameError" class="form-error" role="alert">请填写姓名</div>
                </div>
              </div>
              <div class="form-line form-item">
                <label class="form-label" for="profilePhone">手机号</label>
                <div class="form-control-wrap">
                  <div class="form-control"><input id="profilePhone" v-model="phone" type="text" autocomplete="tel" placeholder="请输入手机号" maxlength="20"></div>
                </div>
              </div>
              <div class="form-line form-item">
                <label class="form-label" for="profileEmail">邮箱</label>
                <div class="form-control-wrap">
                  <div class="form-control"><input id="profileEmail" v-model="email" type="email" autocomplete="email" placeholder="请输入邮箱"></div>
                </div>
              </div>
              <div class="form-line form-item">
                <label class="form-label" for="profileDepartment">部门</label>
                <div class="form-control-wrap">
                  <div class="form-control is-readonly"><input id="profileDepartment" type="text" value="运营中心" readonly></div>
                </div>
              </div>
              <div class="form-line form-item">
                <label class="form-label" for="profileTitleInput">职位</label>
                <div class="form-control-wrap">
                  <div class="form-control"><input id="profileTitleInput" v-model="title" type="text" placeholder="请输入职位" maxlength="20"></div>
                </div>
              </div>
            </div>
          </div>
          <div class="profile-foot">
            <button class="btn" type="button" id="cancelProfile" @click="emit('close')">取消</button>
            <button class="btn primary" type="button" id="saveProfile" @click="save">保存</button>
          </div>
        </div>
      </dialog>
</template>
