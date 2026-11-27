import type { Directive, DirectiveBinding } from 'vue'
import { useUserStore } from '@/store/modules/user'

/**
 * 权限指令 v-permission
 * 用法: v-permission="'search'" 或 v-permission="{ level: 1, permission: 'search' }"
 */

interface PermissionBinding {
  level?: number
  permission?: string
}

const checkPermission = (binding: DirectiveBinding<PermissionBinding | string>): boolean => {
  const userStore = useUserStore()
  const userInfo = userStore.info

  if (!userInfo) return false

  // 获取用户VIP等级
  const userLevel = getVipLevel(userInfo.roles || [])

  // 解析指令参数
  let requiredLevel = 0
  let requiredPermission = ''

  if (typeof binding.value === 'string') {
    requiredPermission = binding.value
  } else if (typeof binding.value === 'object') {
    requiredLevel = binding.value.level || 0
    requiredPermission = binding.value.permission || ''
  }

  // 检查等级
  if (userLevel < requiredLevel) {
    return false
  }

  // 检查特定权限
  if (requiredPermission) {
    return checkSpecificPermission(userInfo, requiredPermission)
  }

  return true
}

/**
 * 获取用户VIP等级
 */
const getVipLevel = (roles: string[]): number => {
  if (roles.includes('R_VIP3')) return 3
  if (roles.includes('R_VIP2')) return 2
  if (roles.includes('R_VIP1')) return 1
  return 0
}

/**
 * 检查特定权限
 */
const checkSpecificPermission = (userInfo: any, permission: string): boolean => {
  // 从用户信息中获取权限列表
  const permissions = userInfo.permissions || []
  return permissions.includes(permission)
}

/**
 * 权限指令 - 无权限时隐藏元素
 */
export const permission: Directive = {
  mounted(el: HTMLElement, binding: DirectiveBinding) {
    if (!checkPermission(binding)) {
      el.style.display = 'none'
    }
  },
  updated(el: HTMLElement, binding: DirectiveBinding) {
    if (checkPermission(binding)) {
      el.style.display = ''
    } else {
      el.style.display = 'none'
    }
  }
}

/**
 * 权限指令 - 无权限时禁用元素
 */
export const permissionDisabled: Directive = {
  mounted(el: HTMLElement, binding: DirectiveBinding) {
    if (!checkPermission(binding)) {
      el.setAttribute('disabled', 'true')
      el.classList.add('is-disabled')
      el.style.cursor = 'not-allowed'
      el.style.opacity = '0.6'
    }
  },
  updated(el: HTMLElement, binding: DirectiveBinding) {
    if (checkPermission(binding)) {
      el.removeAttribute('disabled')
      el.classList.remove('is-disabled')
      el.style.cursor = ''
      el.style.opacity = ''
    } else {
      el.setAttribute('disabled', 'true')
      el.classList.add('is-disabled')
      el.style.cursor = 'not-allowed'
      el.style.opacity = '0.6'
    }
  }
}

/**
 * 会员等级指令 v-vip
 * 用法: v-vip="1" 表示需要VIP1及以上
 */
export const vip: Directive = {
  mounted(el: HTMLElement, binding: DirectiveBinding<number>) {
    const userStore = useUserStore()
    const userInfo = userStore.info

    if (!userInfo) {
      el.style.display = 'none'
      return
    }

    const userLevel = getVipLevel(userInfo.roles || [])
    const requiredLevel = binding.value || 0

    if (userLevel < requiredLevel) {
      el.style.display = 'none'
    }
  },
  updated(el: HTMLElement, binding: DirectiveBinding<number>) {
    const userStore = useUserStore()
    const userInfo = userStore.info

    if (!userInfo) {
      el.style.display = 'none'
      return
    }

    const userLevel = getVipLevel(userInfo.roles || [])
    const requiredLevel = binding.value || 0

    if (userLevel >= requiredLevel) {
      el.style.display = ''
    } else {
      el.style.display = 'none'
    }
  }
}

/**
 * 注册所有权限指令
 */
export const setupPermissionDirectives = (app: any) => {
  app.directive('permission', permission)
  app.directive('permission-disabled', permissionDisabled)
  app.directive('vip', vip)
}
