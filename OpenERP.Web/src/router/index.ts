import type { Component } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import { account, checkSession, expireSession, sessionState } from '../account/session'
import { isAuthRoute, loginRoute, passwordChangeRoute, returnDestination } from './authRoutes'
import RequiredPasswordChangeView from '../views/RequiredPasswordChangeView.vue'
import LoginView from '../views/LoginView.vue'
import BusinessConfigView from '../views/BusinessConfigView.vue'
import DashboardView from '../views/DashboardView.vue'
import HomeView from '../views/HomeView.vue'
import NewDevView from '../views/NewDevView.vue'
import OrdersView from '../views/OrdersView.vue'
import ProductListView from '../views/ProductListView.vue'
import ShopsView from '../views/ShopsView.vue'
import UsersView from '../views/UsersView.vue'
import { workspacePages, type WorkspacePageId } from './workspace'

const views: Record<WorkspacePageId, Component> = {
  dashboard: DashboardView,
  newdev: NewDevView,
  product: ProductListView,
  orders: OrdersView,
  business: BusinessConfigView,
  users: UsersView,
}

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: LoginView, meta: { title: '登录' } },
    { path: '/change-password', name: 'required-password-change', component: RequiredPasswordChangeView, meta: { title: '修改密码' } },
    ...workspacePages.map((page) => ({
      path: page.path,
      name: page.id,
      component: views[page.id],
      meta: { title: page.label, workspace: true },
    })),
    { path: '/service-connection', name: 'service-connection', component: HomeView, meta: { title: '服务连接' } },
    { path: '/shops', name: 'shops', component: ShopsView, meta: { title: '授权店铺' } },
  ],
})

router.beforeEach(async (to) => {
  if (sessionState.value === 'pending') await checkSession()
  expireSession()
  const destination = isAuthRoute(to.name) ? returnDestination(router, to.query.returnTo) : to.fullPath
  if (account.value?.mustChangePassword && to.name !== 'required-password-change') {
    return passwordChangeRoute(destination)
  }
  if (account.value && !account.value.mustChangePassword && isAuthRoute(to.name)) return destination
  if (sessionState.value === 'anonymous' && to.name !== 'login') return loginRoute(destination)
})

router.afterEach((to) => {
  document.title = typeof to.meta.title === 'string' ? `${to.meta.title} | OpenERP` : 'OpenERP'
})

export default router
