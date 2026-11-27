import { AppRouteRecord } from '@/types/router'

export const dashboardRoutes: AppRouteRecord = {
  name: 'Dashboard',
  path: '/dashboard',
  component: '/index/index',
  meta: {
    title: 'menus.dashboard.title',
    icon: 'ri:pie-chart-line',
    roles: ['R_USER', 'R_ADMIN', 'R_SUPER']
  },
  children: [
    {
      path: 'analysis',
      name: 'Analysis',
      component: '/dashboard/analysis',
      meta: {
        title: 'menus.dashboard.analysis',
        icon: 'ri:align-item-bottom-line',
        keepAlive: false
      }
    },
    {
      path: 'ecommerce',
      name: 'Ecommerce',
      component: '/dashboard/ecommerce',
      meta: {
        title: 'menus.dashboard.ecommerce',
        icon: 'ri:bar-chart-box-line',
        keepAlive: false
      }
    }
  ]
}
