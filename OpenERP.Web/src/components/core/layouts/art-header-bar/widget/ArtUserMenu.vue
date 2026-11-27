<!-- 用户菜单 -->
<template>
  <ElPopover
    ref="userMenuPopover"
    placement="bottom-end"
    :width="240"
    :hide-after="0"
    :offset="10"
    trigger="hover"
    :show-arrow="false"
    popper-class="user-menu-popover"
    popper-style="padding: 5px 16px;"
  >
    <template #reference>
      <img
        class="size-8.5 mr-5 c-p rounded-full max-sm:w-6.5 max-sm:h-6.5 max-sm:mr-[16px]"
        :src="userInfo.icon || defaultAvatar"
        alt="avatar"
        @click="openProfile"
      />
    </template>
    <template #default>
      <div class="pt-3">
        <div class="flex-c pb-1 px-0">
          <img
            class="w-10 h-10 mr-3 ml-0 overflow-hidden rounded-full float-left"
            :src="userInfo.icon || defaultAvatar"
          />
          <div class="w-[calc(100%-60px)] h-full">
            <span class="block text-sm font-medium text-g-800 truncate">{{
              userInfo.nickname || userInfo.userName
            }}</span>
            <span class="block mt-0.5 text-xs text-g-500 truncate">{{ userInfo.email }}</span>
          </div>
        </div>
        <ul class="py-4 mt-3 border-t border-g-300/80">
          <li class="btn-item" @click="openProfile">
            <ArtSvgIcon icon="ri:user-3-line" />
            <span>个人资料</span>
          </li>
          <li class="btn-item" @click="openPassword">
            <ArtSvgIcon icon="ri:lock-password-line" />
            <span>修改密码</span>
          </li>
          <div class="w-full h-px my-2 bg-g-300/80"></div>
          <div class="log-out c-p" @click="loginOut">
            {{ $t('topBar.user.logout') }}
          </div>
        </ul>
      </div>
    </template>
  </ElPopover>

  <!-- 个人资料 -->
  <ElDialog v-model="profileVisible" title="个人资料" width="440px" append-to-body>
    <ElForm ref="profileRef" :model="profile" :rules="profileRules" label-width="70px">
      <ElFormItem label="用户名">
        <ElInput :model-value="userInfo.userName" disabled />
      </ElFormItem>
      <ElFormItem label="姓名" prop="name">
        <ElInput v-model.trim="profile.name" maxlength="20" />
      </ElFormItem>
      <ElFormItem label="手机号" prop="phone">
        <ElInput v-model.trim="profile.phone" maxlength="20" />
      </ElFormItem>
      <ElFormItem label="邮箱" prop="email">
        <ElInput v-model.trim="profile.email" />
      </ElFormItem>
    </ElForm>
    <template #footer>
      <ElButton :disabled="saving" @click="profileVisible = false">取消</ElButton>
      <ElButton type="primary" :loading="saving" @click="submitProfile">保存</ElButton>
    </template>
  </ElDialog>

  <!-- 修改密码：成功后会话失效，需要重新登录 -->
  <ElDialog v-model="passwordVisible" title="修改密码" width="440px" append-to-body>
    <ElForm ref="passwordRef" :model="password" :rules="passwordRules" label-width="80px">
      <ElFormItem label="当前密码" prop="oldPassword">
        <ElInput
          v-model="password.oldPassword"
          type="password"
          show-password
          autocomplete="current-password"
        />
      </ElFormItem>
      <ElFormItem label="新密码" prop="newPassword">
        <ElInput
          v-model="password.newPassword"
          type="password"
          show-password
          autocomplete="new-password"
        />
      </ElFormItem>
      <ElFormItem label="确认密码" prop="confirm">
        <ElInput
          v-model="password.confirm"
          type="password"
          show-password
          autocomplete="new-password"
        />
      </ElFormItem>
    </ElForm>
    <template #footer>
      <ElButton :disabled="saving" @click="passwordVisible = false">取消</ElButton>
      <ElButton type="primary" :loading="saving" @click="submitPassword">确认修改</ElButton>
    </template>
  </ElDialog>
</template>

<script setup lang="ts">
  import { useI18n } from 'vue-i18n'
  import { ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
  import { useUserStore } from '@/store/modules/user'
  import { account, changePassword, saveProfile } from '@/api/auth'
  import defaultAvatar from '@/assets/images/user/avatar.webp'

  defineOptions({ name: 'ArtUserMenu' })

  const { t } = useI18n()
  const userStore = useUserStore()

  const { getUserInfo: userInfo } = storeToRefs(userStore)
  const userMenuPopover = ref()

  // ---------- 个人资料 / 修改密码（OpenERP.Api `/api/account/*`） ----------
  const saving = ref(false)
  const profileVisible = ref(false)
  const profileRef = ref<FormInstance>()
  const profile = reactive({ name: '', phone: '', email: '' })
  const profileRules: FormRules = {
    name: [{ required: true, message: '请输入姓名', trigger: 'blur' }],
    email: [{ type: 'email', message: '邮箱格式不正确', trigger: 'blur' }]
  }

  const openProfile = (): void => {
    const current = account.value
    Object.assign(profile, {
      name: current?.name ?? '',
      phone: current?.phone ?? '',
      email: current?.email ?? ''
    })
    profileVisible.value = true
    closeUserMenu()
  }

  const submitProfile = async (): Promise<void> => {
    if (saving.value || !(await profileRef.value?.validate().catch(() => false))) return
    saving.value = true
    try {
      await saveProfile({ ...profile })
      // 账号已更新：同步用户 store 里的展示信息
      const current = account.value
      if (current) {
        userStore.setUserInfo({
          ...userStore.info,
          nickname: current.name,
          tel: current.phone ?? undefined,
          email: current.email ?? ''
        } as Api.Auth.UserInfo)
      }
      ElMessage.success('已保存')
      profileVisible.value = false
    } catch (error) {
      ElMessage.error(error instanceof Error ? error.message : '无法保存个人信息，请稍后重试。')
    } finally {
      saving.value = false
    }
  }

  const passwordVisible = ref(false)
  const passwordRef = ref<FormInstance>()
  const password = reactive({ oldPassword: '', newPassword: '', confirm: '' })
  const passwordRules: FormRules = {
    oldPassword: [{ required: true, message: '请输入当前密码', trigger: 'blur' }],
    newPassword: [
      { required: true, message: '请输入新密码', trigger: 'blur' },
      { min: 6, max: 20, message: '密码为 6–20 个字符', trigger: 'blur' }
    ],
    confirm: [
      {
        validator: (_rule, value: string, callback) =>
          callback(value === password.newPassword ? undefined : new Error('两次输入的密码不一致')),
        trigger: 'blur'
      }
    ]
  }

  const openPassword = (): void => {
    Object.assign(password, { oldPassword: '', newPassword: '', confirm: '' })
    passwordVisible.value = true
    closeUserMenu()
  }

  const submitPassword = async (): Promise<void> => {
    if (saving.value || !(await passwordRef.value?.validate().catch(() => false))) return
    saving.value = true
    try {
      await changePassword({ oldPassword: password.oldPassword, newPassword: password.newPassword })
      passwordVisible.value = false
      ElMessage.success('密码已修改，请重新登录')
      userStore.logOut()
    } catch (error) {
      ElMessage.error(error instanceof Error ? error.message : '无法修改密码，请稍后重试。')
    } finally {
      saving.value = false
      Object.assign(password, { oldPassword: '', newPassword: '', confirm: '' })
    }
  }

  /**
   * 用户登出确认
   */
  const loginOut = (): void => {
    closeUserMenu()
    setTimeout(() => {
      ElMessageBox.confirm(t('common.logOutTips'), t('common.tips'), {
        confirmButtonText: t('common.confirm'),
        cancelButtonText: t('common.cancel'),
        customClass: 'login-out-dialog'
      }).then(() => {
        userStore.signOut()
      })
    }, 200)
  }

  /**
   * 关闭用户菜单弹出层
   */
  const closeUserMenu = (): void => {
    setTimeout(() => {
      userMenuPopover.value.hide()
    }, 100)
  }
</script>

<style scoped>
  @reference '@styles/core/tailwind.css';

  @layer components {
    .btn-item {
      @apply flex items-center p-2 mb-3 select-none rounded-md cursor-pointer last:mb-0;

      span {
        @apply text-sm;
      }

      .art-svg-icon {
        @apply mr-2 text-base;
      }

      &:hover {
        background-color: var(--art-gray-200);
      }
    }
  }

  .log-out {
    @apply py-1.5
    mt-5
    text-xs
    text-center
    border
    border-g-400
    rounded-md
    transition-all
    duration-200
    hover:shadow-xl;
  }
</style>
