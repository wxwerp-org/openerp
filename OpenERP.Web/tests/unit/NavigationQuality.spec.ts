import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import AppShell from '../../src/components/layout/AppShell.vue'
import AccountMenu from '../../src/components/navigation/AccountMenu.vue'
import AppSidebar from '../../src/components/navigation/AppSidebar.vue'
import WorkspaceTabs from '../../src/components/navigation/WorkspaceTabs.vue'
import { workspacePages } from '../../src/router/workspace'
import { adminAccount } from './adminAccount'

const Page = { template: '<main>页面</main>' }

describe('导航与临时状态质量', () => {
  it('Given 折叠侧栏 When 分组获得键盘焦点 Then 显示子菜单且 Escape 可关闭', async () => {
    const wrapper = mount(AppSidebar, {
      props: { activePage: 'dashboard', collapsed: true, administrator: true },
      attachTo: document.body,
    })
    const orders = wrapper.get('[data-group="orders"] button[aria-expanded]')

    await orders.trigger('focusin')
    expect(wrapper.find('[data-group="orders"] [data-testid="nav-flyout"]').exists()).toBe(true)
    await orders.trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('[data-group="orders"] [data-testid="nav-flyout"]').exists()).toBe(false)
  })

  it('Given 报表窄屏自动轨道 When 点击分组 Then 浮层展开且不改写手动折叠状态', async () => {
    const wrapper = mount(AppSidebar, {
      props: { activePage: 'order-report', collapsed: false, rail: true, administrator: true },
      attachTo: document.body,
    })
    const orders = wrapper.get('[data-group="orders"] button[aria-expanded]')

    expect(wrapper.get('[data-testid="sidebar"]').attributes('data-rail')).toBe('true')
    await orders.trigger('click')
    expect(wrapper.find('[data-group="orders"] [data-testid="nav-flyout"]').exists()).toBe(true)
    expect(orders.attributes('aria-expanded')).toBe('true')
    expect(wrapper.emitted('update:collapsed')).toBeUndefined()

    const both = mount(AppSidebar, {
      props: { activePage: 'order-report', collapsed: true, rail: true, administrator: true },
      attachTo: document.body,
    })
    await both.get('[data-group="orders"] button[aria-expanded]').trigger('click')
    expect(both.find('[data-group="orders"] [data-testid="nav-flyout"]').exists()).toBe(true)
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

  it('Given 工作区页签 When 管理员打开页面菜单 Then 列出可打开页面且不含已删页面', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: workspacePages.map((page) => ({ path: page.path, component: Page })),
    })
    await router.push('/')
    await router.isReady()
    const wrapper = mount(WorkspaceTabs, {
      props: {
        items: [workspacePages[0]!],
        activePage: 'dashboard',
        administrator: true,
      },
      global: { plugins: [router] },
    })

    await wrapper.get('[data-testid="tab-add"]').trigger('click')
    const menu = wrapper.get('#tabMenu').text()
    for (const label of ['订单列表', '商品量报表', 'ERP 接入', '用户管理']) {
      expect(menu).toContain(label)
    }
    for (const removed of ['新品开发', '产品管理', '业务配置', '服务连接']) {
      expect(menu).not.toContain(removed)
    }
  })

  it('Given 员工账户 When 打开页面菜单 Then 不出现管理员页面', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: workspacePages.map((page) => ({ path: page.path, component: Page })),
    })
    await router.push('/')
    await router.isReady()
    const wrapper = mount(WorkspaceTabs, {
      props: { items: [workspacePages[0]!], activePage: 'dashboard', administrator: false },
      global: { plugins: [router] },
    })

    await wrapper.get('[data-testid="tab-add"]').trigger('click')
    const menu = wrapper.get('#tabMenu').text()
    expect(menu).toContain('订单列表')
    expect(menu).toContain('商品量报表')
    expect(menu).not.toContain('用户管理')
    expect(menu).not.toContain('ERP 接入')
  })

  it('Given 管理员侧栏 When 渲染分组 Then 真实条目可导航且即将上线不可导航', async () => {
    const wrapper = mount(AppSidebar, {
      props: { activePage: 'dashboard', collapsed: false, administrator: true },
    })

    expect(wrapper.find('[data-nav-page="orders"]').exists()).toBe(true)
    expect(wrapper.find('[data-nav-page="order-report"]').exists()).toBe(true)
    expect(wrapper.find('[data-nav-page="shops"]').exists()).toBe(true)
    expect(wrapper.find('[data-nav-page="users"]').exists()).toBe(true)
    expect(wrapper.find('[data-nav-page="business"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('新品开发')
    expect(wrapper.text()).not.toContain('服务连接')
    expect(wrapper.text()).not.toContain('原型演示')
  })

  it('Given 普通员工侧栏 When 渲染 Then 店铺与设置分组整体隐藏', async () => {
    const wrapper = mount(AppSidebar, {
      props: { activePage: 'dashboard', collapsed: false, administrator: false },
    })

    expect(wrapper.find('[data-group="orders"]').exists()).toBe(true)
    expect(wrapper.find('[data-group="shops"]').exists()).toBe(false)
    expect(wrapper.find('[data-group="settings"]').exists()).toBe(false)
    expect(wrapper.find('[data-group="upcoming"]').exists()).toBe(true)
  })

  it('Given 重复导航 When 键盘用户进入页面 Then 可通过跳转链接聚焦主内容', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: ['/', '/orders', '/reports/orders', '/settings', '/users'].map((path) => ({
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
