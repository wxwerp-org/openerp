import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import DashboardView from '../../src/views/DashboardView.vue'
import { account, type Account } from '../../src/account/session'
import {
  onboardingRibbonCollapsed,
  onboardingSummaryDismissed,
} from '../../src/workspace/onboardingState'
import { adminAccount } from './adminAccount'
import {
  dataTrendFixture,
  degradedTrendFixture,
  emptyTrendFixture,
  salesFixture,
  trendFixture,
} from '../reportFixtures'

vi.mock('../../src/account/session', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../src/account/session')>()
  const { ref } = await import('vue')
  return { ...actual, account: ref(null) }
})

type HubMock = {
  state: { value: string }
  status: { value: unknown }
  orderSyncTasks: { value: Record<number, unknown> }
  connect: ReturnType<typeof vi.fn>
  disconnect: ReturnType<typeof vi.fn>
  reconnect: ReturnType<typeof vi.fn>
  refreshStatus: ReturnType<typeof vi.fn>
  startShopSync: ReturnType<typeof vi.fn>
  startOrderSync: ReturnType<typeof vi.fn>
  startAllOrderSync: ReturnType<typeof vi.fn>
}

const hubState = vi.hoisted(() => ({ current: null as unknown }))

function hubMock(): HubMock {
  return hubState.current as HubMock
}

vi.mock('../../src/composables/useErpHub', async () => {
  const { ref } = await import('vue')
  hubState.current = {
    state: ref('connected'),
    status: ref(null),
    orderSyncTasks: ref({}),
    connect: vi.fn(async () => {}),
    disconnect: vi.fn(async () => {}),
    reconnect: vi.fn(async () => {}),
    refreshStatus: vi.fn(async () => {}),
    startShopSync: vi.fn(async () => ({ accepted: true, status: null, error: null })),
    startOrderSync: vi.fn(async () => ({ accepted: true, status: null, error: null })),
    startAllOrderSync: vi.fn(async () => ({
      accepted: true,
      startedCount: 0,
      busyCount: 0,
      error: null,
    })),
  }
  return { useErpHub: () => hubState.current }
})

function syncTask(status: string, overrides: Record<string, unknown> = {}) {
  return {
    epoch: 'epoch-1',
    version: 1,
    id: 'job-1',
    sid: 30,
    status,
    stage: status === 'running' ? 'details' : 'done',
    total: 10,
    read: 0,
    processed: 0,
    added: 0,
    updated: 0,
    unchanged: 0,
    skipped: 0,
    skipReasons: {},
    error: null,
    ...overrides,
  }
}

const reportPayload = (orderCount = 10, itemQuantity = 42) => ({
  generatedAtUtc: '2026-11-20T07:47:00Z',
  summary: { orderCount, itemQuantity },
  sales: orderCount > 0 ? salesFixture(371.12, 'USD', 4, 0) : salesFixture(0, 'USD', 0, 0),
  trend: orderCount > 0 ? dataTrendFixture() : emptyTrendFixture(),
  platforms: ['TemuPop'],
  shops: [],
  groups:
    orderCount > 0
      ? [
          {
            shop: {
              source: 'wxw-erp',
              platform: 'TemuPop',
              shopId: '30',
              key: 'wxw-erp/TemuPop/30',
              label: 'wxw-erp/TemuPop/30',
            },
            orderCount: 6,
            itemQuantity: 30,
          },
          {
            shop: {
              source: 'wxw-erp',
              platform: 'TemuPop',
              shopId: '31',
              key: 'wxw-erp/TemuPop/31',
              label: 'wxw-erp/TemuPop/31',
            },
            orderCount: 4,
            itemQuantity: 12,
          },
        ]
      : [],
  orders: [],
  page: 1,
  pageSize: 1,
  pageCount: 1,
})

function requestedTrendDays(url: URL): number {
  const start = url.searchParams.get('trendStart')!
  const end = url.searchParams.get('trendEnd')!
  return (
    Math.round((Date.parse(`${end}T00:00:00Z`) - Date.parse(`${start}T00:00:00Z`)) / 86400000) + 1
  )
}

/** 按请求区间生成逐日走势夹具，使响应与所选周期自洽。 */
function trendForRequest(url: URL, orderCounts: number[] = [1]) {
  return trendFixture({
    days: requestedTrendDays(url),
    start: url.searchParams.get('trendStart')!,
    orderCounts,
  })
}

function localDate(offsetDays = 0): string {
  const date = new Date()
  date.setDate(date.getDate() + offsetDays)
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

const directoryPayload = (items = 2, lastSyncedAt: string | null = '2026-11-20T05:40:00Z') => ({
  items: Array.from({ length: items }, (_, index) => ({
    sid: String(30 + index),
    name: index === 0 ? '三十店' : `店铺${30 + index}`,
    platform: 'TemuPop',
    region: 'US',
    memberId: null,
    isTokenOk: true,
    tokenError: null,
    syncedAt: lastSyncedAt,
  })),
  summary: { valid: items, invalid: 0, unknown: 0, lastSyncedAt, syncVersion: 2 },
  page: 1,
  pageSize: 100,
  totalCount: items,
  pageCount: 1,
})

type ApiHandlers = {
  report?: (url: URL) => Response
  directory?: () => Response
  config?: () => Response
  availability?: () => Response
}

function stubApi(handlers: ApiHandlers) {
  const stub = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost')
    if (url.pathname === '/api/reports/order-snapshot')
      return handlers.report?.(url) ?? Response.json(reportPayload())
    if (url.pathname === '/api/erp/shops')
      return handlers.directory?.() ?? Response.json(directoryPayload())
    if (url.pathname === '/api/erp/config')
      return (
        handlers.config?.() ??
        Response.json({ configured: true, baseUrl: 'https://erp.example', orderSyncEnabled: true })
      )
    if (url.pathname === '/api/erp/order-sync-availability')
      return (
        handlers.availability?.() ?? Response.json({ configured: true, orderSyncEnabled: true })
      )
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', stub)
  return stub
}

function setAccount(value: Account) {
  ;(account as unknown as { value: Account }).value = value
}

function createRouterFor(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/reports/orders', component: { template: '<div />' } },
      { path: '/shops', component: { template: '<div />' } },
      { path: '/settings', component: { template: '<div />' } },
    ],
  })
}

async function mountDashboard(
  attachToBody = false,
): Promise<{ wrapper: VueWrapper; router: Router }> {
  const router = createRouterFor()
  await router.push('/')
  await router.isReady()
  const wrapper = mount(DashboardView, {
    attachTo: attachToBody ? document.body : undefined,
    global: { plugins: [router], stubs: { OrderSyncDialog: true } },
  })
  await flushPromises()
  return { wrapper, router }
}

describe('工作台', () => {
  let wrapper: VueWrapper | undefined

  beforeEach(() => {
    setAccount(adminAccount)
    onboardingRibbonCollapsed.value = false
    onboardingSummaryDismissed.value = false
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    vi.unstubAllGlobals()
    vi.clearAllMocks()
  })

  it('Given 报表有订单 When 管理员打开工作台 Then 数字卡、平台分布、Top5 与三张状态卡显示真实数据', async () => {
    stubApi({})
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    expect(wrapper.get('[data-testid="metric-orders"]').text()).toContain('10')
    expect(wrapper.get('[data-testid="metric-items"]').text()).toContain('42')
    expect(wrapper.get('[data-testid="metric-sales"]').text()).toContain('US$371.12')
    expect(wrapper.find('[data-testid="workspace-trend-card"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="metric-last-sync"]').text()).toContain('2026-11-20')

    expect(wrapper.get('[data-testid="workspace-platform-card"]').text()).toContain('Temu 半托管')
    const topRow = wrapper.get('[data-shop-key="wxw-erp/TemuPop/30"]')
    expect(topRow.text()).toContain('三十店')
    expect(topRow.text()).toContain('30')

    expect(wrapper.find('[data-testid="workspace-health-card"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="workspace-sync-card"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="workspace-system-card"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('11-12 15:47')
    expect(wrapper.text()).not.toContain('原型演示')
  })

  it('Given 平台与店铺卡片 When 点击 Then 下钻报表使用平台标识与完整店铺键', async () => {
    stubApi({})
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    await wrapper.get('[data-platform="TemuPop"]').trigger('click')
    await flushPromises()
    expect(mounted.router.currentRoute.value.path).toBe('/reports/orders')
    expect(mounted.router.currentRoute.value.query.platform).toBe('TemuPop')

    await mounted.router.push('/')
    await flushPromises()
    await wrapper.get('[data-shop-key="wxw-erp/TemuPop/30"]').trigger('click')
    await flushPromises()
    expect(mounted.router.currentRoute.value.query.shops).toBe('wxw-erp/TemuPop/30')
  })

  it('Given 空库管理员 When 打开工作台 Then 显示引导带且第一步为当前可执行步', async () => {
    stubApi({
      report: () => Response.json(reportPayload(0, 0)),
      directory: () => Response.json(directoryPayload(0, null)),
      config: () => Response.json({ configured: false, baseUrl: null, orderSyncEnabled: true }),
    })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    expect(wrapper.find('[data-testid="workspace-onboarding"]').exists()).toBe(true)
    expect(wrapper.get('[data-step="configure"]').attributes('data-step-executable')).toBe('true')
    expect(wrapper.get('[data-step="configure"]').attributes('data-step-done')).toBe('false')
    expect(wrapper.get('[data-step="sync-orders"]').attributes('data-step-executable')).toBe(
      'false',
    )
    expect(wrapper.text()).toContain('初始化')
  })

  it('Given 零店员工 When 打开工作台 Then 引导授权和分配且不出现管理卡片', async () => {
    setAccount({ ...adminAccount, role: 'User', username: 'employee' })
    const stub = stubApi({
      report: () => Response.json(reportPayload(0, 0)),
      directory: () => Response.json({ items: [] }),
    })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    expect(wrapper.find('[data-testid="employee-empty-hint"]').exists()).toBe(true)
    const hint = wrapper.get('[data-testid="employee-empty-hint"]')
    expect(hint.text()).toContain('尚无可见店铺')
    expect(hint.text()).toContain('请联系管理员同步目录并分配')
    expect(hint.get('a').attributes('href')).toBe('/shops')
    expect(wrapper.find('[data-testid="workspace-onboarding"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="workspace-health-card"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="workspace-system-card"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="workspace-sync-card"]').exists()).toBe(true)

    const paths = stub.mock.calls.map(
      ([input]) => new URL(String(input), 'http://localhost').pathname,
    )
    expect(paths).toContain('/api/erp/shops')
    expect(paths).not.toContain('/api/erp/config')
    expect(paths).not.toContain('/api/erp/shop-sync')
  })

  it('Given 首次报表失败 When 打开工作台 Then 整页错误态与重试', async () => {
    stubApi({
      report: () => Response.json({ title: '报表数据暂不可用' }, { status: 503 }),
    })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    expect(wrapper.text()).toContain('工作台数据加载失败')
    expect(wrapper.text()).toContain('报表数据暂不可用')
    expect(wrapper.find('[data-testid="dashboard-skeleton"]').exists()).toBe(false)
  })

  it('Given 已有快照 When 手动刷新失败 Then 保留数据并标注上次结果', async () => {
    let failing = false
    stubApi({
      report: () =>
        failing
          ? Response.json({ title: '数据库暂不可用' }, { status: 503 })
          : Response.json(reportPayload()),
    })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    failing = true
    await wrapper.get('[data-testid="workspace-refresh"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="metric-orders"]').text()).toContain('10')
    expect(wrapper.find('[data-testid="report-stale"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('工作台数据加载失败')
  })

  it('Given 空态引导带 When 点击收起 Then 本次会话保留收起状态并显示进度摘要', async () => {
    stubApi({
      report: () => Response.json(reportPayload(0, 0)),
      directory: () => Response.json(directoryPayload(0, null)),
      config: () => Response.json({ configured: true, baseUrl: null, orderSyncEnabled: true }),
    })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    await wrapper.get('[data-testid="onboarding-toggle"]').trigger('click')
    expect(wrapper.get('[data-testid="onboarding-progress"]').text()).toContain('初始化进度')

    wrapper.unmount()
    const second = await mountDashboard()
    wrapper = second.wrapper
    expect(second.wrapper.find('[data-step="configure"]').exists()).toBe(false)
  })

  it('Given 四步全部完成 When 进入有数据态 Then 保留一行完成摘要且可收起', async () => {
    stubApi({})
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    expect(wrapper.find('[data-testid="onboarding-completed"]').exists()).toBe(true)
    await wrapper.get('[data-testid="onboarding-completed-dismiss"]').trigger('click')
    expect(wrapper.find('[data-testid="onboarding-completed"]').exists()).toBe(false)

    wrapper.unmount()
    const second = await mountDashboard()
    wrapper = second.wrapper
    expect(second.wrapper.find('[data-testid="onboarding-completed"]').exists()).toBe(false)
  })

  it('Given 在线漏送终态 When 点击刷新 Then 重建实时连接并重新取数', async () => {
    stubApi({})
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper
    hubMock().reconnect.mockClear()

    await wrapper.get('[data-testid="workspace-refresh"]').trigger('click')
    await flushPromises()

    expect(hubMock().reconnect).toHaveBeenCalledTimes(1)
  })

  it('Given 目录同步启动被拒 When 再次点击 Then 显示原因并允许重试', async () => {
    stubApi({
      report: () => Response.json(reportPayload(0, 0)),
      directory: () => Response.json(directoryPayload(0, null)),
      config: () => Response.json({ configured: true, baseUrl: null, orderSyncEnabled: true }),
    })
    hubMock()
      .startShopSync.mockResolvedValueOnce({
        accepted: false,
        status: null,
        error: '实时连接已断开，请刷新页面后重试。',
      })
      .mockResolvedValueOnce({ accepted: true, status: null, error: null })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '去同步')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('实时连接已断开，请刷新页面后重试。')

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '去同步')!
      .trigger('click')
    await flushPromises()
    expect(hubMock().startShopSync).toHaveBeenCalledTimes(2)
  })

  it('Given 同步任务状态变化 When 开始与失败 Then 各自刷新报表，进度更新不刷新', async () => {
    const stub = stubApi({})
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper
    const reportCalls = () =>
      stub.mock.calls.filter(([input]) => String(input).startsWith('/api/reports/order-snapshot?'))
        .length
    const before = reportCalls()

    hubMock().orderSyncTasks.value = { 30: syncTask('running') }
    await flushPromises()
    expect(reportCalls()).toBe(before + 1)

    hubMock().orderSyncTasks.value = { 30: syncTask('running', { version: 2, processed: 5 }) }
    await flushPromises()
    expect(reportCalls()).toBe(before + 1)

    hubMock().orderSyncTasks.value = { 30: syncTask('failed', { version: 3 }) }
    await flushPromises()
    expect(reportCalls()).toBe(before + 2)
  })

  it('Given 走势卡 When 切换近30天 Then 重新请求报表并把响应周期渲染为30天', async () => {
    const stub = stubApi({
      report: (url) =>
        Response.json({
          ...reportPayload(),
          trend: requestedTrendDays(url) === 30 ? trendForRequest(url) : dataTrendFixture(),
        }),
    })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    const trendCard = wrapper.get('[data-testid="workspace-trend-card"]')
    expect(trendCard.text()).toContain('近 7 天')

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '近 30 天')!
      .trigger('click')
    await flushPromises()

    const calls = stub.mock.calls
      .filter(([input]) => String(input).startsWith('/api/reports/order-snapshot?'))
      .map(([input]) => new URL(String(input), 'http://localhost'))
    expect(calls.at(-1)?.searchParams.get('trendStart')).toBe(localDate(-29))
    expect(calls.at(-1)?.searchParams.get('trendEnd')).toBe(localDate())
    expect(wrapper.get('[data-testid="workspace-trend-card"]').text()).toContain('近 30 天')
  })

  it('Given 30 天请求失败 When 再次点击近 30 天 Then 允许重试并最终切换到 30 天', async () => {
    let fail = false
    stubApi({
      report: (url) => {
        const days = requestedTrendDays(url)
        if (days === 30 && fail)
          return Response.json({ title: '报表数据暂不可用' }, { status: 503 })
        return Response.json({
          ...reportPayload(),
          trend: days === 30 ? trendForRequest(url) : dataTrendFixture(),
        })
      },
    })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    fail = true
    const thirty = () => wrapper!.findAll('button').find((button) => button.text() === '近 30 天')!
    await thirty().trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="report-stale"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="trend-footnote"]').text()).toContain(
      '近 7 天按订单创建日期分桶',
    )
    expect(thirty().attributes('aria-pressed')).toBe('false')

    fail = false
    await thirty().trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="trend-footnote"]').text()).toContain(
      '近 30 天按订单创建日期分桶',
    )
    expect(thirty().attributes('aria-pressed')).toBe('true')
  })

  it('Given 周期请求在途 When 快速切回已展示周期 Then 迟到响应按版本失效不覆盖当前周期', async () => {
    const pending: Array<{ url: URL; resolve: (body: unknown) => void }> = []
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = new URL(String(input), 'http://localhost')
        if (url.pathname === '/api/reports/order-snapshot') {
          return new Promise<Response>((resolve) => {
            pending.push({
              url,
              resolve: (body) => resolve(Response.json(body)),
            })
          })
        }
        if (url.pathname === '/api/erp/shops')
          return Promise.resolve(Response.json(directoryPayload()))
        if (url.pathname === '/api/erp/config')
          return Promise.resolve(Response.json({ configured: true, orderSyncEnabled: true }))
        if (url.pathname === '/api/erp/order-sync-availability')
          return Promise.resolve(Response.json({ configured: true, orderSyncEnabled: true }))
        return Promise.resolve(new Response(null, { status: 404 }))
      }),
    )
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper
    const reportBody = (url: URL) => ({ ...reportPayload(), trend: trendForRequest(url) })

    // 先完成初始近 7 天请求，建立「已展示周期」。
    expect(pending).toHaveLength(1)
    pending[0]!.resolve(reportBody(pending[0]!.url))
    await flushPromises()
    expect(wrapper.get('[data-testid="trend-footnote"]').text()).toContain(
      '近 7 天按订单创建日期分桶',
    )

    // 切到本季度（挂起），再切回已展示的近 7 天（应发起新请求而不是吞掉点击）。
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '本季度')!
      .trigger('click')
    await flushPromises()
    expect(pending).toHaveLength(2)
    const seven = () => wrapper!.findAll('button').find((button) => button.text() === '近 7 天')!
    await seven().trigger('click')
    await flushPromises()
    expect(pending).toHaveLength(3)

    // 近 7 天先返回，随后本季度迟到，迟到的本季度响应必须被丢弃。
    pending[2]!.resolve(reportBody(pending[2]!.url))
    await flushPromises()
    pending[1]!.resolve(reportBody(pending[1]!.url))
    await flushPromises()
    expect(wrapper.get('[data-testid="trend-footnote"]').text()).toContain(
      '近 7 天按订单创建日期分桶',
    )
    expect(seven().attributes('aria-pressed')).toBe('true')
    expect(
      wrapper
        .findAll('button')
        .find((button) => button.text() === '本季度')!
        .attributes('aria-pressed'),
    ).toBe('false')
  })

  it('Given 周期响应迟到 When 自定义面板正在编辑 Then 不关闭面板也不丢失草稿', async () => {
    const pending: Array<{ url: URL; resolve: (body: unknown) => void }> = []
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = new URL(String(input), 'http://localhost')
        if (url.pathname === '/api/reports/order-snapshot') {
          return new Promise<Response>((resolve) => {
            pending.push({ url, resolve: (body) => resolve(Response.json(body)) })
          })
        }
        if (url.pathname === '/api/erp/shops')
          return Promise.resolve(Response.json(directoryPayload()))
        if (url.pathname === '/api/erp/config')
          return Promise.resolve(Response.json({ configured: true, orderSyncEnabled: true }))
        if (url.pathname === '/api/erp/order-sync-availability')
          return Promise.resolve(Response.json({ configured: true, orderSyncEnabled: true }))
        return Promise.resolve(new Response(null, { status: 404 }))
      }),
    )
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper
    const reportBody = (url: URL) => ({ ...reportPayload(), trend: trendForRequest(url) })
    pending[0]!.resolve(reportBody(pending[0]!.url))
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '本季度')!
      .trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="trend-custom-toggle"]').trigger('click')
    await wrapper.get('[data-testid="trend-custom-start"]').setValue('2025-06-01')

    pending[1]!.resolve(reportBody(pending[1]!.url))
    await flushPromises()
    expect(wrapper.find('[data-testid="trend-custom-panel"]').exists()).toBe(true)
    expect(
      (wrapper.get('[data-testid="trend-custom-start"]').element as HTMLInputElement).value,
    ).toBe('2025-06-01')
  })

  it('Given 自定义面板 When 应用或取消 Then 焦点回到自定义按钮', async () => {
    stubApi({})
    const mounted = await mountDashboard(true)
    wrapper = mounted.wrapper
    const toggle = wrapper.get('[data-testid="trend-custom-toggle"]')
    ;(toggle.element as HTMLElement).focus()

    await toggle.trigger('click')
    await wrapper.get('[data-testid="trend-custom-apply"]').trigger('click')
    await flushPromises()
    expect(document.activeElement).toBe(toggle.element)

    ;(toggle.element as HTMLElement).focus()
    await toggle.trigger('click')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '取消')!
      .trigger('click')
    await flushPromises()
    expect(document.activeElement).toBe(toggle.element)
  })

  it('Given 走势图获得焦点 When 按方向键 Then 显示逐日 tooltip 且 Escape 关闭', async () => {
    stubApi({})
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    const chart = wrapper.get('[data-testid="workspace-trend-chart"]')
    expect(chart.attributes('tabindex')).toBe('0')
    await chart.trigger('focus')
    expect(wrapper.find('[data-testid="trend-tooltip"]').exists()).toBe(true)
    await chart.trigger('keydown', { key: 'ArrowLeft' })
    expect(wrapper.find('[data-testid="trend-tooltip"]').exists()).toBe(true)
    await chart.trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('[data-testid="trend-tooltip"]').exists()).toBe(false)
  })

  it('Given 金额权限缺失 When 打开工作台 Then 走势卡降级为订单数并提示无金额权限', async () => {
    stubApi({
      report: () =>
        Response.json({
          ...reportPayload(),
          sales: salesFixture(null, null, 0, 10),
          trend: degradedTrendFixture(),
        }),
    })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    expect(wrapper.get('[data-testid="metric-sales"]').text()).toContain('暂无可用金额')
    expect(wrapper.get('[data-testid="trend-degraded"]').text()).toContain('暂无可用金额')
    expect(wrapper.find('[data-testid="trend-sales-comparison"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="trend-footnote"]').text()).toContain('重新同步后可补齐')
  })

  it('Given 走势窗口内无订单 When 打开工作台 Then 走势卡显示空态而不是 0 折线', async () => {
    stubApi({
      report: () => Response.json(reportPayload(0, 0)),
      directory: () => Response.json(directoryPayload(0, null)),
      config: () => Response.json({ configured: true, baseUrl: null, orderSyncEnabled: true }),
    })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    const trendCard = wrapper.get('[data-testid="workspace-trend-card"]')
    expect(trendCard.text()).toContain('暂无订单数据')
    expect(wrapper.find('[data-testid="workspace-trend-chart"]').exists()).toBe(false)
  })

  it('Given 报表未返回 trend 契约 When 解析 Then 抛出可展示错误而不是渲染半截图表', async () => {
    stubApi({
      report: () => {
        const payload: Record<string, unknown> = { ...reportPayload() }
        delete payload.trend
        return Response.json(payload)
      },
    })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    expect(wrapper.text()).toContain('工作台数据加载失败')
  })
  it('Given 走势卡 When 点击本季度 Then 请求季度首日到今天并展示本季度口径', async () => {
    const stub = stubApi({
      report: (url) => Response.json({ ...reportPayload(), trend: trendForRequest(url, [2]) }),
    })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper
    const now = new Date()
    const quarterStart = `${now.getFullYear()}-${String(Math.floor(now.getMonth() / 3) * 3 + 1).padStart(2, '0')}-01`

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '本季度')!
      .trigger('click')
    await flushPromises()

    const calls = stub.mock.calls
      .filter(([input]) => String(input).startsWith('/api/reports/order-snapshot?'))
      .map(([input]) => new URL(String(input), 'http://localhost'))
    expect(calls.at(-1)?.searchParams.get('trendStart')).toBe(quarterStart)
    expect(calls.at(-1)?.searchParams.get('trendEnd')).toBe(localDate())
    expect(wrapper.get('[data-testid="trend-footnote"]').text()).toContain(
      `本季度（${quarterStart} 至 ${localDate()}）`,
    )
  })

  it('Given 走势卡 When 点击本年 Then 请求自然年首日到今天并展示本年口径', async () => {
    const stub = stubApi({
      report: (url) => Response.json({ ...reportPayload(), trend: trendForRequest(url, [1]) }),
    })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '本年')!
      .trigger('click')
    await flushPromises()

    const calls = stub.mock.calls
      .filter(([input]) => String(input).startsWith('/api/reports/order-snapshot?'))
      .map(([input]) => new URL(String(input), 'http://localhost'))
    expect(calls.at(-1)?.searchParams.get('trendStart')).toBe(`${new Date().getFullYear()}-01-01`)
    expect(calls.at(-1)?.searchParams.get('trendEnd')).toBe(localDate())
    expect(wrapper.get('[data-testid="trend-footnote"]').text()).toContain('本年（')
  })

  it('Given 自定义区间 When 应用起止日期 Then 按浏览器本地日期重新取数并展示自定义口径', async () => {
    const stub = stubApi({
      report: (url) => Response.json({ ...reportPayload(), trend: trendForRequest(url) }),
    })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    await wrapper.get('[data-testid="trend-custom-toggle"]').trigger('click')
    expect(wrapper.find('[data-testid="trend-custom-panel"]').exists()).toBe(true)
    await wrapper.get('[data-testid="trend-custom-start"]').setValue('2025-06-12')
    await wrapper.get('[data-testid="trend-custom-end"]').setValue('2025-06-30')
    await wrapper.get('[data-testid="trend-custom-apply"]').trigger('click')
    await flushPromises()

    const calls = stub.mock.calls
      .filter(([input]) => String(input).startsWith('/api/reports/order-snapshot?'))
      .map(([input]) => new URL(String(input), 'http://localhost'))
    expect(calls.at(-1)?.searchParams.get('trendStart')).toBe('2025-06-12')
    expect(calls.at(-1)?.searchParams.get('trendEnd')).toBe('2025-06-30')
    expect(wrapper.find('[data-testid="trend-custom-panel"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="trend-footnote"]').text()).toContain(
      '自定义区间（2025-06-12 至 2025-06-30）',
    )
  })

  it('Given 自定义区间非法 When 校验 Then 显示错误且不发起取数', async () => {
    const stub = stubApi({})
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper
    const reportCalls = () =>
      stub.mock.calls.filter(([input]) => String(input).startsWith('/api/reports/order-snapshot?'))
        .length

    await wrapper.get('[data-testid="trend-custom-toggle"]').trigger('click')
    const apply = wrapper.get('[data-testid="trend-custom-apply"]')
    const start = wrapper.get('[data-testid="trend-custom-start"]')
    const end = wrapper.get('[data-testid="trend-custom-end"]')
    const before = reportCalls()

    await start.setValue('2025-07-01')
    await end.setValue('2025-06-01')
    expect(wrapper.get('[data-testid="trend-custom-error"]').text()).toBe(
      '开始日期不能晚于结束日期。',
    )
    expect(apply.attributes('disabled')).toBeDefined()

    await start.setValue('2024-06-01')
    await end.setValue('2025-12-31')
    expect(wrapper.get('[data-testid="trend-custom-error"]').text()).toBe(
      '统计区间最长支持 366 天。',
    )

    await start.setValue(localDate(-1))
    await end.setValue(localDate(1))
    expect(wrapper.get('[data-testid="trend-custom-error"]').text()).toBe('结束日期不能晚于今天。')

    await apply.trigger('click')
    await flushPromises()
    expect(reportCalls()).toBe(before)
  })
})
