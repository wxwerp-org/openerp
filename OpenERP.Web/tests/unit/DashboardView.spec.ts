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
  report?: () => Response
  directory?: () => Response
  config?: () => Response
  availability?: () => Response
}

function stubApi(handlers: ApiHandlers) {
  const stub = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost')
    if (url.pathname === '/api/reports/order-snapshot')
      return handlers.report?.() ?? Response.json(reportPayload())
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

async function mountDashboard(): Promise<{ wrapper: VueWrapper; router: Router }> {
  const router = createRouterFor()
  await router.push('/')
  await router.isReady()
  const wrapper = mount(DashboardView, {
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
    expect(wrapper.get('[data-testid="metric-shops"]').text()).toContain('2')
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

  it('Given 空库员工 When 打开工作台 Then 显示联系管理员说明且不出现引导带与管理卡片', async () => {
    setAccount({ ...adminAccount, role: 'User', username: 'employee' })
    const stub = stubApi({ report: () => Response.json(reportPayload(0, 0)) })
    const mounted = await mountDashboard()
    wrapper = mounted.wrapper

    expect(wrapper.find('[data-testid="employee-empty-hint"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('订单数据仍为空，系统可能尚未初始化完成，请联系管理员')
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
})
