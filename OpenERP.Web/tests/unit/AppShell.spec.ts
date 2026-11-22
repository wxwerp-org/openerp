import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import AppShell from '../../src/components/layout/AppShell.vue'
import { adminAccount } from './adminAccount'

const Page = { template: '<main class="test-page">页面</main>' }

function createTestRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: ['/', '/orders', '/reports/orders', '/settings', '/users', '/shops'].map((path) => ({
      path,
      component: Page,
      meta: { title: '测试页面' },
    })),
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

describe('应用公共框架', () => {
  let wrapper: VueWrapper | undefined

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
  })

  it('Given 直接访问用户管理 When 应用初始化 Then 默认只有工作台并追加用户管理页签', async () => {
    const mounted = await mountAt('/users')
    wrapper = mounted.wrapper

    expect(wrapper.findAll('[data-testid="page-tab"]').map((tab) => tab.text())).toEqual([
      '工作台',
      '用户管理',
    ])
    expect(wrapper.get('[data-testid="page-tab"] [aria-current="page"]').text()).toBe('用户管理')
    const sidebarText = wrapper.get('[data-testid="side-nav"]').text()
    for (const label of ['工作台', '订单管理', '店铺', '设置', '即将上线']) {
      expect(sidebarText).toContain(label)
    }
    for (const removed of ['新品开发', '产品管理', '服务连接', '业务配置', '销售', 'FBA']) {
      expect(sidebarText).not.toContain(removed)
    }
  })

  it('Given 普通用户 When 打开导航和账户菜单 Then 隐藏管理入口且保留业务入口', async () => {
    const account = { ...adminAccount, role: 'User' as const, username: 'employee' }
    const mounted = await mountAt('/', account)
    wrapper = mounted.wrapper
    await wrapper.get('[data-testid="account-button"]').trigger('click')

    expect(wrapper.find('[data-nav-page="orders"]').exists()).toBe(true)
    expect(wrapper.find('[data-nav-page="order-report"]').exists()).toBe(true)
    expect(wrapper.find('[data-nav-page="shops"]').exists()).toBe(true)
    expect(wrapper.find('[data-nav-page="users"]').exists()).toBe(false)
    expect(wrapper.find('[data-nav-page="business"]').exists()).toBe(false)
    expect(wrapper.find('[data-account-action="profile"]').exists()).toBe(true)
    expect(wrapper.find('[data-account-action="password"]').exists()).toBe(true)
    expect(wrapper.find('[data-account-action="settings"]').exists()).toBe(false)
    expect(wrapper.find('button[aria-label="通知"]').exists()).toBe(false)
  })

  it('Given 管理员账户菜单 When 点击账户按钮 Then 菜单展示关于与版本行', async () => {
    const mounted = await mountAt('/')
    wrapper = mounted.wrapper

    await wrapper.get('[data-testid="account-button"]').trigger('click')

    expect(wrapper.get('[data-testid="account-button"]').attributes('aria-expanded')).toBe('true')
    expect(wrapper.get('#accountMenu').isVisible()).toBe(true)
    expect(wrapper.get('#accountMenu').text()).toContain('修改个人信息')
    expect(wrapper.get('#accountMenu').text()).toContain('修改密码')
    expect(wrapper.get('#accountMenu').text()).toContain('关于')
    expect(wrapper.get('[data-testid="account-version"]').text()).toBe('dev')
  })

  it('Given 完整侧边导航 When 点击收起按钮 Then 切换折叠轨道', async () => {
    const mounted = await mountAt('/')
    wrapper = mounted.wrapper

    await wrapper.get('[data-testid="collapse-nav"]').trigger('click')

    expect(wrapper.find('[data-testid="sidebar"][data-rail="true"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="collapse-nav"]').attributes('aria-label')).toBe('展开导航')
  })

  it('Given 即将上线分组 When 默认渲染然后展开 Then 占位项灰色且不可导航', async () => {
    const mounted = await mountAt('/')
    wrapper = mounted.wrapper

    const group = wrapper.get('[data-group="upcoming"]')
    expect(group.text()).toContain('即将上线')
    expect(group.find('[data-nav-page="商品管理"]').exists()).toBe(false)

    await group.get('button[aria-expanded]').trigger('click')
    const items = group
      .findAll('button')
      .filter((button) => button.attributes('disabled') !== undefined)
    expect(items.map((item) => item.text())).toEqual(['商品管理', '物流', '仓库'])
  })

  it('Given 用户页签是当前页 When 关闭用户页签 Then 返回工作台', async () => {
    const mounted = await mountAt('/users')
    wrapper = mounted.wrapper

    await wrapper.get('[data-close-tab="users"]').trigger('click')
    await flushPromises()

    expect(mounted.router.currentRoute.value.path).toBe('/')
    expect(wrapper.get('[data-testid="page-tab"] [aria-current="page"]').text()).toBe('工作台')
  })

  it('Given 当前位于工作台 When 通过侧栏进入订单列表 Then 路由、页签与高亮同步', async () => {
    const mounted = await mountAt('/')
    wrapper = mounted.wrapper

    await wrapper.findAll('[data-nav-page="orders"]').at(0)!.trigger('click')
    await flushPromises()

    expect(mounted.router.currentRoute.value.path).toBe('/orders')
    expect(wrapper.get('[data-testid="page-tab"] [aria-current="page"]').text()).toBe('订单列表')
    expect(wrapper.findAll('[data-testid="page-tab"]').map((tab) => tab.text())).toEqual([
      '工作台',
      '订单列表',
    ])
  })

  it('Given 非工作区路由 When 打开授权店铺 Then 不误高亮工作台', async () => {
    const mounted = await mountAt('/shops')
    wrapper = mounted.wrapper

    expect(wrapper.find('[data-nav-page="dashboard"][aria-current="page"]').exists()).toBe(false)
    expect(wrapper.get('[data-nav-page="shops"]').attributes('aria-current')).toBe('page')
  })

  it('Given 键盘用户 When 进入页面 Then 可通过跳转链接聚焦主内容', async () => {
    const mounted = await mountAt('/')
    wrapper = mounted.wrapper

    expect(wrapper.get('a[href="#main"]').attributes('href')).toBe('#main')
    expect(wrapper.get('#main').attributes('tabindex')).toBe('-1')
  })
})
