<!-- 首次初始化：设置管理员（admin）初始密码 -->
<template>
  <div class="flex w-full h-screen">
    <LoginLeftView />

    <div class="relative flex-1">
      <AuthTopBar />

      <div class="auth-right-wrap">
        <div class="form">
          <h3 class="title">初始化系统</h3>
          <p class="sub-title">首次使用，请为管理员账号 admin 设置密码</p>
          <ElForm
            ref="formRef"
            :model="formData"
            :rules="rules"
            style="margin-top: 25px"
            @keyup.enter="handleSubmit"
          >
            <ElFormItem prop="password">
              <ElInput
                v-model="formData.password"
                class="custom-height"
                type="password"
                autocomplete="new-password"
                show-password
                placeholder="新密码（6–20 个字符）"
              >
                <template #prefix>
                  <el-icon><Lock /></el-icon>
                </template>
              </ElInput>
            </ElFormItem>
            <ElFormItem prop="confirm">
              <ElInput
                v-model="formData.confirm"
                class="custom-height"
                type="password"
                autocomplete="new-password"
                show-password
                placeholder="再次输入新密码"
              >
                <template #prefix>
                  <el-icon><Lock /></el-icon>
                </template>
              </ElInput>
            </ElFormItem>

            <div style="margin-top: 30px">
              <ElButton
                class="w-full custom-height"
                type="primary"
                :loading="loading"
                v-ripple
                @click="handleSubmit"
              >
                完成初始化
              </ElButton>
            </div>
          </ElForm>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
  import { Lock } from '@element-plus/icons-vue'
  import type { FormInstance, FormRules } from 'element-plus'
  import { initialize } from '@/api/auth'

  defineOptions({ name: 'Initialize' })

  const router = useRouter()
  const formRef = ref<FormInstance>()
  const loading = ref(false)
  const formData = reactive({ password: '', confirm: '' })

  const rules: FormRules = {
    password: [
      { required: true, message: '请输入新密码', trigger: 'blur' },
      { min: 6, max: 20, message: '密码为 6–20 个字符', trigger: 'blur' }
    ],
    confirm: [
      { required: true, message: '请再次输入新密码', trigger: 'blur' },
      {
        validator: (_rule, value: string, callback) =>
          callback(value === formData.password ? undefined : new Error('两次输入的密码不一致')),
        trigger: 'blur'
      }
    ]
  }

  const handleSubmit = async () => {
    if (loading.value || !(await formRef.value?.validate().catch(() => false))) return
    loading.value = true
    try {
      await initialize(formData.password)
      ElMessage.success('初始化完成，请使用 admin 登录')
      router.replace({ name: 'Login' })
    } catch (error) {
      ElMessage.error(error instanceof Error ? error.message : '初始化失败，请稍后重试。')
    } finally {
      loading.value = false
    }
  }
</script>

<style scoped>
  @import '../login/style.css';
</style>
