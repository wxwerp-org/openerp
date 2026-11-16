import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import HomeView from '../../src/views/HomeView.vue'

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((fulfill) => {
    resolve = fulfill
  })
  return { promise, resolve }
}

function mountHome(): VueWrapper {
  return mount(HomeView)
}

function findElNode(wrapper: VueWrapper): Element | null {
  return wrapper.element.querySelector('[class^="el-"], [class*=" el-"]')
}

describe('服务连接页', () => {
  let wrapper: VueWrapper | undefined

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    vi.unstubAllGlobals()
  })

  it('Given 页面初始化 When 挂载 Then 自动检测并先显示正在检测，且不使用 Element Plus 节点', async () => {
    const pending = deferred<Response>()
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      void input
      return pending.promise
    })
    vi.stubGlobal('fetch', fetchMock)

    wrapper = mountHome()
    await wrapper.vm.$nextTick()

    expect(wrapper.element.tagName).toBe('SECTION')
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/ping')
    expect(wrapper.get('h1').text()).toBe('服务连接')
    expect(wrapper.text()).toContain('正在检测')
    expect(wrapper.text()).toContain('正在请求 /api/ping…')
    expect(wrapper.find('.el-card').exists()).toBe(false)
    expect(findElNode(wrapper)).toBeNull()
    expect(wrapper.get('.tag').classes()).toContain('gray')

    const retry = wrapper.get('.btn.primary')
    expect(retry.text()).toBe('重试')
    expect(retry.attributes('disabled')).toBeDefined()
    expect(retry.attributes('aria-busy')).toBe('true')

    const status = wrapper.get('[role="status"]')
    expect(status.attributes('aria-live')).toBe('polite')

    pending.resolve(jsonResponse({ message: 'pong' }))
    await flushPromises()
  })

  it('Given 后端返回消息 When 请求完成 Then 显示连接成功与响应内容', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => jsonResponse({ message: 'pong' })))
    wrapper = mountHome()
    await flushPromises()

    expect(wrapper.text()).toContain('连接成功')
    expect(wrapper.text()).toContain('后端响应：pong')
    expect(wrapper.get('.tag').classes()).toContain('green')
    const retry = wrapper.get('.btn.primary')
    expect(retry.attributes('disabled')).toBeUndefined()
    expect(retry.attributes('aria-busy')).toBeUndefined()
  })

  it.each([
    {
      label: '网络异常',
      respond: () => Promise.reject(new TypeError('failed to fetch')),
      message: '网络请求失败，请检查前端代理和后端连接。',
    },
    {
      label: '非 2xx',
      respond: async () => new Response(null, { status: 500 }),
      message: '服务器返回 HTTP 500。',
    },
    {
      label: '非 JSON',
      respond: async () => new Response('not json', { status: 200 }),
      message: '服务器响应不是有效的 JSON。',
    },
    {
      label: '缺少 message',
      respond: async () => jsonResponse({ pong: true }),
      message: '服务器响应缺少字符串类型的 message 字段。',
    },
  ])('Given $label When 请求完成 Then 显示既有错误文案且重试可用', async ({ respond, message }) => {
    vi.stubGlobal('fetch', vi.fn(respond))
    wrapper = mountHome()
    await flushPromises()

    expect(wrapper.text()).toContain(message)
    expect(wrapper.get('.tag').classes()).toContain('red')
    expect(wrapper.get('.btn.primary').attributes('disabled')).toBeUndefined()
  })

  it('Given 连接失败 When 点击重试 Then 发起新请求且请求期间按钮禁用并保持原文案', async () => {
    const retryResponse = deferred<Response>()
    let calls = 0
    const fetchMock = vi.fn(() => {
      calls += 1
      return calls === 1
        ? Promise.reject(new TypeError('offline'))
        : retryResponse.promise
    })
    vi.stubGlobal('fetch', fetchMock)
    wrapper = mountHome()
    await flushPromises()
    expect(wrapper.text()).toContain('网络请求失败，请检查前端代理和后端连接。')

    await wrapper.get('.btn.primary').trigger('click')
    await wrapper.vm.$nextTick()

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(wrapper.text()).toContain('正在检测')
    const retry = wrapper.get('.btn.primary')
    expect(retry.text()).toBe('重试')
    expect(retry.attributes('disabled')).toBeDefined()
    expect(retry.attributes('aria-busy')).toBe('true')

    retryResponse.resolve(jsonResponse({ message: 'pong' }))
    await flushPromises()
    expect(wrapper.text()).toContain('连接成功')
    expect(wrapper.get('.btn.primary').attributes('disabled')).toBeUndefined()
  })

  it('Given 连续触发检测 When 旧请求晚于新请求返回 Then 旧结果不覆盖新结果', async () => {
    const first = deferred<Response>()
    const second = deferred<Response>()
    let calls = 0
    vi.stubGlobal('fetch', vi.fn(() => {
      calls += 1
      if (calls === 1) return Promise.reject(new TypeError('offline'))
      return calls === 2 ? first.promise : second.promise
    }))
    wrapper = mountHome()
    await flushPromises()

    const retry = wrapper.get('.btn.primary')
    void retry.trigger('click')
    void retry.trigger('click')
    expect(calls).toBe(3)

    second.resolve(jsonResponse({ message: '新结果' }))
    await flushPromises()
    first.resolve(new Response(null, { status: 500 }))
    await flushPromises()

    expect(wrapper.text()).toContain('连接成功')
    expect(wrapper.text()).toContain('后端响应：新结果')
    expect(wrapper.text()).not.toContain('服务器返回 HTTP 500。')
  })
})
