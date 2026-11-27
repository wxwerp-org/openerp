import { AppRouteRecord } from '@/types/router'

/** 设置：用户管理、API 权限绑定（ERP 接入），仅管理员可见 */
export const systemRoutes: AppRouteRecord = {
  path: '/system',
  name: 'System',
  component: '/index/index',
  meta: {
    title: 'menus.system.title',
    icon: 'ri:settings-3-line',
    roles: ['R_SUPER']
  },
  children: [
    {
      path: 'user',
      name: 'SystemUser',
      component: '/system/user',
      meta: {
        title: 'menus.system.user',
        icon: 'ri:user-line',
        keepAlive: true,
        roles: ['R_SUPER']
      }
    },
    {
      path: 'erp-binding',
      name: 'SystemErpBinding',
      component: '/system/erp-binding',
      meta: {
        title: 'menus.system.erpBinding',
        icon: 'ri:plug-line',
        keepAlive: true,
        roles: ['R_SUPER']
      }
    }
  ]
}
