import { flushPromises, mount, RouterLinkStub, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { ref, type Ref } from 'vue'
import * as session from '../../src/account/session'
import type { ErpHubState, ShopSyncStatus } from '../../src/composables/useErpHub'
import { adminAccount } from './adminAccount'
import { installNativeDialogShim } from './installNativeDialogShim'

type ShopRow = {
  sid: string
  name: string
  platform: string
  region: string | null
  memberId: string | null
  isTokenOk: boolean | null
  tokenError: string | null
  syncedAt: string | null
}

const hubMock = vi.hoisted(
  () =>
    ({}) as {
      state: Ref<ErpHubState>
      status: Ref<ShopSyncStatus | null>
      connect: ReturnType<typeof vi.fn>
      disconnect: ReturnType<typeof vi.fn>
      refreshStatus: ReturnType<typeof vi.fn>
      startShopSync: ReturnType<typeof vi.fn>
    },
)

vi.mock('../../src/composables/useErpHub', () => ({ useErpHub: () => hubMock }))

let fetchMock: ReturnType<typeof vi.fn>

beforeAll(installNativeDialogShim)

beforeEach(() => {
  hubMock.state = ref<ErpHubState>('connected')
  hubMock.status = ref<ShopSyncStatus | null>(null)
  hubMock.connect = vi.fn(async () => undefined)
  hubMock.disconnect = vi.fn(async () => undefined)
  hubMock.refreshStatus = vi.fn(async () => undefined)
  hubMock.startShopSync = vi.fn(async () => {
    const running = statusBody({ running: true, total: 131, checked: 0, version: 1 })
    hubMock.status.value = running
    return { accepted: true, status: running, error: null }
  })
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('授权店铺页', () => {
  let wrapper: VueWrapper | undefined

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
  })

  it('Given 本地已有店铺 When 打开授权页 Then 只读本地接口并渲染汇总、列表与最后同步', async () => {
    const syncedAt = new Date(Date.now() - 15 * 60_000).toISOString()
    const items = [
      shop({ isTokenOk: true, syncedAt }),
      shop({ sid: '6', name: 'Beta 店', platform: 'AliExpress', isTokenOk: false, syncedAt }),
    ]
    installFetch(
      defaultHandler(items, { valid: 1, invalid: 1, unknown: 0, lastSyncedAt: syncedAt }),
    )
    wrapper = await mountView()

    expect(hubMock.connect).toHaveBeenCalled()
    expect(wrapper.findAll('tbody tr')).toHaveLength(2)
    expect(wrapper.get('[data-testid="shop-summary-card"][data-static="true"]').text()).toContain(
      '15 分钟前',
    )
    expect(wrapper.text()).toContain('有效')
    expect(wrapper.text()).toContain('无效')
    expect(wrapper.text()).toContain('共 2 家店铺')
    expect(wrapper.findAll('[data-testid="shop-summary-card"]')).toHaveLength(4)
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/api/erp/shops?'))).toBe(true)
  })

  it('Given 系统配置受限 When 访问授权店铺 Then 通过业务可用性启用目录同步', async () => {
    const local = defaultHandler([shop()], { valid: 1, invalid: 0, unknown: 0, lastSyncedAt: null })
    installFetch((url) => {
      if (url === '/api/erp/config') return new Response(null, { status: 403 })
      if (url === '/api/erp/order-sync-availability')
        return jsonResponse({ configured: true, orderSyncEnabled: true })
      return local(url)
    })
    wrapper = await mountView()

    expect(wrapper.text()).toContain('上游连接：已配置')
    expect(firstButton(wrapper, '立即同步').attributes('disabled')).toBeUndefined()
    expect(fetchMock.mock.calls.map(([url]) => String(url))).not.toContain('/api/erp/config')
  })

  it('Given 点击无效汇总卡 When 查询 Then 后端按 status 过滤且汇总不随 status 变化', async () => {
    const items = [
      shop({ isTokenOk: true }),
      shop({ sid: '6', name: 'Beta 店', platform: 'AliExpress', isTokenOk: false }),
    ]
    installFetch(defaultHandler(items, { valid: 1, invalid: 1, unknown: 0, lastSyncedAt: null }))
    wrapper = await mountView()

    await wrapper.get('[aria-label^="无效"]').trigger('click')
    await flushPromises()

    expect(lastShopsUrl()).toContain('status=invalid')
    expect(lastShopsUrl()).toContain('page=1')
    expect(wrapper.get('[aria-label^="无效"]').attributes('aria-pressed')).toBe('true')
    expect(wrapper.get('[aria-label^="有效"]').text()).toContain('1')
    expect(wrapper.get('[aria-label^="无效"]').text()).toContain('1')
    expect(wrapper.findAll('tbody tr')).toHaveLength(1)

    await wrapper.get('[aria-label^="无效"]').trigger('click')
    await flushPromises()
    expect(lastShopsUrl()).not.toContain('status=')
  })

  it('Given 选择平台与输入关键词 When 查询 Then 参数进入本地 SQL 请求', async () => {
    installFetch(defaultHandler([shop()], { valid: 1, invalid: 0, unknown: 0, lastSyncedAt: null }))
    wrapper = await mountView()

    await wrapper.get('select[aria-label="平台筛选"]').setValue('AliExpress')
    await flushPromises()
    expect(lastShopsUrl()).toContain('platform=AliExpress')

    const search = wrapper.get('form[role="search"]')
    await search.get('input').setValue('Alpha')
    await search.trigger('submit')
    await flushPromises()
    expect(lastShopsUrl()).toContain('keyword=Alpha')
    expect(lastShopsUrl()).toContain('page=1')
  })

  it('Given 多页店铺 When 翻页与修改每页条数 Then 使用本地分页参数', async () => {
    const items = Array.from({ length: 45 }, (_, index) =>
      shop({ sid: String(index + 1), name: `分页店铺 ${index + 1}` }),
    )
    installFetch(defaultHandler(items, { valid: 45, invalid: 0, unknown: 0, lastSyncedAt: null }))
    wrapper = await mountView()

    expect(wrapper.findAll('tbody tr')).toHaveLength(20)
    expect(wrapper.get('[aria-current="page"]').text()).toBe('1')

    await wrapper.get('button[aria-label="下一页"]').trigger('click')
    await flushPromises()
    expect(lastShopsUrl()).toContain('page=2')
    expect(wrapper.get('[aria-current="page"]').text()).toBe('2')
    expect(wrapper.findAll('tbody tr')[0].text()).toContain('分页店铺 21')

    await wrapper.get('select[aria-label="每页条数"]').setValue('50')
    await flushPromises()
    expect(lastShopsUrl()).toContain('pageSize=50')
    expect(lastShopsUrl()).toContain('page=1')
    expect(wrapper.findAll('tbody tr')).toHaveLength(45)
  })

  it('Given 点击详情 When 打开弹窗 Then 展示可用字段且深链在新标签打开', async () => {
    const open = vi.spyOn(window, 'open').mockImplementation(() => null)
    installFetch(
      defaultHandler([shop({ memberId: 'member-42', syncedAt: new Date().toISOString() })], {
        valid: 1,
        invalid: 0,
        unknown: 0,
        lastSyncedAt: null,
      }),
    )
    wrapper = await mountView()

    await firstButton(wrapper, '详情').trigger('click')
    await flushPromises()

    const dialog = wrapper.get('#shopDetailLayer')
    expect(dialog.attributes('open')).toBeDefined()
    expect(dialog.text()).toContain('member-42')
    expect(dialog.text()).toContain('wxw ERP（OpenERP 只读）')
    expect(dialog.text()).toContain('授权范围、有效期、授权历史由 wxw ERP 管理')
    expect(dialog.text()).not.toContain('授权历史记录')

    await wrapper.get('#shopAuthorize').trigger('click')
    expect(open).toHaveBeenCalledWith(
      'https://v3.wxwerp.com/system/authorize',
      '_blank',
      'noopener,noreferrer',
    )

    await wrapper.get('#closeShopDetail').trigger('click')
    await flushPromises()
    expect(wrapper.get('#shopDetailLayer').attributes('open')).toBeUndefined()
  })

  it('Given 实时连接正常 When 点击立即同步 Then 显示 Hub 进度并在完成后刷新列表', async () => {
    const items = [shop()]
    installFetch(defaultHandler(items, { valid: 1, invalid: 0, unknown: 0, lastSyncedAt: null }))
    wrapper = await mountView()
    const initialShopsCalls = shopsCalls()

    await firstButton(wrapper, '立即同步').trigger('click')
    await flushPromises()
    expect(hubMock.startShopSync).toHaveBeenCalledTimes(1)
    expect(firstButton(wrapper, '同步中 0/131').attributes('disabled')).toBeDefined()

    hubMock.status.value = statusBody({
      running: true,
      total: 131,
      checked: 10,
      valid: 10,
      version: 1,
    })
    await flushPromises()
    expect(firstButton(wrapper, '同步中 10/131')).toBeTruthy()

    hubMock.status.value = statusBody({
      running: false,
      total: 1,
      checked: 1,
      valid: 1,
      version: 2,
    })
    await flushPromises()
    expect(firstButton(wrapper, '立即同步')).toBeTruthy()
    expect(shopsCalls()).toBeGreaterThan(initialShopsCalls)
  })

  it('Given 实时连接断开 When 查看 Then 列表仍可读且同步按钮禁用', async () => {
    hubMock.state = ref<ErpHubState>('disconnected')
    installFetch(defaultHandler([shop()], { valid: 1, invalid: 0, unknown: 0, lastSyncedAt: null }))
    wrapper = await mountView()

    expect(wrapper.findAll('tbody tr')).toHaveLength(1)
    expect(wrapper.text()).toContain('实时连接已断开')
    expect(firstButton(wrapper, '立即同步').attributes('disabled')).toBeDefined()
  })

  it('Given 普通用户且未配置 Token When 打开 Then 提示联系管理员且禁用同步但保留本地数据', async () => {
    vi.spyOn(session, 'account', 'get').mockReturnValue(
      ref({ ...adminAccount, role: 'User', username: 'employee' }),
    )
    installFetch((url) => {
      if (url === '/api/erp/order-sync-availability')
        return jsonResponse({
          configured: false,
          orderSyncEnabled: false,
        })
      return defaultHandler([shop()], { valid: 1, invalid: 0, unknown: 0, lastSyncedAt: null })(url)
    })
    wrapper = await mountView()

    expect(wrapper.text()).toContain('尚未配置私有 ERP AccessToken')
    expect(wrapper.text()).toContain('请联系管理员配置 ERP 接入')
    expect(wrapper.text()).not.toContain('前往 ERP 接入')
    expect(wrapper.findAll('tbody tr')).toHaveLength(1)
    expect(firstButton(wrapper, '立即同步').attributes('disabled')).toBeDefined()
  })

  it('Given 本地无店铺 When 打开 Then 显示可操作的同步空态', async () => {
    installFetch(defaultHandler([], { valid: 0, invalid: 0, unknown: 0, lastSyncedAt: null }))
    wrapper = await mountView()

    expect(wrapper.text()).toContain('本地暂无店铺数据')
    expect(firstButton(wrapper, '立即同步')).toBeTruthy()
  })

  it('Given 本地接口失败 When 打开 Then 显示错误详情且可重试', async () => {
    installFetch((url) => {
      if (url === '/api/erp/order-sync-availability') return configResponse()
      if (url === '/api/erp/platforms') return platformsResponse()
      if (url === '/api/erp/shop-sync') return jsonResponse(statusBody())
      return jsonResponse({ title: '本地店铺数据暂时不可用', detail: '数据库连接失败。' }, 503)
    })
    wrapper = await mountView()

    const notice = wrapper.get('[role="alert"]')
    expect(notice.text()).toContain('数据库连接失败。')
    expect(firstButton(wrapper, '重试')).toBeTruthy()
  })

  it('Given 授权页 When 查看 Then 不再出现订单同步或定时同步入口', async () => {
    installFetch(defaultHandler([shop()], { valid: 1, invalid: 0, unknown: 0, lastSyncedAt: null }))
    wrapper = await mountView()

    expect(wrapper.text()).not.toContain('同步历史订单')
    expect(wrapper.text()).not.toContain('定时同步')
    expect(wrapper.text()).not.toContain('订单同步')
  })

  it('Given 断线期间同步完成 When 重连收到新的完成时间 Then 刷新本地列表', async () => {
    const initialCompletedAt = '2026-11-18T05:20:00Z'
    const nextCompletedAt = '2026-11-18T05:30:00Z'
    const base = defaultHandler([shop()], {
      valid: 1,
      invalid: 0,
      unknown: 0,
      lastSyncedAt: initialCompletedAt,
      syncVersion: 1,
    })
    installFetch(base)
    wrapper = await mountView()
    const before = shopsCalls()

    hubMock.status.value = statusBody({
      completedAt: nextCompletedAt,
      total: 1,
      checked: 1,
      valid: 1,
      version: 2,
    })
    await flushPromises()
    await flushPromises()

    expect(shopsCalls()).toBeGreaterThan(before)
  })

  it('Given 重连收到的完成时间未变化 When 应用状态 Then 不重复刷新本地列表', async () => {
    const completedAt = '2026-11-18T05:20:00Z'
    installFetch(
      defaultHandler([shop()], {
        valid: 1,
        invalid: 0,
        unknown: 0,
        lastSyncedAt: completedAt,
        syncVersion: 1,
      }),
    )
    wrapper = await mountView()
    const before = shopsCalls()

    hubMock.status.value = statusBody({ completedAt, version: 1 })
    await flushPromises()

    expect(shopsCalls()).toBe(before)
  })

  it('Given 初次加载与状态读取竞态 When 目录版本落后 Then 按完成版本刷新一次', async () => {
    let calls = 0
    const first = defaultHandler([shop()], {
      valid: 1,
      invalid: 0,
      unknown: 0,
      lastSyncedAt: '2026-11-18T05:20:00Z',
      syncVersion: 1,
    })
    const settled = defaultHandler([shop()], {
      valid: 1,
      invalid: 0,
      unknown: 0,
      lastSyncedAt: '2026-11-18T05:30:00Z',
      syncVersion: 2,
    })
    installFetch((url) => {
      if (!url.startsWith('/api/erp/shops')) return first(url)
      calls += 1
      return calls === 1 ? first(url) : settled(url)
    })
    hubMock.refreshStatus = vi.fn(async () => {
      hubMock.status.value = statusBody({
        completedAt: '2026-11-18T05:30:00Z',
        total: 1,
        checked: 1,
        valid: 1,
        version: 2,
      })
    })

    wrapper = await mountView()

    expect(calls).toBe(2)
    expect(wrapper.findAll('tbody tr')).toHaveLength(1)
  })
})

async function mountView(): Promise<VueWrapper> {
  const { default: ShopsView } = await import('../../src/views/ShopsView.vue')
  const view = mount(ShopsView, { global: { stubs: { RouterLink: RouterLinkStub } } })
  await flushPromises()
  await flushPromises()
  return view
}

function shop(overrides: Partial<ShopRow> = {}): ShopRow {
  return {
    sid: '42',
    name: 'Alpha 店',
    platform: 'TemuPop',
    region: 'US',
    memberId: null,
    isTokenOk: true,
    tokenError: null,
    syncedAt: new Date().toISOString(),
    ...overrides,
  }
}

function statusBody(overrides: Partial<ShopSyncStatus> = {}): ShopSyncStatus {
  return {
    running: false,
    startedAt: null,
    completedAt: null,
    total: 0,
    checked: 0,
    valid: 0,
    invalid: 0,
    unknown: 0,
    error: null,
    version: 0,
    ...overrides,
  }
}

function configResponse(configured = true): Response {
  return jsonResponse({ configured, baseUrl: 'https://erp.test', orderSyncEnabled: false })
}

function platformsResponse(): Response {
  return jsonResponse({
    items: [
      {
        name: 'TemuPop',
        displayName: 'Temu半托管',
        icon: 'icon-temupop',
        isProduction: true,
        shopCount: 2,
      },
      {
        name: 'AliExpress',
        displayName: '速卖通',
        icon: 'icon-aliexpress',
        isProduction: true,
        shopCount: 1,
      },
    ],
  })
}

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function defaultHandler(
  items: ShopRow[],
  summary: {
    valid: number
    invalid: number
    unknown: number
    lastSyncedAt: string | null
    syncVersion?: number
  },
) {
  const summaryWithVersion = { ...summary, syncVersion: summary.syncVersion ?? 0 }
  return (url: string): Response => {
    if (url === '/api/erp/order-sync-availability') return configResponse()
    if (url === '/api/erp/platforms') return platformsResponse()
    if (url === '/api/erp/shop-sync') return jsonResponse(statusBody())
    if (!url.startsWith('/api/erp/shops')) throw new Error(`意外的请求：${url}`)

    const params = new URL(url, 'https://local.test').searchParams
    const status = params.get('status')
    const platform = params.get('platform')
    const keyword = params.get('keyword')
    const page = Number(params.get('page') ?? 1)
    const pageSize = Number(params.get('pageSize') ?? 20)
    const filtered = items.filter(
      (item) =>
        (!status ||
          (status === 'valid' && item.isTokenOk === true) ||
          (status === 'invalid' && item.isTokenOk === false) ||
          (status === 'unknown' && item.isTokenOk === null)) &&
        (!platform || item.platform === platform) &&
        (!keyword || item.name.includes(keyword) || item.sid === keyword),
    )
    const pageCount = filtered.length === 0 ? 0 : Math.ceil(filtered.length / pageSize)
    const start = (Math.min(page, Math.max(pageCount, 1)) - 1) * pageSize
    return jsonResponse({
      items: filtered.slice(start, start + pageSize),
      page: Math.min(page, Math.max(pageCount, 1)),
      pageSize,
      totalCount: filtered.length,
      pageCount,
      summary: summaryWithVersion,
    })
  }
}

function installFetch(handler: (url: string) => Response): void {
  fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url =
      typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url
    return handler(url)
  })
  vi.stubGlobal('fetch', fetchMock)
}

function lastShopsUrl(): string {
  const calls = fetchMock.mock.calls
    .map(([url]) => String(url))
    .filter((url) => url.startsWith('/api/erp/shops'))
  return calls.at(-1) ?? ''
}

function shopsCalls(): number {
  return fetchMock.mock.calls
    .map(([url]) => String(url))
    .filter((url) => url.startsWith('/api/erp/shops')).length
}

function firstButton(wrapper: VueWrapper, text: string) {
  const button = wrapper.findAll('button').find((item) => item.text().trim() === text)
  if (!button) throw new Error(`找不到按钮：${text}`)
  return button
}
