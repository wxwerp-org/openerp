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
    const sales = wrapper.get('[data-group="sales"] .nav-group-head')

    await sales.trigger('focusin')
    expect(wrapper.find('[data-group="sales"] .nav-flyout').exists()).toBe(true)
    await sales.trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('[data-group="sales"] .nav-flyout').exists()).toBe(false)
  })

  it('Given 账户按钮 When 按方向下键和 Escape Then 打开菜单并聚焦首项后关闭归还焦点', async () => {
    const wrapper = mount(AccountMenu, { props: { account: adminAccount }, attachTo: document.body })
    const button = wrapper.get('.account-button')

    await button.trigger('keydown', { key: 'ArrowDown' })
    expect(wrapper.get('.account-button').attributes('aria-expanded')).toBe('true')
    expect(document.activeElement?.textContent).toContain('修改个人信息')

    await wrapper.get('.account-menu').trigger('keydown', { key: 'Escape' })
    expect(wrapper.get('.account-button').attributes('aria-expanded')).toBe('false')
    expect(document.activeElement?.id).toBe('accountBtn')
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

    await wrapper.get('#tabAdd').trigger('click')
    expect(wrapper.get('#tabMenu').text()).toContain('新品开发')
    await wrapper.get('.page-tab .tab-label').trigger('click')
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
    const wrapper = mount(AppShell, { props: { account: adminAccount }, global: { plugins: [router] } })
    await flushPromises()

    expect(wrapper.find('.nav-link.active').exists()).toBe(false)
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
    const wrapper = mount(AppShell, { props: { account: adminAccount }, global: { plugins: [router] } })
    await flushPromises()

    expect(wrapper.get('.skip-link').attributes('href')).toBe('#main')
    expect(wrapper.get('#main').attributes('tabindex')).toBe('-1')
  })
})
