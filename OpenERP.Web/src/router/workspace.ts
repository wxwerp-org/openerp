export type WorkspacePageId = 'dashboard' | 'orders' | 'order-report' | 'business' | 'users'

export type WorkspacePage = {
  id: WorkspacePageId
  label: string
  path: string
  administratorOnly?: boolean
}

export const erpAccessLocation = { name: 'business', query: { section: 'erp' } } as const

/** 可出现在顶部页签里的页面；顺序即「+」菜单顺序。 */
export const workspacePages: WorkspacePage[] = [
  { id: 'dashboard', label: '工作台', path: '/' },
  { id: 'orders', label: '订单列表', path: '/orders' },
  { id: 'order-report', label: '商品量报表', path: '/reports/orders' },
  { id: 'business', label: 'ERP 接入', path: '/settings', administratorOnly: true },
  { id: 'users', label: '用户管理', path: '/users', administratorOnly: true },
]

export type NavigationPage = {
  id: string
  label: string
  path: string
  administratorOnly?: boolean
}

/** 侧栏独立项：只出现在导航里，不开页签。 */
export const standalonePages: NavigationPage[] = [
  { id: 'shops', label: '授权店铺', path: '/shops' },
]

export const navigationPages: NavigationPage[] = [...workspacePages, ...standalonePages]

export type NavigationItem = {
  label: string
  /** 真实页面 id；无 page 的条目为即将上线占位，不可导航。 */
  page?: string
}

export type NavigationGroup = {
  id: string
  label: string
  icon: string
  items: NavigationItem[]
}

/** 侧栏分组定义；角色过滤统一读取 navigationPages 的 administratorOnly。 */
export const navigationGroups: NavigationGroup[] = [
  {
    id: 'orders',
    label: '订单管理',
    icon: 'i-nav-sales',
    items: [
      { label: '订单列表', page: 'orders' },
      { label: '商品量报表', page: 'order-report' },
    ],
  },
  {
    id: 'shops',
    label: '店铺',
    icon: 'i-nav-fba',
    items: [{ label: '授权店铺', page: 'shops' }],
  },
  {
    id: 'settings',
    label: '设置',
    icon: 'i-nav-settings',
    items: [
      { label: '用户管理', page: 'users' },
      { label: 'ERP 接入', page: 'business' },
    ],
  },
  {
    id: 'upcoming',
    label: '即将上线',
    icon: 'i-nav-tools',
    items: [{ label: '商品管理' }, { label: '物流' }, { label: '仓库' }],
  },
]

export function isAdministratorOnlyPage(pageId: string | undefined): boolean {
  if (!pageId) return false
  return navigationPages.some((page) => page.id === pageId && page.administratorOnly)
}
