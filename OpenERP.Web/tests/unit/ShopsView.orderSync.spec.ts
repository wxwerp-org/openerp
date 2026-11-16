import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import ShopsView from '../../src/views/ShopsView.vue'

const shop = {
  sid: '42',
  name: '测试店铺',
  platform: 'Temu',
  region: 'US',
  isTokenOk: false,
  statusError: '授权已失效',
}

const shopList = {
  items: [shop],
  page: 1,
  pageSize: 20,
  totalCount: 1,
  pageCount: 1,
}

type SyncTask = {
  id: string
  sid: number
  status: 'running' | 'succeeded' | 'partial' | 'failed'
  stage: 'shop' | 'pages' | 'details' | 'saving' | 'done'
  total: number
  read: number
  processed: number
  added: number
  updated: number
  unchanged: number
  skipped: number
  skipReasons: Record<string, number>
  error: string | null
}

function task(overrides: Partial<SyncTask> = {}): SyncTask {
  return {
    id: 'task-1',
    sid: 42,
    status: 'running',
    stage: 'pages',
    total: 10,
    read: 4,
    processed: 3,
    added: 1,
    updated: 1,
    unchanged: 1,
    skipped: 0,
    skipReasons: {},
    error: null,
    ...overrides,
  }
}

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function configureFetch(options: {
  tokenStatus?: boolean | null
  getConfig?: () => unknown
  getTask?: () => Promise<Response> | Response
  postTask?: () => Promise<Response> | Response
} = {}) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url === '/api/auth/csrf') {
      return jsonResponse({ requestToken: 'shop-token' })
    }
    if (url === '/api/erp/config') {
      return jsonResponse(options.getConfig?.() ?? {
        configured: true,
        baseUrl: 'https://erp.test',
        orderSyncEnabled: true,
      })
    }
    if (url.startsWith('/api/erp/shops?')) {
      const visibleShop = options.tokenStatus === undefined
        ? shop
        : { ...shop, isTokenOk: options.tokenStatus }
      return jsonResponse({ ...shopList, items: [visibleShop] })
    }
    if (url === '/api/erp/order-sync-settings') {
      return jsonResponse({ enabled: false, shopSids: [], intervalMinutes: 30 })
    }
    if (url === '/api/erp/order-sync-settings/shops') {
      return jsonResponse({ items: [{ sid: '42', name: '测试店铺' }] })
    }
    if (url === '/api/erp/shops/42/order-sync' && init?.method === 'POST') {
      return options.postTask?.() ?? jsonResponse(task(), 202)
    }
    if (url === '/api/erp/shops/42/order-sync') {
      return options.getTask?.() ?? new Response(null, { status: 204 })
    }

    throw new Error(`意外的请求：${url}`)
  })

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

async function mountShops(): Promise<VueWrapper> {
  const wrapper = mount(ShopsView)
  await flushPromises()
  return wrapper
}

function syncButton(wrapper: VueWrapper) {
  return wrapper.findAll('button').find((button) => button.text().includes('同步历史订单'))
}

function orderSyncProgress(wrapper: VueWrapper): number {
  return Number(wrapper.get('[role="progressbar"]').attributes('aria-valuenow'))
}

function findRefreshButton(wrapper: VueWrapper) {
  return wrapper.get('button[aria-label="刷新"]')
}

describe('店铺历史订单同步', () => {
  let wrapper: VueWrapper | undefined

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  it.each([
    { tokenStatus: true, tokenLabel: '有效' },
    { tokenStatus: false, tokenLabel: '无效' },
    { tokenStatus: null, tokenLabel: '未知' },
  ])('Given 店铺授权$tokenLabel When 点击同步历史订单 Then 携带防伪令牌发起无请求体的任务并显示进度', async ({ tokenStatus, tokenLabel }) => {
    const fetchMock = configureFetch({ tokenStatus })
    wrapper = await mountShops()

    expect(wrapper.text()).toContain(tokenLabel)
    const button = syncButton(wrapper)
    expect(button).toBeDefined()
    expect(button!.attributes('disabled')).toBeUndefined()

    await button!.trigger('click')
    await flushPromises()

    const postCall = fetchMock.mock.calls.find(([input, init]) =>
      String(input) === '/api/erp/shops/42/order-sync' && init?.method === 'POST',
    )
    expect(new Headers(postCall?.[1]?.headers).get('X-CSRF-TOKEN')).toBe('shop-token')
    expect(postCall?.[1]?.body).toBeUndefined()
    expect(postCall?.[1]?.signal).toBeUndefined()
    expect(wrapper.text()).toContain('读取 4 / 10')
    expect(wrapper.text()).toContain('已处理 3')
  })

  it.each([
    {
      label: '能力关闭',
      config: { configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: false },
    },
    {
      label: '旧配置缺少能力字段',
      config: { configured: true, baseUrl: 'https://erp.test' },
    },
  ])('Given $label When 店铺页加载并刷新 Then 隐藏同步功能且不请求同步 API', async ({ config }) => {
    const fetchMock = configureFetch({ getConfig: () => config })
    wrapper = await mountShops()

    expect(wrapper.text()).toContain('测试店铺')
    expect(syncButton(wrapper)).toBeUndefined()

    const refreshButton = findRefreshButton(wrapper)
    await refreshButton.trigger('click')
    await flushPromises()

    const orderSyncCalls = fetchMock.mock.calls.filter(([input]) =>
      String(input).includes('/order-sync'),
    )
    expect(orderSyncCalls).toHaveLength(0)
  })

  it('Given 同步能力重新读取后关闭且存在轮询 When 店铺页刷新 Then 清除轮询并停止同步状态请求', async () => {
    vi.useFakeTimers()
    const setTimeoutSpy = vi.spyOn(globalThis, 'setTimeout')
    const clearTimeoutSpy = vi.spyOn(globalThis, 'clearTimeout')
    let orderSyncEnabled = true
    const fetchMock = configureFetch({
      getConfig: () => ({
        configured: true,
        baseUrl: 'https://erp.test',
        orderSyncEnabled,
      }),
      getTask: () => jsonResponse(task()),
    })
    wrapper = await mountShops()

    const initialStatusCalls = fetchMock.mock.calls.filter(([input, init]) =>
      String(input) === '/api/erp/shops/42/order-sync' && init?.method !== 'POST',
    )
    expect(initialStatusCalls).toHaveLength(1)
    const pollTimerIndex = setTimeoutSpy.mock.calls.findIndex(([, delay]) => delay === 2000)
    expect(pollTimerIndex).toBeGreaterThanOrEqual(0)
    const pollTimer = setTimeoutSpy.mock.results[pollTimerIndex]?.value

    orderSyncEnabled = false
    const refreshButton = findRefreshButton(wrapper)
    await refreshButton.trigger('click')
    await flushPromises()

    expect(syncButton(wrapper)).toBeUndefined()
    expect(clearTimeoutSpy).toHaveBeenCalledWith(pollTimer)
    await vi.advanceTimersByTimeAsync(2000)
    await flushPromises()
    const finalStatusCalls = fetchMock.mock.calls.filter(([input, init]) =>
      String(input) === '/api/erp/shops/42/order-sync' && init?.method !== 'POST',
    )
    expect(finalStatusCalls).toHaveLength(1)
  })

  it('Given 有运行中的任务 When 页面载入 Then 展示阶段、计数和进度', async () => {
    configureFetch({ getTask: () => jsonResponse(task({ stage: 'details', read: 6, processed: 5 })) })
    wrapper = await mountShops()

    expect(wrapper.text()).toContain('正在同步')
    expect(wrapper.text()).toContain('商品详情')
    expect(wrapper.text()).toContain('读取 6 / 10')
    expect(wrapper.text()).toContain('已处理 5')
  })

  it('Given 订单列表已读取完成 When 转入商品详情阶段 Then 进度不回退', async () => {
    let taskReadCount = 0
    configureFetch({
      getTask: () => {
        taskReadCount += 1
        return taskReadCount === 1
          ? jsonResponse(task({ stage: 'pages', total: 10, read: 10, processed: 0 }))
          : jsonResponse(task({ stage: 'details', total: 10, read: 10, processed: 0 }))
      },
    })
    wrapper = await mountShops()
    const pagesProgress = orderSyncProgress(wrapper)

    const refreshButton = findRefreshButton(wrapper)
    await refreshButton.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('商品详情')
    expect(pagesProgress).toBe(50)
    expect(orderSyncProgress(wrapper)).toBe(pagesProgress)
  })

  it('Given 商品详情处理数增加 When 刷新运行状态 Then 进度随详情推进且未达到完成', async () => {
    let taskReadCount = 0
    configureFetch({
      getTask: () => {
        taskReadCount += 1
        return jsonResponse(task({
          stage: 'details',
          total: 10,
          read: 10,
          processed: taskReadCount === 1 ? 2 : 5,
        }))
      },
    })
    wrapper = await mountShops()
    const initialProgress = orderSyncProgress(wrapper)

    const refreshButton = findRefreshButton(wrapper)
    await refreshButton.trigger('click')
    await flushPromises()

    const nextProgress = orderSyncProgress(wrapper)
    expect(nextProgress).toBeGreaterThan(initialProgress)
    expect(nextProgress).toBeLessThan(100)
  })

  it('Given 列表读取阶段提前失败 When 展示失败任务 Then 保留失败时进度且不显示完成', async () => {
    configureFetch({
      getTask: () => jsonResponse(task({
        status: 'failed',
        stage: 'pages',
        total: 10,
        read: 3,
        processed: 0,
        error: '订单列表读取失败',
      })),
    })
    wrapper = await mountShops()

    expect(wrapper.text()).toContain('同步失败')
    expect(orderSyncProgress(wrapper)).toBe(15)
    expect(orderSyncProgress(wrapper)).toBeLessThan(100)
  })

  it('Given 任务正在保存 When 保存尚未结束 Then 进度保持未完成', async () => {
    configureFetch({
      getTask: () => jsonResponse(task({ stage: 'saving', total: 10, read: 10, processed: 10 })),
    })
    wrapper = await mountShops()

    expect(wrapper.text()).toContain('保存结果')
    expect(orderSyncProgress(wrapper)).toBeLessThan(100)
  })

  it('Given 商品详情阶段失败 When 处理数增加直至等于总数 Then 进度按公式推进且封顶 99', async () => {
    let taskReadCount = 0
    configureFetch({
      getTask: () => {
        taskReadCount += 1
        return jsonResponse(task({
          status: 'failed',
          stage: 'details',
          total: 10,
          read: 10,
          processed: taskReadCount === 1 ? 5 : 10,
          error: '商品详情读取失败',
        }))
      },
    })
    wrapper = await mountShops()

    expect(wrapper.text()).toContain('同步失败')
    expect(orderSyncProgress(wrapper)).toBe(75)

    const refreshButton = findRefreshButton(wrapper)
    await refreshButton.trigger('click')
    await flushPromises()

    expect(orderSyncProgress(wrapper)).toBe(99)
  })

  it.each(['succeeded', 'partial'] as const)(
    'Given 任务以%s结束 When 显示结果 Then 进度为100%%',
    async (status) => {
      configureFetch({
        getTask: () => jsonResponse(task({
          status,
          stage: 'done',
          total: 10,
          read: 10,
          processed: 10,
        })),
      })
      wrapper = await mountShops()

      expect(orderSyncProgress(wrapper)).toBe(100)
    },
  )

  it('Given 已提交同步请求 When 连续点击同步按钮 Then 只提交一次且按钮保持禁用', async () => {
    let resolvePost: ((response: Response) => void) | undefined
    const postPromise = new Promise<Response>((resolve) => {
      resolvePost = resolve
    })
    const fetchMock = configureFetch({ postTask: () => postPromise })
    wrapper = await mountShops()
    const button = syncButton(wrapper)!

    await button.trigger('click')
    await button.trigger('click')

    const postCalls = fetchMock.mock.calls.filter(([input, init]) =>
      String(input) === '/api/erp/shops/42/order-sync' && init?.method === 'POST',
    )
    expect(postCalls).toHaveLength(1)
    expect(syncButton(wrapper)?.attributes('disabled')).toBeDefined()

    resolvePost!(jsonResponse(task(), 202))
    await flushPromises()
    expect(wrapper.text()).toContain('正在同步')
  })

  it('Given 服务端返回已有同步任务 When 前端重新查询 Then 展示现有任务并阻止再次提交', async () => {
    let taskReadCount = 0
    const fetchMock = configureFetch({
      getTask: () => {
        taskReadCount += 1
        return taskReadCount === 1
          ? new Response(null, { status: 204 })
          : jsonResponse(task({ read: 5, processed: 4 }))
      },
      postTask: () => new Response(null, { status: 409 }),
    })
    wrapper = await mountShops()
    await syncButton(wrapper)!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('正在同步')
    expect(wrapper.text()).toContain('读取 5 / 10')
    expect(syncButton(wrapper)?.attributes('disabled')).toBeDefined()
    const postCalls = fetchMock.mock.calls.filter(([input, init]) =>
      String(input) === '/api/erp/shops/42/order-sync' && init?.method === 'POST',
    )
    expect(postCalls).toHaveLength(1)
  })

  it('Given 另一家店正在同步 When 本店返回冲突且没有任务 Then 提示稍后重试且本店可再次提交', async () => {
    let posts = 0
    configureFetch({
      postTask: () => ++posts === 1 ? new Response(null, { status: 409 }) : jsonResponse(task(), 202),
    })
    wrapper = await mountShops()

    await syncButton(wrapper)!.trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('已有同步任务')
    expect(syncButton(wrapper)?.attributes('disabled')).toBeUndefined()

    await syncButton(wrapper)!.trigger('click')
    await flushPromises()
    expect(posts).toBe(2)
    expect(wrapper.text()).toContain('正在同步')
  })

  it('Given 最近任务部分成功且有跳过原因 When 页面载入 Then 显示部分成功、错误和原因计数', async () => {
    configureFetch({
      getTask: () => jsonResponse(task({
        status: 'partial',
        stage: 'done',
        total: 10,
        read: 10,
        processed: 8,
        skipped: 2,
        skipReasons: { missingSku: 2 },
        error: '两个商品缺少 SKU',
      })),
    })
    wrapper = await mountShops()

    expect(wrapper.text()).toContain('部分成功')
    expect(wrapper.text()).toContain('两个商品缺少 SKU')
    expect(wrapper.text()).toContain('missingSku：2')
  })

  it('Given 首次查询尚无任务 When 刷新店铺列表后 Then 重新查询并恢复可见店铺的运行状态', async () => {
    let taskReadCount = 0
    const fetchMock = configureFetch({
      getTask: () => {
        taskReadCount += 1
        return taskReadCount === 1
          ? new Response(null, { status: 204 })
          : jsonResponse(task({ read: 7, processed: 6 }))
      },
    })
    wrapper = await mountShops()

    expect(wrapper.text()).not.toContain('正在同步')
    const refreshButton = findRefreshButton(wrapper)
    await refreshButton.trigger('click')
    await flushPromises()

    const statusCalls = fetchMock.mock.calls.filter(([input, init]) =>
      String(input) === '/api/erp/shops/42/order-sync' && init?.method !== 'POST',
    )
    expect(statusCalls).toHaveLength(2)
    expect(wrapper.text()).toContain('正在同步')
    expect(wrapper.text()).toContain('读取 7 / 10')
  })

  it('Given 第一页店铺正在同步 When 换到第二页 Then 清除不可见任务且只查询可见店铺状态', async () => {
    vi.useFakeTimers()
    let status42Calls = 0
    let status43Calls = 0
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      if (url === '/api/erp/config') {
        return jsonResponse({ configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: true })
      }
      if (url.startsWith('/api/erp/shops?')) {
        const page = new URL(url, 'https://local.test').searchParams.get('page')
        const visibleShop = page === '2'
          ? { ...shop, sid: '43', name: '第二页店铺' }
          : shop
        return jsonResponse({
          items: [visibleShop],
          page: Number(page),
          pageSize: 20,
          totalCount: 2,
          pageCount: 2,
        })
      }
      if (url === '/api/erp/shops/42/order-sync') {
        status42Calls += 1
        return jsonResponse(task({ sid: 42 }))
      }
      if (url === '/api/erp/shops/43/order-sync') {
        status43Calls += 1
        return new Response(null, { status: 204 })
      }

      throw new Error(`意外的请求：${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    wrapper = await mountShops()

    expect(wrapper.text()).toContain('正在同步')
    await wrapper.get('button[aria-label="下一页"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('第二页店铺')
    expect(wrapper.text()).not.toContain('正在同步')
    expect(status42Calls).toBe(1)
    expect(status43Calls).toBe(1)

    await vi.advanceTimersByTimeAsync(2000)
    await flushPromises()

    expect(status42Calls).toBe(1)
    expect(status43Calls).toBe(1)
  })

  it('Given 页面正在轮询运行任务 When 页面卸载 Then 清除轮询定时器', async () => {
    vi.useFakeTimers()
    const setTimeoutSpy = vi.spyOn(globalThis, 'setTimeout')
    const clearTimeoutSpy = vi.spyOn(globalThis, 'clearTimeout')
    configureFetch({ getTask: () => jsonResponse(task()) })
    wrapper = await mountShops()

    const pollTimerIndex = setTimeoutSpy.mock.calls.findIndex(([, delay]) => delay === 2000)
    expect(pollTimerIndex).toBeGreaterThanOrEqual(0)
    const pollTimer = setTimeoutSpy.mock.results[pollTimerIndex]?.value
    wrapper.unmount()
    wrapper = undefined

    expect(clearTimeoutSpy).toHaveBeenCalledWith(pollTimer)
  })
})
