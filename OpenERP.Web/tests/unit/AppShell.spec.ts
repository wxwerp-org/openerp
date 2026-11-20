import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import AppShell from '../../src/components/layout/AppShell.vue'
import { adminAccount } from './adminAccount'

const Page = { template: '<main class="test-page">原型页面</main>' }

function createTestRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: Page, meta: { title: '首页' } },
      { path: '/newdev', component: Page, meta: { title: '新品开发' } },
      { path: '/products', component: Page, meta: { title: '产品管理' } },
      { path: '/orders', component: Page, meta: { title: '订单列表' } },
      { path: '/reports/orders', component: Page, meta: { title: '商品量报表' } },
      { path: '/settings', component: Page, meta: { title: '业务配置' } },
      { path: '/users', component: Page, meta: { title: '用户管理' } },
    ],
  })
}

async function mountAt(
  path: string,
  account = adminAccount,
): Promise<{ wrapper: VueWrapper; router: Router }> {
  const router = createTestRouter()
  await router.push(path)
  await router.isReady()
  const wrapper = mount(AppShell, {
    props: { account },
    global: { plugins: [router] },
  })
  await flushPromises()
  return { wrapper, router }
}

describe('原型应用公共框架', () => {
  let wrapper: VueWrapper | undefined

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
  })

  it('Given 直接访问用户管理 When 应用初始化 Then 保留原型默认页签并打开用户页签和完整导航', async () => {
    const mounted = await mountAt('/users')
    wrapper = mounted.wrapper

    expect(wrapper.findAll('[data-testid="page-tab"]').map((tab) => tab.text())).toEqual([
      '首页',
      '新品开发',
      '产品管理',
      '用户管理',
    ])
    expect(wrapper.get('[data-testid="page-tab"] [aria-current="page"]').text()).toBe('用户管理')
    for (const label of [
      '产品',
      '销售',
      'FBA',
      '采购',
      '仓库',
      '物流',
      '广告',
      '客服',
      '财务',
      '统计',
      '工具',
      '设置',
    ]) {
      expect(wrapper.get('[data-testid="side-nav"]').text()).toContain(label)
    }
    expect(wrapper.get('[data-testid="account-button"]').text()).toContain(adminAccount.name)
    expect(wrapper.find('button[aria-label="通知"]').exists()).toBe(true)
  })

  it('Given 普通用户 When 打开导航和账户菜单 Then 保留日常业务与本人资料入口并隐藏管理入口', async () => {
    const account = { ...adminAccount, role: 'User' as const }
    const mounted = await mountAt('/', account)
    wrapper = mounted.wrapper
    await wrapper.get('[data-testid="account-button"]').trigger('click')

    expect(wrapper.find('[data-nav-page="orders"]').exists()).toBe(true)
    expect(wrapper.find('[data-nav-page="shops"]').exists()).toBe(true)
    expect(wrapper.find('[data-account-action="profile"]').exists()).toBe(true)
    expect(wrapper.find('[data-account-action="password"]').exists()).toBe(true)
    expect(wrapper.find('[data-nav-page="users"]').exists()).toBe(false)
    expect(wrapper.find('[data-nav-page="business"]').exists()).toBe(false)
    expect(wrapper.find('[data-account-action="settings"]').exists()).toBe(false)
  })

  it('Given 顶部管理员入口 When 点击账户按钮 Then 展示账户菜单', async () => {
    const mounted = await mountAt('/')
    wrapper = mounted.wrapper

    await wrapper.get('[data-testid="account-button"]').trigger('click')

    expect(wrapper.get('[data-testid="account-button"]').attributes('aria-expanded')).toBe('true')
    expect(wrapper.get('#accountMenu').isVisible()).toBe(true)
    expect(wrapper.get('#accountMenu').text()).toContain('修改个人信息')
    expect(wrapper.get('#accountMenu').text()).toContain('修改密码')
  })

  it('Given 完整侧边导航 When 点击收起按钮 Then 切换原型折叠轨道', async () => {
    const mounted = await mountAt('/')
    wrapper = mounted.wrapper

    await wrapper.get('[data-testid="collapse-nav"]').trigger('click')

    expect(wrapper.find('[data-testid="sidebar"][data-rail="true"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="collapse-nav"]').attributes('aria-label')).toBe('展开导航')
  })

  it('Given 用户页签是当前页 When 关闭用户页签 Then 返回前一个产品管理页签', async () => {
    const mounted = await mountAt('/users')
    wrapper = mounted.wrapper

    await wrapper.get('[data-close-tab="users"]').trigger('click')
    await flushPromises()

    expect(mounted.router.currentRoute.value.path).toBe('/products')
    expect(wrapper.get('[data-testid="page-tab"] [aria-current="page"]').text()).toBe('产品管理')
  })

  it('Given 当前位于首页 When 通过设置分组进入用户管理 Then 路由和当前页签同步更新', async () => {
    const mounted = await mountAt('/')
    wrapper = mounted.wrapper

    const usersLink = wrapper.findAll('[data-nav-page="users"]').at(0)
    expect(usersLink).toBeDefined()
    await usersLink!.trigger('click')
    await flushPromises()

    expect(mounted.router.currentRoute.value.path).toBe('/users')
    expect(wrapper.get('[data-testid="page-tab"] [aria-current="page"]').text()).toBe('用户管理')
  })
})
