import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { beforeAll, afterEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import AppShell from '../../src/components/layout/AppShell.vue'
import { installNativeDialogShim } from './installNativeDialogShim'
import { adminAccount } from './adminAccount'

beforeAll(installNativeDialogShim)

const Page = { template: '<main>页面</main>' }

async function mountShell(): Promise<VueWrapper> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: Page, meta: { title: '首页' } },
      { path: '/orders', component: Page, meta: { title: '订单列表' } },
      { path: '/reports/orders', component: Page, meta: { title: '商品量报表' } },
      { path: '/settings', component: Page, meta: { title: 'ERP 接入' } },
      { path: '/users', component: Page, meta: { title: '用户管理' } },
    ],
  })
  await router.push('/')
  await router.isReady()
  const wrapper = mount(AppShell, {
    props: { account: adminAccount },
    attachTo: document.body,
    global: { plugins: [router] },
  })
  await flushPromises()
  return wrapper
}

describe('账户弹窗', () => {
  let wrapper: VueWrapper | undefined

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
  })

  it('Given 账户菜单 When 选择修改个人信息 Then 显示会话中的姓名并聚焦输入框', async () => {
    wrapper = await mountShell()
    await wrapper.get('[data-testid="account-button"]').trigger('click')
    await wrapper.get('[data-account-action="profile"]').trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(wrapper.get('#profileLayer').attributes('open')).toBeDefined()
    expect(wrapper.get('#profileName').element).toHaveProperty('value', adminAccount.name)
    expect(document.activeElement?.id).toBe('profileName')
  })

  it('Given 账户菜单 When 选择修改密码并输入不一致密码 Then 显示确认密码错误', async () => {
    wrapper = await mountShell()
    await wrapper.get('[data-testid="account-button"]').trigger('click')
    await wrapper.get('[data-account-action="password"]').trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(wrapper.get('#passwordLayer').attributes('open')).toBeDefined()
    expect(document.activeElement?.id).toBe('oldPassword')
    await wrapper.get('#oldPassword').setValue('old-password')
    await wrapper.get('#newPassword').setValue('new-password')
    await wrapper.get('#confirmPassword').setValue('different')
    await wrapper.get('#savePassword').trigger('click')
    expect(wrapper.get('#confirmPassword').attributes('aria-invalid')).toBe('true')
    expect(wrapper.get('#passwordLayer').text()).toContain('两次输入的密码不一致')
  })
})
