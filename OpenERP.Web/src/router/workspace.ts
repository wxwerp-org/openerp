export type WorkspacePageId = 'dashboard' | 'newdev' | 'product' | 'orders' | 'business' | 'users'

export type WorkspacePage = {
  id: WorkspacePageId
  label: string
  path: string
}

export const workspacePages: WorkspacePage[] = [
  { id: 'dashboard', label: '首页', path: '/' },
  { id: 'newdev', label: '新品开发', path: '/newdev' },
  { id: 'product', label: '产品管理', path: '/products' },
  { id: 'orders', label: '订单列表', path: '/orders' },
  { id: 'business', label: '业务配置', path: '/settings' },
  { id: 'users', label: '用户管理', path: '/users' },
]

export type NavigationPage = {
  id: string
  label: string
  path: string
}

export const standalonePages: NavigationPage[] = [
  { id: 'service-connection', label: '服务连接', path: '/service-connection' },
  { id: 'shops', label: '授权店铺', path: '/shops' },
]

export const navigationPages: NavigationPage[] = [...workspacePages, ...standalonePages]
