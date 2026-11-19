import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import AppShell from '../../src/components/layout/AppShell.vue'
import AccountMenu from '../../src/components/navigation/AccountMenu.vue'
import AppSidebar from '../../src/components/navigation/AppSidebar.vue'
import WorkspaceTabs from '../../src/components/navigation/WorkspaceTabs.vue'
import { adminAccount } from './adminAccount'

const Page = { template: '<main>页面</main>' }

describe('导航与临时状态质量', () => {
  it('Given 折叠侧栏 When 分组获得键盘焦点 Then 显示子菜单且 Escape 可关闭', async () => {
    const wrapper = mount(AppSidebar, {
      props: { activePage: 'dashboard', collapsed: true },
      attachTo: document.body,
    })
    const sales = wrapper.get('[data-group="sales"] button[aria-expanded]')

    await sales.trigger('focusin')
    expect(wrapper.find('[data-group="sales"] [data-testid="nav-flyout"]').exists()).toBe(true)
    await sales.trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('[data-group="sales"] [data-testid="nav-flyout"]').exists()).toBe(false)
  })

  it('Given 报表窄屏自动轨道 When 点击分组 Then 浮层展开且不改写手动折叠状态', async () => {
    const wrapper = mount(AppSidebar, {
      props: { activePage: 'order-report', collapsed: false, rail: true },
      attachTo: document.body,
    })
    const sales = wrapper.get('[data-group="sales"] button[aria-expanded]')

    expect(wrapper.get('[data-testid="sidebar"]').attributes('data-rail')).toBe('true')
    await sales.trigger('click')
    expect(wrapper.find('[data-group="sales"] [data-testid="nav-flyout"]').exists()).toBe(true)
    expect(sales.attributes('aria-expanded')).toBe('true')
    expect(wrapper.emitted('update:collapsed')).toBeUndefined()

    // 手动折叠与视口轨道同时存在时，点击仍打开浮层而不是做无效的展开。
    const both = mount(AppSidebar, {
      props: { activePage: 'order-report', collapsed: true, rail: true },
      attachTo: document.body,
    })
    await both.get('[data-group="sales"] button[aria-expanded]').trigger('click')
    expect(both.find('[data-group="sales"] [data-testid="nav-flyout"]').exists()).toBe(true)
    expect(both.emitted('update:collapsed')).toBeUndefined()
  })

  it('Given 账户按钮 When 按方向下键和 Escape Then 打开菜单并聚焦首项后关闭归还焦点', async () => {
    const wrapper = mount(AccountMenu, {
      props: { account: adminAccount },
      attachTo: document.body,
    })
    const button = wrapper.get('[data-testid="account-button"]')

    await button.trigger('keydown', { key: 'ArrowDown' })
    expect(wrapper.get('[data-testid="account-button"]').attributes('aria-expanded')).toBe('true')
    expect(document.activeElement?.textContent).toContain('修改个人信息')

    await wrapper.get('#accountMenu').trigger('keydown', { key: 'Escape' })
    expect(wrapper.get('[data-testid="account-button"]').attributes('aria-expanded')).toBe('false')
    expect(document.activeElement).toBe(wrapper.get('[data-testid="account-button"]').element)
  })

  it('Given 工作区页签 When 点击路由链接 Then 不重复发出 select 且打开页面菜单含新品开发', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: Page },
        { path: '/newdev', component: Page },
        { path: '/products', component: Page },
        { path: '/orders', component: Page },
        { path: '/settings', component: Page },
        { path: '/users', component: Page },
      ],
    })
    await router.push('/')
    await router.isReady()
    const wrapper = mount(WorkspaceTabs, {
      props: {
        items: [
          { id: 'dashboard', label: '首页', path: '/' },
          { id: 'newdev', label: '新品开发', path: '/newdev' },
        ],
        activePage: 'dashboard',
      },
      global: { plugins: [router] },
    })

    await wrapper.get('[data-testid="tab-add"]').trigger('click')
    expect(wrapper.get('#tabMenu').text()).toContain('新品开发')
    await wrapper.get('[data-testid="page-tab"] a').trigger('click')
    expect(wrapper.emitted('select')).toBeUndefined()
  })

  it('Given 非工作区路由 When 打开独立页面 Then 不误高亮工作台', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: Page, meta: { title: '首页' } },
        { path: '/shops', component: Page, meta: { title: '授权店铺' } },
        { path: '/newdev', component: Page, meta: { title: '新品开发' } },
        { path: '/products', component: Page, meta: { title: '产品管理' } },
        { path: '/orders', component: Page, meta: { title: '订单列表' } },
        { path: '/settings', component: Page, meta: { title: '业务配置' } },
        { path: '/users', component: Page, meta: { title: '用户管理' } },
      ],
    })
    await router.push('/shops')
    await router.isReady()
    const wrapper = mount(AppShell, {
      props: { account: adminAccount },
      global: { plugins: [router] },
    })
    await flushPromises()

    expect(wrapper.find('[data-nav-page="dashboard"][aria-current="page"]').exists()).toBe(false)
  })

  it('Given 重复导航 When 键盘用户进入页面 Then 可通过跳转链接聚焦主内容', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: ['/', '/newdev', '/products', '/orders', '/settings', '/users'].map((path) => ({
        path,
        component: Page,
        meta: { title: '测试页面' },
      })),
    })
    await router.push('/')
    await router.isReady()
    const wrapper = mount(AppShell, {
      props: { account: adminAccount },
      global: { plugins: [router] },
    })
    await flushPromises()

    expect(wrapper.get('a[href="#main"]').attributes('href')).toBe('#main')
    expect(wrapper.get('#main').attributes('tabindex')).toBe('-1')
  })
})
