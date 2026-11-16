import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import ShopsView from '../../src/views/ShopsView.vue'

const tableShop = {
  sid: '42',
  name: '表格店铺',
  platform: 'Temu',
  region: 'US',
  isTokenOk: true,
  statusError: null,
}

const shopList = {
  items: [tableShop],
  page: 1,
  pageSize: 20,
  totalCount: 1,
  pageCount: 1,
}

const allShops = [
  { sid: '42', name: '表格店铺' },
  { sid: '77', name: '第二页店铺' },
]

type SyncSettings = {
  enabled: boolean
  shopSids: number[]
  intervalMinutes: number
}

type PutBody = {
  enabled: boolean
  shopSids: number[]
}

type FetchOptions = {
  orderSyncEnabled?: boolean
  configured?: boolean
  settings?: SyncSettings
  shops?: { sid: string; name: string }[]
  getShops?: () => Response
  getTask?: () => Response
  putSettings?: (body: PutBody) => Response
}

const runningTask = {
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
}

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function configureFetch(options: FetchOptions = {}) {
  const settings = options.settings ?? { enabled: false, shopSids: [], intervalMinutes: 30 }
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url === '/api/erp/config') {
      return jsonResponse({
        configured: options.configured ?? true,
        baseUrl: 'https://erp.test',
        orderSyncEnabled: options.orderSyncEnabled ?? true,
      })
    }
    if (url.startsWith('/api/erp/shops?')) {
      return jsonResponse(shopList)
    }
    if (url === '/api/erp/order-sync-settings/shops') {
      return options.getShops?.() ?? jsonResponse({ items: options.shops ?? allShops })
    }
    if (url === '/api/erp/order-sync-settings' && init?.method === 'PUT') {
      const body = JSON.parse(String(init.body)) as PutBody
      return options.putSettings?.(body) ?? jsonResponse({ ...body, intervalMinutes: 30 })
    }
    if (url === '/api/erp/order-sync-settings') {
      return jsonResponse(settings)
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
  await flushPromises()
  return wrapper
}

function findButton(wrapper: VueWrapper, text: string) {
  return wrapper.findAll('.sync-settings-card button').find((button) => button.text().includes(text))
}

async function setScheduleEnabled(wrapper: VueWrapper, enabled: boolean): Promise<void> {
  await wrapper.get('.sync-settings-card .el-switch__input').setValue(enabled)
  await flushPromises()
}

describe('店铺定时同步设置', () => {
  let wrapper: VueWrapper | undefined

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  it('Given 同步能力开启 When 店铺页加载 Then 显示定时同步卡片和固定周期', async () => {
    // Given 定时同步关闭，服务端固定周期为 30 分钟。
    const fetchMock = configureFetch({
      settings: { enabled: false, shopSids: [], intervalMinutes: 30 },
    })

    // When 打开店铺页。
    wrapper = await mountShops()

    // Then 卡片显示启用状态和只读周期，并读取全部可见店铺。
    expect(wrapper.text()).toContain('定时同步')
    expect(wrapper.text()).toContain('每 30 分钟尝试一轮')
    expect(wrapper.text()).toContain('未启用')
    const settingsCalls = fetchMock.mock.calls.filter(([input]) =>
      String(input) === '/api/erp/order-sync-settings',
    )
    expect(settingsCalls).toHaveLength(1)
    const shopCalls = fetchMock.mock.calls.filter(([input]) =>
      String(input) === '/api/erp/order-sync-settings/shops',
    )
    expect(shopCalls).toHaveLength(1)
  })

  it('Given 同步能力关闭 When 店铺页加载 Then 隐藏卡片且不请求设置接口', async () => {
    // Given 服务端关闭订单同步能力。
    const fetchMock = configureFetch({ orderSyncEnabled: false })

    // When 打开店铺页。
    wrapper = await mountShops()

    // Then 设置卡片不存在，也不请求定时同步设置。
    expect(wrapper.find('.sync-settings-card').exists()).toBe(false)
    const scheduleCalls = fetchMock.mock.calls.filter(([input]) =>
      String(input).includes('/order-sync-settings'),
    )
    expect(scheduleCalls).toHaveLength(0)
  })

  it('Given 启用但未选店铺 When 查看保存 Then 提示至少选择一家且禁止保存', async () => {
    // Given 定时同步关闭。
    const fetchMock = configureFetch()
    wrapper = await mountShops()

    // When 只启用定时同步，不选择店铺。
    await setScheduleEnabled(wrapper, true)

    // Then 提示并禁用保存，不会发出保存请求。
    expect(wrapper.text()).toContain('启用定时同步前请至少选择一家店铺')
    expect(findButton(wrapper, '保存')?.attributes('disabled')).toBeDefined()
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(false)
  })

  it('Given 全选可见店铺 When 保存 Then 提交全部店铺并显示启用反馈', async () => {
    // Given 可选店铺覆盖表格当前页之外的另一家店。
    const putBodies: PutBody[] = []
    configureFetch({
      shops: allShops,
      putSettings: (body) => {
        putBodies.push(body)
        return jsonResponse({ ...body, intervalMinutes: 30 })
      },
    })
    wrapper = await mountShops()

    // When 启用、全选可见店铺并保存。
    await setScheduleEnabled(wrapper, true)
    await findButton(wrapper, '全选可见店铺')!.trigger('click')
    await flushPromises()
    await findButton(wrapper, '保存')!.trigger('click')
    await flushPromises()

    // Then 提交全部店铺 SID，并提示下一个周期首次执行。
    expect(putBodies).toEqual([{ enabled: true, shopSids: [42, 77] }])
    expect(wrapper.text()).toContain('定时同步已启用')
  })

  it('Given 保存失败 When 提交设置 Then 显示错误并保留已选店铺', async () => {
    // Given 保存接口返回错误。
    configureFetch({
      shops: allShops,
      putSettings: () => jsonResponse({ title: '保存定时同步设置失败' }, 500),
    })
    wrapper = await mountShops()
    await setScheduleEnabled(wrapper, true)
    await findButton(wrapper, '全选可见店铺')!.trigger('click')

    // When 保存。
    await findButton(wrapper, '保存')!.trigger('click')
    await flushPromises()

    // Then 显示可读错误，卡片和已选店铺仍然保留。
    expect(wrapper.text()).toContain('保存定时同步设置失败')
    expect(wrapper.find('.sync-settings-card').exists()).toBe(true)
    expect(wrapper.text()).toContain('表格店铺')
  })

  it('Given 已启用且选了店铺 When 关闭并保存 Then 只关闭开关并保留店铺选择', async () => {
    // Given 服务端已有启用设置和一家店。
    const putBodies: PutBody[] = []
    configureFetch({
      settings: { enabled: true, shopSids: [42], intervalMinutes: 30 },
      putSettings: (body) => {
        putBodies.push(body)
        return jsonResponse({ ...body, intervalMinutes: 30 })
      },
    })
    wrapper = await mountShops()
    expect(wrapper.text()).toContain('已启用')

    // When 关闭开关并保存。
    await setScheduleEnabled(wrapper, false)
    await findButton(wrapper, '保存')!.trigger('click')
    await flushPromises()

    // Then 只关闭启用状态，已选店铺保留。
    expect(putBodies).toEqual([{ enabled: false, shopSids: [42] }])
    expect(wrapper.text()).toContain('定时同步已关闭')
  })

  it('Given 选店接口暂时失败 When 点击卡片重试 Then 恢复选店且保留开关草稿', async () => {
    // Given 设置读取成功但选店接口第一次失败。
    let shopCalls = 0
    configureFetch({
      getShops: () => {
        shopCalls += 1
        return shopCalls === 1
          ? jsonResponse({ title: '获取店铺失败' }, 502)
          : jsonResponse({ items: allShops })
      },
    })
    wrapper = await mountShops()
    expect(wrapper.text()).toContain('获取店铺失败')

    // When 用户已打开开关，并点击卡片内的选店重试。
    await setScheduleEnabled(wrapper, true)
    await findButton(wrapper, '重试')!.trigger('click')
    await flushPromises()

    // Then 选店列表恢复，草稿中的启用状态没有被设置接口覆盖。
    expect(shopCalls).toBe(2)
    expect(wrapper.text()).toContain('表格店铺')
    expect(
      (wrapper.get('.sync-settings-card .el-switch__input').element as HTMLInputElement).checked,
    ).toBe(true)
  })

  it('Given 选店接口暂时失败 When 点击页面刷新 Then 也会重试选店', async () => {
    // Given 设置读取成功但选店接口第一次失败。
    let shopCalls = 0
    configureFetch({
      getShops: () => {
        shopCalls += 1
        return shopCalls === 1
          ? jsonResponse({ title: '获取店铺失败' }, 502)
          : jsonResponse({ items: allShops })
      },
    })
    wrapper = await mountShops()
    expect(wrapper.text()).toContain('获取店铺失败')

    // When 点击店铺页顶部的刷新。
    const refresh = wrapper.findAll('button').find((button) => button.text().includes('刷新'))!
    await refresh.trigger('click')
    await flushPromises()

    // Then 选店列表随之恢复。
    expect(shopCalls).toBe(2)
    expect(wrapper.text()).toContain('表格店铺')
  })

  it('Given 定时同步已启用且页面空闲 When 后台启动任务 Then 低频发现并切换到轮询', async () => {
    // Given 定时同步已启用，页面初始没有运行中的任务。
    vi.useFakeTimers()
    let statusReads = 0
    configureFetch({
      settings: { enabled: true, shopSids: [42], intervalMinutes: 30 },
      getTask: () => {
        statusReads += 1
        return statusReads === 1
          ? new Response(null, { status: 204 })
          : jsonResponse(runningTask)
      },
    })
    wrapper = await mountShops()
    expect(statusReads).toBe(1)

    // When 15 秒后后台已经有定时批次在运行。
    await vi.advanceTimersByTimeAsync(15000)
    await flushPromises()

    // Then 发现运行任务并展示进度，随后按 2 秒轮询。
    expect(statusReads).toBeGreaterThanOrEqual(2)
    expect(wrapper.text()).toContain('正在同步')
    const readsAfterDiscovery = statusReads
    await vi.advanceTimersByTimeAsync(2000)
    await flushPromises()
    expect(statusReads).toBeGreaterThan(readsAfterDiscovery)
  })

  it('Given 未配置 Token When 加载设置 Then 提示配置后可选择店铺', async () => {
    // Given 私有 ERP Token 未配置。
    configureFetch({
      configured: false,
      settings: { enabled: false, shopSids: [], intervalMinutes: 30 },
    })

    // When 打开店铺页。
    wrapper = await mountShops()

    // Then 设置可读取，但选店、开关和保存都要求先配置 Token。
    expect(wrapper.text()).toContain('配置私有 ERP AccessToken 后可选择店铺。')
    expect(wrapper.get('.sync-settings-card .el-switch__input').attributes('disabled')).toBeDefined()
    expect(findButton(wrapper, '保存')?.attributes('disabled')).toBeDefined()
  })
})
