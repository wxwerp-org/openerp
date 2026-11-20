import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { OrderSyncTask } from '../../src/composables/orderSyncTask'
import type { ErpHubState } from '../../src/composables/useErpHub'
import { login, logout } from '../../src/account/session'
import { adminAccount } from './adminAccount'
import OrdersView from '../../src/views/OrdersView.vue'
import { installNativeDialogShim } from './installNativeDialogShim'

const hub = vi.hoisted(() => ({
  connect: vi.fn(),
  disconnect: vi.fn(),
  startOrderSync: vi.fn(),
}))
const state = ref<ErpHubState>('connected')
const orderSyncTasks = ref<Record<number, OrderSyncTask>>({})
let onOrdersChanged: (() => void) | undefined

vi.mock('../../src/composables/useErpHub', () => ({
  useErpHub: (options?: { onOrdersChanged?: () => void }) => {
    onOrdersChanged = options?.onOrdersChanged
    return { ...hub, state, orderSyncTasks }
  },
}))

const shops = [
  { sid: '42', name: '测试店铺', platform: 'TemuPop', region: 'US', isTokenOk: false },
  { sid: '43', name: '另一店铺', platform: 'TemuPop', region: 'US', isTokenOk: null },
]

function task(overrides: Partial<OrderSyncTask> = {}): OrderSyncTask {
  return {
    id: 'task-1',
    epoch: 'test-epoch',
    version: 1,
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

function configureFetch({
  loadOrders,
  availability,
  shopStatus = 200,
  emptyShops = false,
}: {
  loadOrders?: (query: URLSearchParams) => Response
  availability?: () => Response
  shopStatus?: number
  emptyShops?: boolean
} = {}) {
  const fetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost')
    switch (url.pathname) {
      case '/api/auth/csrf':
        return Response.json({ requestToken: 'test-csrf' })
      case '/api/auth/login':
        return Response.json({
          accessToken: 'employee-token',
          accessTokenExpiresAt: adminAccount.expiresAt,
          account: { ...adminAccount, username: 'employee', role: 'User' },
        })
      case '/api/auth/logout':
        return new Response(null, { status: 204 })
      case '/api/erp/order-sync-availability':
        return availability?.() ?? Response.json({ configured: true, orderSyncEnabled: true })
      case '/api/orders':
        return loadOrders?.(url.searchParams) ?? ordersPage('本地订单')
      case '/api/erp/shops':
        if (shopStatus !== 200) {
          return Response.json({ title: '上游服务不可达' }, { status: shopStatus })
        }
        return Response.json({
          items: emptyShops ? [] : shops,
          page: 1,
          pageSize: 20,
          totalCount: emptyShops ? 0 : 2,
          pageCount: emptyShops ? 0 : 1,
        })
      case '/api/erp/order-sync-settings':
        return Response.json({ enabled: false, shopSids: [], intervalMinutes: 30 })
      case '/api/erp/order-sync-settings/shops':
        return Response.json({ items: emptyShops ? [] : shops })
      default:
        throw new Error(`意外的请求：${url.pathname}`)
    }
  })
  vi.stubGlobal('fetch', fetch)
  return fetch
}

function ordersPage(number?: string, page = 1) {
  const items = number
    ? [
        {
          id: 'erp-order',
          sid: 42,
          shopName: '测试店铺',
          platform: 'TemuPop',
          platformOrders: [{ number, status: '待发货', matched: true }],
          created: null,
          dispatched: null,
          lineCount: 1,
          itemQuantity: 2,
        },
      ]
    : []
  return Response.json({
    items,
    total: number ? 21 : 0,
    page,
    pageSize: 20,
    options: { shops: [], platforms: [], statuses: [] },
  })
}

async function openPanel(wrapper: VueWrapper): Promise<void> {
  await wrapper.get('[data-testid="orders-toolbar"] [aria-haspopup="dialog"]').trigger('click')
  await flushPromises()
}

function syncButton(wrapper: VueWrapper, label = '测试店铺') {
  return wrapper.get<HTMLButtonElement>(`button[aria-label="同步历史订单：${label}"]`)
}

function orderSyncProgress(wrapper: VueWrapper): number {
  return Number(wrapper.get('[role="progressbar"]').attributes('aria-valuenow'))
}

describe('订单列表同步面板', () => {
  let wrapper: VueWrapper | undefined

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    state.value = 'connected'
    orderSyncTasks.value = {}
    onOrdersChanged = undefined
    vi.resetAllMocks()
    vi.unstubAllGlobals()
  })

  it('Given 普通用户有手动同步能力 When 从订单页同步店铺 Then 可发起并看到结果且不读系统设置', async () => {
    const fetch = configureFetch()
    installNativeDialogShim()
    await login('employee', 'password')
    try {
      hub.startOrderSync.mockImplementation(async () => {
        const result = task({ status: 'succeeded', stage: 'done' })
        orderSyncTasks.value[42] = result
        return { accepted: true, status: result, error: null }
      })
      wrapper = mount(OrdersView)
      await openPanel(wrapper)
      await syncButton(wrapper).trigger('click')
      await flushPromises()

      expect(wrapper.get('dialog[open]').text()).toContain('同步完成')
      expect(wrapper.find('[data-testid="sync-settings-card"]').exists()).toBe(false)
      const paths = fetch.mock.calls.map(([url]) => String(url).split('?')[0])
      expect(paths).not.toContain('/api/erp/config')
      expect(paths).not.toContain('/api/erp/order-sync-settings')
      expect(paths).not.toContain('/api/erp/order-sync-settings/shops')
    } finally {
      await logout()
    }
  })

  it('Given 普通用户且 ERP 未接入 When 打开同步面板 Then 提示联系管理员且不查询同步店铺', async () => {
    const fetch = configureFetch({
      availability: () => Response.json({ configured: false, orderSyncEnabled: true }),
    })
    installNativeDialogShim()
    await login('employee', 'password')
    try {
      wrapper = mount(OrdersView)
      await openPanel(wrapper)

      const panel = wrapper.get('dialog[open]')
      expect(panel.get('[role="alert"]').text()).toContain('请联系管理员配置 ERP 接入后重试。')
      expect(panel.find('[data-testid="sync-settings-card"]').exists()).toBe(false)
      expect(fetch.mock.calls.some(([url]) => String(url).startsWith('/api/erp/shops?'))).toBe(
        false,
      )
    } finally {
      await logout()
    }
  })

  it('Given 店铺授权无效和未知 When 手动同步 Then 两店均可发起且同步期间禁止重复和跨店提交', async () => {
    configureFetch()
    installNativeDialogShim()
    let finish!: () => void
    hub.startOrderSync.mockImplementation(
      (sid: number) =>
        new Promise((resolve) => {
          finish = () => {
            orderSyncTasks.value[sid] = task({ sid })
            resolve({ accepted: true, status: orderSyncTasks.value[sid], error: null })
          }
        }),
    )
    wrapper = mount(OrdersView)
    await openPanel(wrapper)

    expect(syncButton(wrapper).element.disabled).toBe(false)
    expect(syncButton(wrapper, '另一店铺').element.disabled).toBe(false)
    await syncButton(wrapper).trigger('click')
    expect(syncButton(wrapper).element.disabled).toBe(true)
    expect(syncButton(wrapper, '另一店铺').element.disabled).toBe(true)
    await syncButton(wrapper).trigger('click')
    await syncButton(wrapper, '另一店铺').trigger('click')
    expect(hub.startOrderSync.mock.calls).toEqual([[42]])

    finish()
    await flushPromises()
    expect(wrapper.text()).toContain('正在同步')
    expect(syncButton(wrapper, '另一店铺').element.disabled).toBe(true)
  })

  it('Given 其他客户端已抢占任务 When 服务端拒绝启动并刷新店铺 Then 保留原因和重试入口', async () => {
    configureFetch()
    installNativeDialogShim()
    hub.startOrderSync.mockResolvedValue({
      accepted: false,
      status: null,
      error: '已有店铺正在同步，请稍后重试',
    })
    wrapper = mount(OrdersView)
    await openPanel(wrapper)

    await syncButton(wrapper).trigger('click')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toBe('已有店铺正在同步，请稍后重试')
    await wrapper.get('dialog button[aria-label="刷新"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toBe('已有店铺正在同步，请稍后重试')
    expect(syncButton(wrapper).element.disabled).toBe(false)
  })

  it('Given 上游无法读取授权店铺 When 打开面板 Then 显示原因和重试入口', async () => {
    configureFetch({ shopStatus: 502 })
    installNativeDialogShim()
    wrapper = mount(OrdersView)
    await openPanel(wrapper)

    expect(wrapper.get('[role="alert"]').text()).toContain('上游服务不可达')
    expect(wrapper.find('dialog button[aria-label="重试"]').exists()).toBe(true)
  })

  it('Given 本地店铺目录为空 When 打开面板 Then 显示授权店铺入口', async () => {
    configureFetch({ emptyShops: true })
    installNativeDialogShim()
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/orders', component: OrdersView },
        { path: '/shops', component: { template: '<div>授权店铺</div>' } },
      ],
    })
    await router.push('/orders')
    wrapper = mount(OrdersView, { global: { plugins: [router] } })
    await openPanel(wrapper)

    const link = wrapper.get('dialog a[href="/shops"]')
    expect(link.text()).toBe('前往授权店铺')
    await link.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/shops')
  })

  it('Given 外部任务已进入详情阶段 When 打开再关闭面板 Then 计数可见且关闭后同一筛选和页码仍能刷新', async () => {
    let number = '原始订单'
    const queries: Record<string, string>[] = []
    configureFetch({
      loadOrders: (query) => {
        queries.push(Object.fromEntries(query))
        return ordersPage(number, Number(query.get('page')))
      },
    })
    installNativeDialogShim()
    orderSyncTasks.value[42] = task({ stage: 'details', read: 6, processed: 5 })
    wrapper = mount(OrdersView)
    await flushPromises()
    await wrapper.get('input[name="number"]').setValue('同一筛选')
    await wrapper.get('form[aria-label="筛选订单"]').trigger('submit')
    await flushPromises()
    await wrapper.get('[aria-label="下一页"]').trigger('click')
    await flushPromises()
    await openPanel(wrapper)

    expect(wrapper.get('dialog[open]').text()).toContain('正在同步')
    expect(wrapper.get('dialog[open]').text()).toContain('商品详情')
    expect(wrapper.get('dialog[open]').text()).toContain('读取 6 / 10')
    expect(wrapper.get('dialog[open]').text()).toContain('已处理 5')
    wrapper.get<HTMLDialogElement>('dialog[open]').element.close()
    await flushPromises()
    expect(wrapper.find('dialog[open]').exists()).toBe(false)

    number = '同步后的订单'
    onOrdersChanged?.()
    await flushPromises()
    expect(wrapper.get('tbody tr').text()).toContain(number)
    expect(queries.at(-1)).toEqual({ number: '同一筛选', page: '2', pageSize: '20' })
  })

  it('Given 面板从未打开 When 定时任务持久化完成 Then 本地列表及时刷新', async () => {
    let number = '原始订单'
    configureFetch({ loadOrders: () => ordersPage(number) })
    wrapper = mount(OrdersView)
    await flushPromises()

    expect(wrapper.get('tbody tr').text()).toContain(number)
    number = '定时同步后的订单'
    onOrdersChanged?.()
    await flushPromises()
    expect(wrapper.get('tbody tr').text()).toContain(number)
    expect(wrapper.find('dialog[open]').exists()).toBe(false)
  })

  it('Given 正在重连 When 连接恢复 Then 恢复手动同步入口', async () => {
    configureFetch()
    installNativeDialogShim()
    state.value = 'reconnecting'
    wrapper = mount(OrdersView)
    await openPanel(wrapper)

    expect(wrapper.get('dialog[open]').text()).toContain('重新连接')
    expect(syncButton(wrapper).element.disabled).toBe(true)
    state.value = 'connected'
    await flushPromises()
    expect(syncButton(wrapper).element.disabled).toBe(false)
  })

  it('Given 列表读取完成 When 进入商品详情再进入保存 Then 进度不回退且不会提前到 100%', async () => {
    configureFetch()
    installNativeDialogShim()
    orderSyncTasks.value[42] = task({ read: 10, processed: 0 })
    wrapper = mount(OrdersView)
    await openPanel(wrapper)
    expect(orderSyncProgress(wrapper)).toBe(50)

    orderSyncTasks.value[42] = task({ stage: 'details', read: 10, processed: 0 })
    await flushPromises()
    expect(wrapper.text()).toContain('商品详情')
    expect(orderSyncProgress(wrapper)).toBe(50)

    orderSyncTasks.value[42] = task({ stage: 'details', read: 10, processed: 5 })
    await flushPromises()
    expect(orderSyncProgress(wrapper)).toBe(75)

    orderSyncTasks.value[42] = task({ stage: 'saving', read: 10, processed: 10 })
    await flushPromises()
    expect(wrapper.text()).toContain('保存结果')
    expect(orderSyncProgress(wrapper)).toBe(99)
  })

  it.each([
    { stage: 'pages', read: 3, processed: 0, expected: 15 },
    { stage: 'details', read: 10, processed: 10, expected: 99 },
  ] as const)(
    'Given $stage 阶段失败 When 展示结果 Then 保留失败原因和进度',
    async ({ stage, read, processed, expected }) => {
      configureFetch()
      installNativeDialogShim()
      orderSyncTasks.value[42] = task({
        status: 'failed',
        stage,
        read,
        processed,
        error: '上游无法访问',
      })
      wrapper = mount(OrdersView)
      await openPanel(wrapper)

      expect(wrapper.text()).toContain('同步失败')
      expect(wrapper.text()).toContain('上游无法访问')
      expect(orderSyncProgress(wrapper)).toBe(expected)
    },
  )

  it.each([
    { status: 'succeeded', label: '同步完成' },
    {
      status: 'partial',
      label: '部分成功',
      skipped: 1,
      skipReasons: { 缺少详情: 1 },
      error: '有订单未保存',
    },
  ] as const)(
    'Given 任务状态为 $status When 展示结果 Then 进度为100%且展示结果计数',
    async ({ status, label, ...details }) => {
      configureFetch()
      installNativeDialogShim()
      orderSyncTasks.value[42] = task({
        status,
        stage: 'done',
        total: 10,
        read: 10,
        processed: 10,
        ...details,
      })
      wrapper = mount(OrdersView)
      await openPanel(wrapper)

      expect(orderSyncProgress(wrapper)).toBe(100)
      const panel = wrapper.get('dialog[open]')
      expect(panel.text()).toContain(label)
      expect(panel.text()).toContain('新增 1')
      expect(panel.text()).toContain('更新 1')
      expect(panel.text()).toContain('未变 1')
      expect(panel.text().includes('缺少详情：1')).toBe(status === 'partial')
      expect(panel.text().includes('有订单未保存')).toBe(status === 'partial')
    },
  )
})
