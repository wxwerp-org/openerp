import type { Component } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
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

router.afterEach((to) => {
  document.title = typeof to.meta.title === 'string' ? `${to.meta.title} | OpenERP` : 'OpenERP'
})

export default router
