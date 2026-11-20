import type { Component } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import { checkSession, sessionState } from '../account/session'
import { sessionRedirect } from './authRoutes'
import { erpAccessLocation, workspacePages, type WorkspacePageId } from './workspace'
import InitializeView from '../views/InitializeView.vue'
import LoginView from '../views/LoginView.vue'
import BusinessConfigView from '../views/BusinessConfigView.vue'
import DashboardView from '../views/DashboardView.vue'
import NotFoundView from '../views/NotFoundView.vue'
import OrdersView from '../views/OrdersView.vue'
import OrderSnapshotReportView from '../views/OrderSnapshotReportView.vue'
import ShopsView from '../views/ShopsView.vue'
import UsersView from '../views/UsersView.vue'

const views: Record<WorkspacePageId, Component> = {
  dashboard: DashboardView,
  orders: OrdersView,
  'order-report': OrderSnapshotReportView,
  business: BusinessConfigView,
  users: UsersView,
}

// 视觉原型用于内部比对参考 ERP 工作台，仅管理员可达，不对外开放。
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: LoginView, meta: { title: '登录' } },
    {
      path: '/initialize',
      name: 'initialize',
      component: InitializeView,
      meta: { title: '初始化' },
    },
    {
      path: '/change-password',
      name: 'legacy-password-change',
      redirect: (to) => ({ name: 'login', query: to.query }),
    },
    ...workspacePages.map((page) => ({
      path: page.path,
      name: page.id,
      component: views[page.id],
      meta: { title: page.label, workspace: true, administratorOnly: page.administratorOnly },
    })),
    {
      path: '/shops',
      name: 'shops',
      component: ShopsView,
      meta: { title: '授权店铺', administratorOnly: true },
    },
    {
      path: '/visual-prototype',
      component: () => import('../views/prototype/PrototypeShell.vue'),
      meta: { administratorOnly: true },
      children: [
        {
          path: '',
          name: 'visual-prototype',
          component: () => import('../views/prototype/DashboardPrototypeView.vue'),
          meta: { title: '视觉原型' },
        },
        {
          path: 'products',
          name: 'visual-prototype-products',
          component: () => import('../views/prototype/ProductPrototypeView.vue'),
          meta: { title: '视觉原型 · 产品管理' },
        },
      ],
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: NotFoundView,
      meta: { title: '页面不存在' },
    },
  ],
})

router.beforeEach(async (to) => {
  if (sessionState.value === 'pending') await checkSession()
  const redirect = sessionRedirect(router, to, sessionState.value)
  if (redirect) return redirect
  // 业务配置收敛为 ERP 接入：/settings 统一落到该分区，旧链接也能打开正确内容。
  if (to.name === 'business' && to.query.section !== erpAccessLocation.query.section) {
    return {
      name: 'business',
      query: { ...to.query, section: erpAccessLocation.query.section },
      replace: true,
    }
  }
})

router.afterEach((to) => {
  document.title = typeof to.meta.title === 'string' ? `${to.meta.title} | OpenERP` : 'OpenERP'
})

export default router
