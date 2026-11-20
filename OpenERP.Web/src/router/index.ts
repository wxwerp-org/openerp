import type { Component } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import { checkSession, sessionState } from '../account/session'
import { sessionRedirect } from './authRoutes'
import InitializeView from '../views/InitializeView.vue'
import LoginView from '../views/LoginView.vue'
import BusinessConfigView from '../views/BusinessConfigView.vue'
import DashboardView from '../views/DashboardView.vue'
import HomeView from '../views/HomeView.vue'
import NewDevView from '../views/NewDevView.vue'
import NotFoundView from '../views/NotFoundView.vue'
import OrdersView from '../views/OrdersView.vue'
import OrderSnapshotReportView from '../views/OrderSnapshotReportView.vue'
import ProductListView from '../views/ProductListView.vue'
import ShopsView from '../views/ShopsView.vue'
import UsersView from '../views/UsersView.vue'
import { workspacePages, type WorkspacePageId } from './workspace'

const views: Record<WorkspacePageId, Component> = {
  dashboard: DashboardView,
  newdev: NewDevView,
  product: ProductListView,
  orders: OrdersView,
  'order-report': OrderSnapshotReportView,
  business: BusinessConfigView,
  users: UsersView,
}

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
      path: '/service-connection',
      name: 'service-connection',
      component: HomeView,
      meta: { title: '服务连接' },
    },
    { path: '/shops', name: 'shops', component: ShopsView, meta: { title: '授权店铺' } },
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
  return sessionRedirect(router, to, sessionState.value)
})

router.afterEach((to) => {
  document.title = typeof to.meta.title === 'string' ? `${to.meta.title} | OpenERP` : 'OpenERP'
})

export default router
