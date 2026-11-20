import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { installNativeDialogShim } from './installNativeDialogShim'

beforeAll(installNativeDialogShim)

const aboutBody = (overrides: Record<string, unknown> = {}) => ({
  version: 'v1.2.3',
  commit: 'ab981fa',
  buildTime: '2026-11-20T07:47:39Z',
  ...overrides,
})

async function loadAbout(info: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => Response.json(info)),
  )
  const { useAboutInfo } = await import('../../src/account/aboutInfo')
  const api = useAboutInfo()
  await flushPromises()
  return api
}

describe('构建版本信息', () => {
  beforeEach(() => {
    vi.resetModules()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('Given 注入的版本信息 When 读取 Then 版本带 v 前缀并展示七位提交', async () => {
    const about = await loadAbout(aboutBody())
    expect(about.versionLabel.value).toBe('v1.2.3')
    expect(about.commitLabel.value).toBe('ab981fa')
    expect(about.info.value?.buildTime).toBe('2026-11-20T07:47:39Z')
  })

  it('Given 版本字段缺失 When 读取 Then 显示 dev 而不是假版本', async () => {
    const about = await loadAbout({ version: '', commit: '', buildTime: '' })
    expect(about.versionLabel.value).toBe('dev')
    expect(about.commitLabel.value).toBe('')
    expect(about.info.value).toBeNull()
  })

  it('Given 版本接口失败 When 读取 Then 静默回退 dev', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(null, { status: 500 })),
    )
    const { useAboutInfo } = await import('../../src/account/aboutInfo')
    const about = useAboutInfo()
    await flushPromises()
    expect(about.versionLabel.value).toBe('dev')
  })

  it('Given 账户菜单 When 渲染 Then 底部版本行展示版本与提交', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Response.json(aboutBody())),
    )
    const { default: AccountMenu } = await import('../../src/components/navigation/AccountMenu.vue')
    const { adminAccount } = await import('./adminAccount')
    const wrapper = mount(AccountMenu, { props: { account: adminAccount } })
    await flushPromises()
    expect(wrapper.get('[data-testid="account-version"]').text()).toBe('v1.2.3 · ab981fa')
  })

  it('Given 关于弹窗 When 打开 Then 展示版本、提交与构建时间', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Response.json(aboutBody())),
    )
    const { default: AboutDialog } = await import('../../src/account/AboutDialog.vue')
    const wrapper = mount(AboutDialog, { props: { open: true }, attachTo: document.body })
    await flushPromises()

    const text = wrapper.get('#aboutLayer').text()
    expect(text).toContain('关于 OpenERP')
    expect(text).toContain('v1.2.3')
    expect(text).toContain('ab981fa')
    expect(text).toContain('2026-11-20T07:47:39Z')
  })

  it('Given 登录页 When 渲染 Then 页脚展示版本行', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Response.json(aboutBody())),
    )
    const { default: LoginView } = await import('../../src/views/LoginView.vue')
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/login', component: LoginView }],
    })
    await router.push('/login')
    await router.isReady()
    const wrapper = mount(LoginView, { global: { plugins: [router] } })
    await flushPromises()

    expect(wrapper.get('[data-testid="login-version"]').text()).toBe('v1.2.3 · ab981fa')
  })
})
