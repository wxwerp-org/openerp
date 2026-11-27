<template>
  <div v-if="hasPermission" class="vip-only-wrapper">
    <slot />
  </div>
  <div v-else-if="showFallback" class="vip-only-fallback">
    <slot name="fallback">
      <ElResult icon="warning" :title="fallbackTitle" :sub-title="fallbackSubTitle">
        <template #extra>
          <ElButton type="primary" @click="handleUpgrade">
            {{ upgradeButtonText }}
          </ElButton>
          <ElButton v-if="showBackButton" @click="handleBack">
            {{ backButtonText }}
          </ElButton>
        </template>
      </ElResult>
    </slot>
  </div>
</template>

<script setup lang="ts">
  import { computed } from 'vue'
  import { useRouter } from 'vue-router'
  import { useUserStore } from '@/store/modules/user'

  interface Props {
    /** 最低VIP等级要求 */
    minLevel?: number
    /** 特定权限代码 */
    permission?: string
    /** 是否显示降级提示 */
    showFallback?: boolean
    /** 降级提示标题 */
    fallbackTitle?: string
    /** 降级提示副标题 */
    fallbackSubTitle?: string
    /** 升级按钮文字 */
    upgradeButtonText?: string
    /** 是否显示返回按钮 */
    showBackButton?: boolean
    /** 返回按钮文字 */
    backButtonText?: string
  }

  const props = withDefaults(defineProps<Props>(), {
    minLevel: 0,
    permission: '',
    showFallback: true,
    fallbackTitle: '功能受限',
    fallbackSubTitle: '该功能需要更高等级的会员才能使用',
    upgradeButtonText: '升级会员',
    showBackButton: true,
    backButtonText: '返回'
  })

  const router = useRouter()
  const userStore = useUserStore()

  /** 检查是否有权限 */
  const hasPermission = computed(() => {
    const userInfo = userStore.info
    if (!userInfo) return false

    const userLevel = getVipLevel(userInfo.roles || [])

    // 检查等级
    if (userLevel < props.minLevel) {
      return false
    }

    // 检查特定权限
    if (props.permission) {
      const permissions = userInfo.permissions || []
      if (!permissions.includes(props.permission)) {
        return false
      }
    }

    return true
  })

  /** 获取VIP等级 */
  const getVipLevel = (roles: string[]): number => {
    if (roles.includes('R_VIP3')) return 3
    if (roles.includes('R_VIP2')) return 2
    if (roles.includes('R_VIP1')) return 1
    return 0
  }

  /** 处理升级 */
  const handleUpgrade = () => {
    router.push('/membership/upgrade')
  }

  /** 处理返回 */
  const handleBack = () => {
    router.back()
  }
</script>

<style scoped lang="scss">
  .vip-only-wrapper {
    width: 100%;
  }

  .vip-only-fallback {
    padding: 40px 20px;
    background: var(--art-bg-color);
    border-radius: 8px;
  }
</style>
