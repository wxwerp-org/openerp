import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import BusinessConfigView from '../../src/views/BusinessConfigView.vue'

const configPath = '/api/erp/config'

function configResponse(configured: boolean): Response {
  return Response.json({ configured, baseUrl: 'https://erp.test', orderSyncEnabled: false })
}

function stubApi(handler: (method: string, request?: RequestInit) => Response) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, request?: RequestInit) => {
    expect(String(input)).toBe(configPath)
    return handler(request?.method ?? 'GET', request)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('系统设置中的 ERP 接入', () => {
  let wrapper: VueWrapper

  afterEach(() => wrapper?.unmount())

  async function openSettings() {
    wrapper = mount(BusinessConfigView)
    await wrapper.get('[data-settings-item="ERP 接入"]').trigger('click')
    await flushPromises()
    return wrapper.get('#settingsContent')
  }

  it('Given 尚未配置 ERP Token When 填写并保存 Then 提交新值、显示已配置并清空密码字段', async () => {
    const fetchMock = stubApi((method, request) => {
      if (method === 'GET') return configResponse(false)
      expect(method).toBe('PUT')
      expect(new Headers(request?.headers).get('Content-Type')).toBe('application/json')
      expect(JSON.parse(String(request?.body))).toEqual({ accessToken: 'initial-test-token' })
      return new Response(null, { status: 204 })
    })
    const settings = await openSettings()
    const input = settings.get<HTMLInputElement>('input[type="password"]')

    expect(settings.get('[data-testid="erp-config-status"] [role="status"]').text()).toBe('未配置')
    expect(settings.get('label').attributes('for')).toBe(input.attributes('id'))
    expect(input.element.value).toBe('')
    expect(settings.get('button[type="submit"]').attributes('disabled')).toBeDefined()

    await input.setValue('   ')
    await settings.get('form').trigger('submit')
    await flushPromises()
    expect(fetchMock).toHaveBeenCalledTimes(1)

    await input.setValue('initial-test-token')
    await settings.get('form').trigger('submit')
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(settings.get('[data-testid="erp-config-status"] [role="status"]').text()).toBe('已配置')
    expect(settings.text()).toContain('保存成功')
    expect(input.element.value).toBe('')
  })

  it('Given ERP Token 已配置 When 更新失败后重试 Then 显示错误并允许重试，旧值始终为空', async () => {
    let attempts = 0
    stubApi((method) => {
      if (method === 'GET') return configResponse(true)
      attempts++
      return attempts === 1
        ? Response.json({ detail: 'unexpected-echo-of-test-token' }, { status: 503 })
        : new Response(null, { status: 204 })
    })
    const settings = await openSettings()
    const input = settings.get<HTMLInputElement>('input[type="password"]')

    expect(settings.get('[data-testid="erp-config-status"] [role="status"]').text()).toBe('已配置')
    expect(input.element.value).toBe('')
    await input.setValue('replacement-test-token')
    await settings.get('form').trigger('submit')
    await flushPromises()

    expect(settings.get('[role="alert"]').text()).toContain('保存失败')
    expect(settings.text()).not.toContain('unexpected-echo-of-test-token')
    expect(settings.get('[data-testid="erp-config-status"] [role="status"]').text()).toBe('已配置')
    expect(settings.get('button[type="submit"]').attributes('disabled')).toBeUndefined()

    await settings.get('form').trigger('submit')
    await flushPromises()

    expect(settings.find('[role="alert"]').exists()).toBe(false)
    expect(settings.text()).toContain('保存成功')
    expect(input.element.value).toBe('')
  })

  it('Given ERP 配置读取失败 When 重试读取成功 Then 状态从读取失败变为已配置', async () => {
    let reads = 0
    stubApi(() => {
      reads++
      return reads === 1
        ? Response.json({ detail: 'error' }, { status: 503 })
        : configResponse(true)
    })
    const settings = await openSettings()

    expect(settings.get('[data-testid="erp-config-status"] [role="status"]').text()).toBe(
      '读取失败',
    )
    const retry = settings.get('button[type="button"]')
    expect(retry.text()).toBe('重试')
    await retry.trigger('click')
    await flushPromises()

    expect(settings.get('[data-testid="erp-config-status"] [role="status"]').text()).toBe('已配置')
    expect(settings.get<HTMLInputElement>('input[type="password"]').element.value).toBe('')
  })
})
