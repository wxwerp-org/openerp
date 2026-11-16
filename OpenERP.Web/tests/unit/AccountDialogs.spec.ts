import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { beforeAll, afterEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import AppShell from '../../src/components/layout/AppShell.vue'
import { installNativeDialogShim } from './installNativeDialogShim'

beforeAll(installNativeDialogShim)

const Page = { template: '<main>页面</main>' }

async function mountShell(): Promise<VueWrapper> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: Page, meta: { title: '首页' } },
      { path: '/newdev', component: Page, meta: { title: '新品开发' } },
      { path: '/products', component: Page, meta: { title: '产品管理' } },
      { path: '/orders', component: Page, meta: { title: '订单列表' } },
      { path: '/settings', component: Page, meta: { title: '业务配置' } },
      { path: '/users', component: Page, meta: { title: '用户管理' } },
    ],
  })
  await router.push('/')
  await router.isReady()
  const wrapper = mount(AppShell, {
    attachTo: document.body,
    global: { plugins: [router] },
  })
  await flushPromises()
  return wrapper
}

describe('原型账户弹窗', () => {
  let wrapper: VueWrapper | undefined

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
  })

  it('Given 账户菜单 When 选择修改个人信息 Then 显示原型个人信息弹窗', async () => {
    wrapper = await mountShell()
    await wrapper.get('.account-button').trigger('click')
    await wrapper.get('[data-account-action="profile"]').trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(wrapper.get('#profileLayer').classes()).toContain('open')
    expect(wrapper.get('#profileName').element).toHaveProperty('value', '张明')
    expect(document.activeElement?.id).toBe('profileName')
  })

  it('Given 账户菜单 When 选择修改密码并输入不一致密码 Then 显示确认密码错误', async () => {
    wrapper = await mountShell()
    await wrapper.get('.account-button').trigger('click')
    await wrapper.get('[data-account-action="password"]').trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(wrapper.get('#passwordLayer').classes()).toContain('open')
    expect(document.activeElement?.id).toBe('oldPassword')
    await wrapper.get('#oldPassword').setValue('old-password')
    await wrapper.get('#newPassword').setValue('new-password')
    await wrapper.get('#confirmPassword').setValue('different')
    await wrapper.get('#savePassword').trigger('click')
    expect(wrapper.get('#confirmPassword').text()).toBe('')
    expect(wrapper.get('#passwordLayer').text()).toContain('两次输入的密码不一致')
  })
})
