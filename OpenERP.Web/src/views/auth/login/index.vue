<!-- 登录页面 -->
<template>
  <div class="flex w-full h-screen">
    <LoginLeftView />

    <div class="relative flex-1">
      <AuthTopBar />

      <div class="auth-right-wrap">
        <div class="form">
          <h3 class="title">{{ $t('login.title') }}</h3>
          <p class="sub-title">{{ $t('login.subTitle') }}</p>
          <ElForm
            ref="formRef"
            :model="formData"
            :rules="rules"
            :key="formKey"
            @keyup.enter="handleSubmit"
            style="margin-top: 25px"
          >
            <ElFormItem prop="account">
              <ElInput
                class="custom-height"
                :placeholder="$t('login.placeholder.account')"
                v-model.trim="formData.account"
              >
                <template #prefix>
                  <el-icon><User /></el-icon>
                </template>
              </ElInput>
            </ElFormItem>
            <ElFormItem prop="password">
              <ElInput
                class="custom-height"
                :placeholder="$t('login.placeholder.password')"
                v-model.trim="formData.password"
                type="password"
                autocomplete="off"
                show-password
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
                @click="handleSubmit"
                :loading="loading"
                v-ripple
              >
                {{ $t('login.btnText') }}
              </ElButton>
            </div>
          </ElForm>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
  import AppConfig from '@/config'
  import { useI18n } from 'vue-i18n'
  import { login } from '@/api/auth'
  import { ElNotification, type FormInstance, type FormRules } from 'element-plus'
  import { User, Lock } from '@element-plus/icons-vue'

  defineOptions({ name: 'Login' })

  const { t, locale } = useI18n()
  const formKey = ref(0)

  // 监听语言切换，重置表单
  watch(locale, () => {
    formKey.value++
  })

  const router = useRouter()
  const route = useRoute()

  const systemName = AppConfig.systemInfo.name
  const formRef = ref<FormInstance>()

  const formData = reactive({
    account: '',
    password: ''
  })

  const rules = computed<FormRules>(() => ({
    account: [{ required: true, message: t('login.placeholder.account'), trigger: 'blur' }],
    password: [{ required: true, message: t('login.placeholder.password'), trigger: 'blur' }]
  }))

  const loading = ref(false)

  // 登录：令牌由会话模块保管，路由守卫随后同步账号并注册菜单
  const handleSubmit = async () => {
    if (!formRef.value || loading.value) return

    try {
      if (!(await formRef.value.validate().catch(() => false))) {
        ElMessage.error('请输入账号和密码')
        return
      }

      loading.value = true
      await login(formData.account, formData.password)
      showLoginSuccessNotice()

      // 回到登录前访问的页面，缺省进入首页
      const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
      await router.push(redirect.startsWith('/') ? redirect : '/')
    } catch (error) {
      ElMessage.error(error instanceof Error ? error.message : '登录失败')
    } finally {
      loading.value = false
    }
  }

  // 登录成功提示
  const showLoginSuccessNotice = () => {
    setTimeout(() => {
      ElNotification({
        title: t('login.success.title'),
        type: 'success',
        duration: 2500,
        zIndex: 10000,
        message: `${t('login.success.message')}, ${systemName}!`
      })
    }, 1000)
  }

  // 如果存在由于 Token 失效带来的错误消息，在登录页统一提示
  onMounted(() => {
    const msg = localStorage.getItem('login-error-message')
    if (msg) {
      ElMessage.error(msg)
      localStorage.removeItem('login-error-message')
    }
  })
</script>

<style scoped>
  @import './style.css';
</style>

<style lang="scss" scoped>
  :deep(.el-select__wrapper) {
    height: 40px !important;
  }
</style>
