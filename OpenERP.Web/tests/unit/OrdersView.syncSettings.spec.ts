import { flushPromises, mount, RouterLinkStub, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { adminAccount } from './adminAccount'
import { installNativeDialogShim } from './installNativeDialogShim'

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
  getSettings?: () => Response
  getShops?: () => Response
  refresh?: () => Response
  putSettings?: (body: PutBody) => Response
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
    if (url === '/api/auth/status') {
      return jsonResponse({ initialized: true })
    }
    if (url === '/api/auth/refresh') {
      return (
        options.refresh?.() ??
        jsonResponse({
          accessToken: 'schedule-access',
          accessTokenExpiresAt: new Date(Date.now() + 20 * 60 * 1000).toISOString(),
          account: adminAccount,
        })
      )
    }
    if (url === '/api/auth/csrf') {
      return jsonResponse({ requestToken: 'schedule-token' })
    }
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
      const headers = new Headers(init.headers)
      if (
        headers.get('Authorization') !== 'Bearer schedule-access' ||
        headers.has('X-CSRF-TOKEN')
      ) {
        return jsonResponse({ title: '认证请求头不符合约定' }, 400)
      }
      const body = JSON.parse(String(init.body)) as PutBody
      return options.putSettings?.(body) ?? jsonResponse({ ...body, intervalMinutes: 30 })
    }
    if (url === '/api/erp/order-sync-settings') {
      return options.getSettings?.() ?? jsonResponse(settings)
    }
    if (url.startsWith('/api/orders?')) {
      return jsonResponse({
        items: [],
        total: 0,
        page: 1,
        pageSize: 20,
        options: { shops: [], statuses: [], platforms: [] },
      })
    }

    throw new Error(`意外的请求：${url}`)
  })

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

async function mountPanel(): Promise<VueWrapper> {
  const { checkSession } = await import('../../src/account/session')
  await checkSession()
  const { default: OrdersView } = await import('../../src/views/OrdersView.vue')
  installNativeDialogShim()
  const wrapper = mount(OrdersView, { global: { stubs: { RouterLink: RouterLinkStub } } })
  await wrapper.get('[data-testid="orders-toolbar"] [aria-haspopup="dialog"]').trigger('click')
  await flushPromises()
  return wrapper
}

function findButton(wrapper: VueWrapper, text: string) {
  return wrapper
    .findAll('[data-testid="sync-settings-card"] button')
    .find((button) => button.text().includes(text))
}

async function setScheduleEnabled(wrapper: VueWrapper, enabled: boolean): Promise<void> {
  await wrapper
    .get('[data-testid="sync-settings-card"] input[aria-label="启用定时同步"]')
    .setValue(enabled)
  await flushPromises()
}

describe('订单同步面板定时同步设置', () => {
  let wrapper: VueWrapper | undefined

  beforeEach(() => vi.resetModules())

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    vi.unstubAllGlobals()
  })

  it('Given 同步能力开启 When 打开同步面板 Then 显示定时同步卡片和固定周期', async () => {
    // Given 定时同步关闭，服务端固定周期为 30 分钟。
    const fetchMock = configureFetch({
      settings: { enabled: false, shopSids: [], intervalMinutes: 30 },
    })

    // When 打开同步面板。
    wrapper = await mountPanel()

    // Then 卡片显示启用状态和只读周期，并读取全部可见店铺。
    expect(wrapper.text()).toContain('定时同步')
    expect(wrapper.text()).toContain('每 30 分钟尝试一轮')
    expect(wrapper.text()).toContain('未启用')
    const settingsCalls = fetchMock.mock.calls.filter(
      ([input]) => String(input) === '/api/erp/order-sync-settings',
    )
    expect(settingsCalls).toHaveLength(1)
    const shopCalls = fetchMock.mock.calls.filter(
      ([input]) => String(input) === '/api/erp/order-sync-settings/shops',
    )
    expect(shopCalls).toHaveLength(1)
  })

  it('Given 同步能力关闭 When 打开同步面板 Then 隐藏卡片且不请求设置接口', async () => {
    // Given 服务端关闭订单同步能力。
    const fetchMock = configureFetch({ orderSyncEnabled: false })

    // When 打开同步面板。
    wrapper = await mountPanel()

    // Then 设置卡片不存在，也不请求定时同步设置。
    expect(wrapper.find('[data-testid="sync-settings-card"]').exists()).toBe(false)
    const scheduleCalls = fetchMock.mock.calls.filter(([input]) =>
      String(input).includes('/order-sync-settings'),
    )
    expect(scheduleCalls).toHaveLength(0)
  })

  it('Given 启用但未选店铺 When 查看保存 Then 提示至少选择一家且禁止保存', async () => {
    // Given 定时同步关闭。
    const fetchMock = configureFetch()
    wrapper = await mountPanel()

    // When 只启用定时同步，不选择店铺。
    await setScheduleEnabled(wrapper, true)

    // Then 提示并禁用保存，不会发出保存请求。
    expect(wrapper.text()).toContain('启用定时同步前请至少选择一家店铺')
    expect(findButton(wrapper, '保存')?.attributes('disabled')).toBeDefined()
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(false)
  })

  it('Given 全选可见店铺 When 保存 Then 通过 Bearer 认证并显示启用反馈', async () => {
    // Given 可选店铺覆盖表格当前页之外的另一家店。
    const putBodies: PutBody[] = []
    configureFetch({
      shops: allShops,
      putSettings: (body) => {
        putBodies.push(body)
        return jsonResponse({ ...body, intervalMinutes: 30 })
      },
    })
    wrapper = await mountPanel()

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

  it('Given 管理员会话已通过初始检查 When 读取定时同步设置返回 401 Then 会话变为未登录', async () => {
    const { account, sessionState } = await import('../../src/account/session')
    let sessionStateWhenSettingsRequested: string | undefined
    const refreshStatuses: number[] = []
    const fetchMock = configureFetch({
      getSettings: () => {
        sessionStateWhenSettingsRequested = sessionState.value
        return new Response(null, { status: 401 })
      },
      refresh: () => {
        const response =
          refreshStatuses.length === 0
            ? jsonResponse({
                accessToken: 'schedule-access',
                accessTokenExpiresAt: new Date(Date.now() + 20 * 60 * 1000).toISOString(),
                account: adminAccount,
              })
            : new Response(null, { status: 401 })
        refreshStatuses.push(response.status)
        return response
      },
    })
    wrapper = await mountPanel()

    const settingsRequests = fetchMock.mock.calls.filter(
      ([input]) => String(input) === '/api/erp/order-sync-settings',
    )
    const requestIndexes = fetchMock.mock.calls
      .map(([input], index) => (String(input) === '/api/auth/refresh' ? index : -1))
      .filter((index) => index >= 0)
    const settingsRequestIndex = fetchMock.mock.calls.findIndex(
      ([input]) => String(input) === '/api/erp/order-sync-settings',
    )
    expect(sessionStateWhenSettingsRequested).toBe('authenticated')
    expect(refreshStatuses).toEqual([200, 401])
    expect(settingsRequests).toHaveLength(1)
    expect(new Headers(settingsRequests[0]?.[1]?.headers).get('Authorization')).toBe(
      'Bearer schedule-access',
    )
    expect(requestIndexes).toHaveLength(2)
    expect(requestIndexes[0]).toBeLessThan(settingsRequestIndex)
    expect(requestIndexes[1]).toBeGreaterThan(settingsRequestIndex)
    expect(sessionState.value).toBe('anonymous')
    expect(account.value).toBeNull()
  })

  it('Given 管理员已登录 When 读取可选店铺返回尚未初始化 409 Then 清除原会话并要求初始化', async () => {
    configureFetch({ getShops: () => jsonResponse({ code: 'initialization_required' }, 409) })
    wrapper = await mountPanel()

    const { account, sessionState } = await import('../../src/account/session')
    expect(account.value).toBeNull()
    expect(sessionState.value).toBe('uninitialized')
  })

  it('Given 已保存店铺不在可选列表 When 取消兜底项并保存 Then 只提交仍选中的店铺', async () => {
    // Given 服务端保存了 99，但当前可选列表只有 42/77。
    const putBodies: PutBody[] = []
    configureFetch({
      settings: { enabled: true, shopSids: [99], intervalMinutes: 30 },
      shops: allShops,
      putSettings: (body) => {
        putBodies.push(body)
        return jsonResponse({ ...body, intervalMinutes: 30 })
      },
    })
    wrapper = await mountPanel()

    // Then 99 作为可取消的兜底项展示，而不是被静默隐藏。
    expect(wrapper.text()).toContain('已选店铺 99')

    // When 选择 42 并取消 99 后保存。
    await wrapper.get('[data-testid="sync-shop-option"] input[value="42"]').setValue(true)
    await wrapper
      .findAll('[data-testid="sync-shop-option"]')
      .find((option) => option.text().includes('99'))!
      .get('input')
      .setValue(false)
    await findButton(wrapper, '保存')!.trigger('click')
    await flushPromises()

    // Then 只提交仍选中的 42。
    expect(putBodies).toEqual([{ enabled: true, shopSids: [42] }])
  })

  it('Given 保存失败 When 提交设置 Then 显示错误并保留已选店铺', async () => {
    // Given 保存接口返回错误。
    configureFetch({
      shops: allShops,
      putSettings: () => jsonResponse({ title: '保存定时同步设置失败' }, 500),
    })
    wrapper = await mountPanel()
    await setScheduleEnabled(wrapper, true)
    await findButton(wrapper, '全选可见店铺')!.trigger('click')

    // When 保存。
    await findButton(wrapper, '保存')!.trigger('click')
    await flushPromises()

    // Then 显示可读错误，卡片和已选店铺仍然保留。
    expect(wrapper.text()).toContain('保存定时同步设置失败')
    expect(wrapper.find('[data-testid="sync-settings-card"]').exists()).toBe(true)
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
    wrapper = await mountPanel()
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
    wrapper = await mountPanel()
    expect(wrapper.text()).toContain('获取店铺失败')

    // When 用户已打开开关，并点击卡片内的选店重试。
    await setScheduleEnabled(wrapper, true)
    await findButton(wrapper, '重试')!.trigger('click')
    await flushPromises()

    // Then 选店列表恢复，草稿中的启用状态没有被设置接口覆盖。
    expect(shopCalls).toBe(2)
    expect(wrapper.text()).toContain('表格店铺')
    expect(
      (
        wrapper.get('[data-testid="sync-settings-card"] input[aria-label="启用定时同步"]')
          .element as HTMLInputElement
      ).checked,
    ).toBe(true)
  })

  it('Given 选店接口暂时失败 When 点击面板刷新 Then 也会重试选店', async () => {
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
    wrapper = await mountPanel()
    expect(wrapper.text()).toContain('获取店铺失败')

    // When 点击面板内的店铺刷新。
    await wrapper.get('dialog button[aria-label="刷新"]').trigger('click')
    await flushPromises()

    // Then 选店列表随之恢复。
    expect(shopCalls).toBe(2)
    expect(wrapper.text()).toContain('表格店铺')
  })

  it('Given 已启用 When 修改开关但未保存 Then 关闭再打开面板仍保留选择且服务端无更新', async () => {
    const fetchMock = configureFetch({
      settings: { enabled: true, shopSids: [42], intervalMinutes: 30 },
    })
    wrapper = await mountPanel()
    await setScheduleEnabled(wrapper, false)
    wrapper.get<HTMLDialogElement>('dialog[open]').element.close()
    await flushPromises()
    await wrapper.get('[data-testid="orders-toolbar"] [aria-haspopup="dialog"]').trigger('click')
    await flushPromises()

    expect(wrapper.get<HTMLInputElement>('input[aria-label="启用定时同步"]').element.checked).toBe(
      false,
    )
    expect(
      wrapper.get<HTMLInputElement>('[data-testid="sync-shop-option"] input[value="42"]').element
        .checked,
    ).toBe(true)
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(false)
  })

  it('Given 未配置 Token When 加载设置 Then 提示配置后可选择店铺', async () => {
    // Given 私有 ERP Token 未配置。
    configureFetch({
      configured: false,
      settings: { enabled: false, shopSids: [], intervalMinutes: 30 },
    })

    // When 打开同步面板。
    wrapper = await mountPanel()

    // Then 设置可读取，但选店、开关和保存都要求先配置 Token。
    expect(wrapper.text()).toContain('配置私有 ERP AccessToken 后可选择店铺。')
    expect(
      wrapper
        .get('[data-testid="sync-settings-card"] input[aria-label="启用定时同步"]')
        .attributes('disabled'),
    ).toBeDefined()
    expect(findButton(wrapper, '保存')?.attributes('disabled')).toBeDefined()
  })
})
