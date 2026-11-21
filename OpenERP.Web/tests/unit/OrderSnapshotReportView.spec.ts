import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import OrderSnapshotReportView from '../../src/views/OrderSnapshotReportView.vue'
import { dataTrendFixture, salesFixture } from '../reportFixtures'

type Shop = {
  source: string
  platform: string
  shopId: string
  key: string
  label: string
}

type Order = {
  id: string
  number: string
  shop: Shop
  status: string
  platformOrders: { number: string; statusCode: string | null; statusText: string | null }[]
  items: {
    lineId: string
    productId: string | null
    skuId: string | null
    skuCode: string | null
    quantity: number
    disabled: boolean | null
    appended: boolean | null
  }[]
  itemQuantity: number
}

function shop(overrides: Partial<Shop> = {}): Shop {
  return {
    source: 'wxwerp',
    platform: 'TemuPop',
    shopId: '30',
    key: 'opaque:shop/30',
    label: '店铺 30',
    ...overrides,
  }
}

function order(overrides: Partial<Order> = {}): Order {
  return {
    id: 'erp-order-1',
    number: 'ERP-001',
    shop: shop(),
    status: '已发货',
    platformOrders: [{ number: 'PO-001', statusCode: 'SHIPPED', statusText: '已发货' }],
    items: [
      {
        lineId: 'line-1',
        productId: 'product-1',
        skuId: 'sku-1',
        skuCode: 'SKU-A',
        quantity: 2,
        disabled: false,
        appended: false,
      },
      {
        lineId: 'line-2',
        productId: null,
        skuId: null,
        skuCode: 'SKU-B',
        quantity: 3,
        disabled: true,
        appended: true,
      },
    ],
    itemQuantity: 5,
    ...overrides,
  }
}

function reportBody(overrides: Record<string, unknown> = {}) {
  const secondTemuShop = shop({ shopId: '31', key: 'opaque:shop/31', label: '店铺 31' })
  const otherPlatformShop = shop({
    source: 'another-source',
    platform: 'AliExpress',
    shopId: '12',
    key: 'opaque:other/12',
    label: '店铺 12',
  })
  return {
    generatedAtUtc: '2026-11-17T01:02:03Z',
    summary: { orderCount: 3, itemQuantity: 12 },
    sales: salesFixture(371.12, 'USD', 3, 0),
    trend: dataTrendFixture(),
    platforms: ['TemuPop', 'AliExpress'],
    shops: [shop(), secondTemuShop, otherPlatformShop],
    groups: [
      { shop: shop(), orderCount: 1, itemQuantity: 5 },
      { shop: secondTemuShop, orderCount: 1, itemQuantity: 4 },
      { shop: otherPlatformShop, orderCount: 1, itemQuantity: 3 },
    ],
    orders: [order()],
    page: 1,
    pageSize: 20,
    pageCount: 2,
    ...overrides,
  }
}

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

type Handler = (url: string, init?: RequestInit) => Response | Promise<Response>

function stubFetch(handler: Handler) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    // 报表用例只关心报表请求；店名映射等辅助请求返回 404，避免污染断言与计数。
    const path = new URL(String(input), 'http://localhost').pathname
    if (path !== '/api/reports/order-snapshot') return new Response(null, { status: 404 })
    return handler(String(input), init)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((fulfill) => {
    resolve = fulfill
  })
  return { promise, resolve }
}

function reportCalls(fetchMock: ReturnType<typeof stubFetch>) {
  return fetchMock.mock.calls.filter(([input]) =>
    String(input).startsWith('/api/reports/order-snapshot?'),
  )
}

let currentRouter: Router

async function createReportRouter(path = '/reports/orders'): Promise<Router> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/reports/orders', component: OrderSnapshotReportView }],
  })
  await router.push(path)
  await router.isReady()
  return router
}

async function mountReport(path = '/reports/orders'): Promise<VueWrapper> {
  const router = await createReportRouter(path)
  currentRouter = router
  const wrapper = mount(OrderSnapshotReportView, { global: { plugins: [router] } })
  await flushPromises()
  return wrapper
}

describe('本地订单商品量快照报表', () => {
  let wrapper: VueWrapper | undefined

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    vi.unstubAllGlobals()
  })

  it('Given 已登录且报表可查询 When 打开商品量报表 Then 请求第一页并展示工作台卡片结构、四指标、图表、完整分店汇总和展开明细', async () => {
    const fetchMock = stubFetch((url) => {
      expect(url).toBe('/api/reports/order-snapshot?page=1&pageSize=20')
      return jsonResponse(reportBody())
    })
    wrapper = await mountReport()

    expect(reportCalls(fetchMock)).toHaveLength(1)
    expect(wrapper.text()).toContain('按本地来源订单计数')
    expect(wrapper.text()).toContain('当前商品行数量合计，包含禁用/追加行')
    expect(wrapper.text()).toContain('不判断付款或另行扣退款')
    expect(wrapper.text()).toContain('同步未返回的旧记录可能仍保留')

    // 快照概览卡展示四个真实指标：有订单店铺数取 groups.length，平均值取 summary 派生。
    expect(wrapper.get('[aria-label="订单总数"]').text()).toBe('3')
    expect(wrapper.get('[aria-label="商品件数"]').text()).toBe('12')
    expect(wrapper.get('[aria-label="有订单店铺数"]').text()).toBe('3')
    expect(wrapper.get('[aria-label="平均每单件数"]').text()).toBe('4')
    expect(wrapper.text()).toContain('Temu 半托管')
    expect(wrapper.text()).toContain('AliExpress')
    expect(wrapper.text()).toContain('店铺 30')
    expect(wrapper.text()).toContain('店铺 12')
    expect(wrapper.text()).toContain('查询时间')
    expect(wrapper.text()).not.toContain('下单时间')
    expect(wrapper.text()).not.toContain('2026-11-17T01:02:03Z')

    // 页签页只保留一个隐藏标题，不再重复渲染可见标题。
    expect(wrapper.findAll('h1')).toHaveLength(1)
    expect(wrapper.get('h1').classes()).toContain('sr-only')
    expect(wrapper.get('h1').text()).toBe('商品量报表')

    // 工作台骨架：筛选条、工具栏与平台/店铺筛选由稳定测试钩子和可访问名称定位。
    const searchbar = wrapper.get('[data-testid="report-searchbar"]')
    expect(searchbar.find('[data-testid="report-platform-filter"]').exists()).toBe(true)
    expect(searchbar.find('[data-testid="report-toolbar"]').exists()).toBe(true)
    expect(searchbar.find('summary[aria-label="店铺筛选"]').exists()).toBe(true)

    // 概览卡与两个可视化卡片是同一分析区内的直接兄弟卡片。
    const overview = wrapper.get('[data-testid="report-overview"]')
    const rankCard = wrapper.get('[data-testid="report-shop-ranking"]')
    const distCard = wrapper.get('[data-testid="report-platform-distribution"]')
    expect(overview.element.parentElement).not.toBeNull()
    expect(rankCard.element.parentElement).toBe(overview.element.parentElement)
    expect(distCard.element.parentElement).toBe(overview.element.parentElement)

    // 图表组件渲染真实快照数值：排行按商品件数降序，分布按平台聚合占比。
    expect(rankCard.text()).toContain('店铺排行')
    expect(rankCard.text()).toContain('店铺 30')
    expect(distCard.text()).toContain('平台分布')
    expect(distCard.text()).toContain('66.7%')
    expect(distCard.text()).toContain('33.3%')

    // Top 5 图表之外仍保留完整分店汇总表（全部 3 家店铺）。
    expect(wrapper.findAll('[data-testid="report-groups-table"] tbody tr')).toHaveLength(3)
    expect(wrapper.text()).toContain('店铺 31')

    // 表格复用列表页外壳：滚动区可聚焦并带区域语义，表格有显式列宽。
    const ordersScroll = wrapper.get('[aria-label="订单明细表格"]')
    expect(ordersScroll.attributes('role')).toBe('region')
    expect(ordersScroll.attributes('tabindex')).toBe('0')
    expect(ordersScroll.find('[data-testid="report-orders-table"]').exists()).toBe(true)
    expect(
      wrapper.find('[data-testid="report-orders-panel"] [aria-label="订单明细表格"]').exists(),
    ).toBe(true)
    const groupsScroll = wrapper.get('[aria-label="分店汇总表格"]')
    expect(groupsScroll.attributes('role')).toBe('region')
    expect(groupsScroll.attributes('tabindex')).toBe('0')
    expect(groupsScroll.find('[data-testid="report-groups-table"]').exists()).toBe(true)
    expect(
      wrapper.find('[data-testid="report-groups-panel"] [aria-label="分店汇总表格"]').exists(),
    ).toBe(true)
    expect(wrapper.findAll('[data-testid="report-orders-table"] colgroup col')).toHaveLength(8)
    expect(wrapper.findAll('[data-testid="report-groups-table"] colgroup col')).toHaveLength(4)

    // 刷新、分页与下拉统一使用 AppIcon，不再出现字符箭头。
    expect(wrapper.get('button[aria-label="刷新"] use').attributes('href')).toBe('#i-refresh')
    expect(wrapper.get('button[aria-label="上一页"] use').attributes('href')).toBe('#i-chev-left')
    expect(wrapper.get('button[aria-label="下一页"] use').attributes('href')).toBe('#i-chev-right')
    const pageSizeSelect = wrapper.get('select[aria-label="每页条数"]')
    expect(
      pageSizeSelect.element.closest('label')?.querySelector('use')?.getAttribute('href'),
    ).toBe('#i-chev-down')
    expect(wrapper.get('input[aria-label="前往页码"]').attributes('aria-label')).toBe('前往页码')
    const platformFilter = wrapper.get('[data-testid="report-platform-filter"]')
    expect(platformFilter.find('select[aria-label="平台筛选"]').exists()).toBe(true)
    expect(platformFilter.find('use').attributes('href')).toBe('#i-chev-down')
    expect(wrapper.get('summary[aria-label="店铺筛选"] use').attributes('href')).toBe(
      '#i-chev-down',
    )
    const scope = wrapper.get('[data-testid="report-scope"]')
    expect(scope.attributes('role')).toBe('note')
    expect(scope.get('use').attributes('href')).toBe('#i-info')
    expect(
      wrapper.get('[aria-label="订单总数"]').element.closest('[data-testid="report-overview"]'),
    ).not.toBeNull()
    expect(wrapper.find('[data-testid="report-groups-panel"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="report-orders-panel"]').exists()).toBe(true)
    expect(wrapper.get('[aria-current="page"]').text()).toBe('1')
    expect(wrapper.find('button[aria-label="第 2 页"]').exists()).toBe(true)

    // 加载完成后不再处于 aria-busy。
    expect(wrapper.find('[aria-busy]').exists()).toBe(false)

    await wrapper.get('button[aria-label="展开订单 ERP-001 商品明细"]').trigger('click')
    expect(wrapper.text()).toContain('PO-001')
    expect(wrapper.text()).toContain('SKU-A')
    expect(wrapper.text()).toContain('SKU-B')
    expect(wrapper.text()).toContain('禁用')
    expect(wrapper.text()).toContain('追加')
    expect(wrapper.text()).toContain('本地订单件数 5')

    await wrapper.get('button[aria-label="刷新"]').trigger('click')
    await flushPromises()
    expect(reportCalls(fetchMock)).toHaveLength(2)
  })

  it('Given 可用平台和多家店铺 When 选择平台与多店铺并翻页 Then 请求携带平台、不透明店铺键、页码和页长', async () => {
    const requested: URL[] = []
    const fetchMock = stubFetch((url) => {
      requested.push(new URL(url, 'https://local.test'))
      return jsonResponse(reportBody())
    })
    wrapper = await mountReport()

    await wrapper.get('select[aria-label="平台筛选"]').setValue('TemuPop')
    await flushPromises()
    await wrapper.get('input[type="checkbox"][value="opaque:shop/30"]').setValue(true)
    await flushPromises()
    await wrapper.get('input[type="checkbox"][value="opaque:shop/31"]').setValue(true)
    await flushPromises()
    await wrapper.get('button[aria-label="下一页"]').trigger('click')
    await flushPromises()

    expect(reportCalls(fetchMock)).toHaveLength(5)
    const last = requested.at(-1)!
    expect(last.pathname).toBe('/api/reports/order-snapshot')
    expect(last.searchParams.get('platform')).toBe('TemuPop')
    expect(last.searchParams.getAll('shops')).toEqual(['opaque:shop/30', 'opaque:shop/31'])
    expect(last.searchParams.get('page')).toBe('2')
    expect(last.searchParams.get('pageSize')).toBe('20')

    await wrapper.get('select[aria-label="每页条数"]').setValue('100')
    await flushPromises()
    const resized = requested.at(-1)!
    expect(resized.searchParams.get('page')).toBe('1')
    expect(resized.searchParams.get('pageSize')).toBe('100')
  })

  it('Given 多页报表 When 点击页码或输入跳页 Then 复用共享 pager 请求目标页', async () => {
    const requested: URL[] = []
    stubFetch((url) => {
      requested.push(new URL(url, 'https://local.test'))
      return jsonResponse(reportBody({ page: 1, pageCount: 7 }))
    })
    wrapper = await mountReport()

    expect(wrapper.text()).toContain('…')
    expect(wrapper.find('button[aria-label="第 7 页"]').exists()).toBe(true)
    await wrapper.get('button[aria-label="第 3 页"]').trigger('click')
    await flushPromises()
    expect(requested.at(-1)!.searchParams.get('page')).toBe('3')

    await wrapper.get('input[aria-label="前往页码"]').setValue('6')
    await wrapper.get('input[aria-label="前往页码"]').trigger('keyup.enter')
    await flushPromises()
    expect(requested.at(-1)!.searchParams.get('page')).toBe('6')

    await wrapper.get('input[aria-label="前往页码"]').setValue('99')
    await wrapper.get('input[aria-label="前往页码"]').trigger('keyup.enter')
    await flushPromises()
    expect(requested.at(-1)!.searchParams.get('page')).toBe('7')
  })

  it('Given 跳页请求尚未返回 When 输入框因加载被禁用而失焦 Then 只保留目标页请求', async () => {
    const pending = deferred<Response>()
    const requested: URL[] = []
    stubFetch((url) => {
      requested.push(new URL(url, 'https://local.test'))
      if (requested.length === 2) return pending.promise
      return jsonResponse(reportBody({ page: 1, pageCount: 7 }))
    })
    wrapper = await mountReport()

    await wrapper.get('input[aria-label="前往页码"]').setValue('3')
    await wrapper.get('input[aria-label="前往页码"]').trigger('keyup.enter')
    // Chromium 在输入框被禁用时触发原生 blur；VTU 的 trigger 会跳过 disabled 元素，故直接派发。
    wrapper.get('input[aria-label="前往页码"]').element.dispatchEvent(new Event('blur'))
    pending.resolve(jsonResponse(reportBody({ page: 3, pageCount: 7 })))
    await flushPromises()

    expect(requested.map((url) => url.searchParams.get('page'))).toEqual(['1', '3'])
    expect(wrapper.get('[aria-current="page"]').text()).toBe('3')
  })

  it('Given 筛选请求尚未完成 When 新筛选结果先返回且旧响应晚到 Then 只展示最新查询结果', async () => {
    const oldResponse = deferred<Response>()
    const fetchMock = stubFetch((url) => {
      const query = new URL(url, 'https://local.test').searchParams
      if (query.get('platform') === 'AliExpress') return oldResponse.promise
      return jsonResponse(
        reportBody({
          platforms: ['TemuPop', 'AliExpress'],
          summary: { orderCount: 7, itemQuantity: 19 },
          groups: [{ shop: shop(), orderCount: 7, itemQuantity: 19 }],
          orders: [order({ number: 'LATEST-ORDER', itemQuantity: 19 })],
        }),
      )
    })
    const router = await createReportRouter()
    wrapper = mount(OrderSnapshotReportView, { global: { plugins: [router] } })
    await wrapper.get('select[aria-label="平台筛选"]').setValue('AliExpress')
    expect(wrapper.get('[role="status"]').text()).toContain('正在加载')
    expect(wrapper.get('[aria-busy]').attributes('aria-busy')).toBe('true')
    expect(wrapper.get('[data-testid="report-orders-empty"]').text()).toContain('正在加载')
    expect(wrapper.get('[data-testid="report-shop-ranking"]').text()).toContain('正在加载店铺排行')
    expect(wrapper.get('[data-testid="report-platform-distribution"]').text()).toContain(
      '正在加载平台分布',
    )
    expect(wrapper.text()).not.toContain('暂无店铺排行数据')
    expect(wrapper.text()).not.toContain('暂无平台分布数据')
    await wrapper.get('select[aria-label="平台筛选"]').setValue('TemuPop')
    await flushPromises()
    oldResponse.resolve(
      jsonResponse(
        reportBody({
          summary: { orderCount: 99, itemQuantity: 999 },
          orders: [order({ number: 'STALE-ORDER', itemQuantity: 999 })],
        }),
      ),
    )
    await flushPromises()

    expect(reportCalls(fetchMock)).toHaveLength(3)
    expect(wrapper.text()).toContain('LATEST-ORDER')
    expect(wrapper.text()).not.toContain('STALE-ORDER')
    expect(wrapper.get('[aria-label="订单总数"]').text()).toBe('7')
    expect(wrapper.get('[aria-label="商品件数"]').text()).toBe('19')
    expect(wrapper.text()).not.toContain('999')
    expect(wrapper.find('[aria-busy]').exists()).toBe(false)
  })

  it('Given 大数量汇总 When 加载完成 Then 概览数量与平均每单件数使用千分位', async () => {
    stubFetch(() => jsonResponse(reportBody({ summary: { orderCount: 2, itemQuantity: 5000 } })))
    wrapper = await mountReport()

    expect(wrapper.get('[aria-label="订单总数"]').text()).toBe('2')
    expect(wrapper.get('[aria-label="商品件数"]').text()).toBe('5,000')
    expect(wrapper.get('[aria-label="平均每单件数"]').text()).toBe('2,500')
  })

  it('Given 报表接口返回空结果 When 查询完成 Then 显示零汇总和空态且仍保留第一页', async () => {
    stubFetch(() =>
      jsonResponse(
        reportBody({
          summary: { orderCount: 0, itemQuantity: 0 },
          groups: [],
          orders: [],
          page: 1,
          pageCount: 1,
        }),
      ),
    )
    wrapper = await mountReport()

    expect(wrapper.get('[data-testid="report-orders-empty"]').text()).toContain(
      '暂无符合条件的本地订单',
    )
    expect(wrapper.get('[data-testid="report-groups-empty"]').text()).toContain('暂无分店汇总')
    expect(wrapper.get('[aria-label="订单总数"]').text()).toBe('0')
    expect(wrapper.get('[aria-label="商品件数"]').text()).toBe('0')
    expect(wrapper.get('[aria-label="有订单店铺数"]').text()).toBe('0')
    expect(wrapper.get('[aria-label="平均每单件数"]').text()).toBe('—')
    expect(wrapper.get('[data-testid="report-shop-ranking"]').text()).toContain('暂无店铺排行数据')
    expect(wrapper.get('[data-testid="report-platform-distribution"]').text()).toContain(
      '暂无平台分布数据',
    )
    expect(wrapper.text()).not.toContain('NaN')
    expect(wrapper.get('button[aria-label="上一页"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[aria-label="下一页"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[aria-current="page"]').text()).toBe('1')
  })

  it('Given 数据服务暂不可用 When 请求失败后重试 Then 显示 ProblemDetails 标题并恢复报表', async () => {
    let failed = true
    const fetchMock = stubFetch(() => {
      if (failed) return jsonResponse({ title: '报表数据暂不可用' }, 503)
      return jsonResponse(reportBody())
    })
    wrapper = await mountReport()

    expect(wrapper.get('[role="alert"]').text()).toContain('报表数据暂不可用')
    expect(wrapper.get('[role="alert"] use').attributes('href')).toBe('#i-warning')
    expect(wrapper.get('[data-testid="report-orders-empty"]').text()).toContain('报表加载失败')
    expect(wrapper.find('button[aria-label="重试"]').exists()).toBe(true)
    failed = false
    await wrapper.get('button[aria-label="重试"]').trigger('click')
    await flushPromises()

    expect(reportCalls(fetchMock)).toHaveLength(2)
    expect(wrapper.text()).toContain('PO-001')
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  it('Given 曾可查看的报表店铺 When 刷新返回 403 Then 清除旧报表和筛选选项', async () => {
    let revoked = false
    stubFetch(() =>
      revoked ? jsonResponse({ title: '无权查看所选店铺。' }, 403) : jsonResponse(reportBody()),
    )
    wrapper = await mountReport('/reports/orders?platform=TemuPop&shops=opaque%3Ashop%2F30')
    expect(wrapper.text()).toContain('PO-001')
    revoked = true
    await wrapper.get('button[aria-label="刷新"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('无权查看所选店铺')
    expect(wrapper.text()).not.toContain('PO-001')
    expect(wrapper.get('[data-testid="report-searchbar"]').text()).not.toContain('店铺 30')
  })

  it('Given 从工作台下钻带平台与店铺参数 When 打开报表 Then 首次请求携带筛选且控件反映 URL', async () => {
    const fetchMock = stubFetch(() => jsonResponse(reportBody()))
    wrapper = await mountReport(`/reports/orders?platform=TemuPop&shops=opaque%3Ashop%2F30`)

    const calls = reportCalls(fetchMock).map(([input]) => String(input))
    // 首请求只带平台用于取全量选项，店铺键在选项返回后才过滤应用。
    expect(calls[0]).toContain('platform=TemuPop')
    expect(calls[0]).not.toContain('shops=')
    expect(calls.at(-1)).toContain('shops=opaque%3Ashop%2F30')
    expect(wrapper.get('[data-testid="report-platform-filter"] select').element).toHaveProperty(
      'value',
      'TemuPop',
    )
    expect(wrapper.text()).toContain('已选 1 家店铺')
  })

  it('Given URL 含未知平台与未知店铺键 When 首次结果返回 Then 丢弃非法值并重新查询且不报错', async () => {
    const fetchMock = stubFetch(() => jsonResponse(reportBody()))
    wrapper = await mountReport(`/reports/orders?platform=Unknown&shops=missing%2Fkey`)

    const calls = reportCalls(fetchMock)
    expect(calls.length).toBeGreaterThanOrEqual(2)
    expect(String(calls.at(-1)![0])).toBe('/api/reports/order-snapshot?page=1&pageSize=20')
    expect(currentRouter.currentRoute.value.query.platform).toBeUndefined()
    expect(currentRouter.currentRoute.value.query.shops).toBeUndefined()
    expect(wrapper.text()).not.toContain('报表数据格式无效')
  })

  it('Given 已带筛选 When 点击清除筛选 Then 请求回到无筛选并清理 URL', async () => {
    const fetchMock = stubFetch(() => jsonResponse(reportBody()))
    wrapper = await mountReport(`/reports/orders?platform=TemuPop&shops=opaque%3Ashop%2F30`)

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '清除筛选')!
      .trigger('click')
    await flushPromises()

    expect(String(reportCalls(fetchMock).at(-1)![0])).toBe(
      '/api/reports/order-snapshot?page=1&pageSize=20',
    )
    expect(currentRouter.currentRoute.value.query.platform).toBeUndefined()
    expect(currentRouter.currentRoute.value.query.shops).toBeUndefined()
  })

  it('Given 报表已打开 When URL 筛选再次变化 Then 重置分页并请求对应筛选', async () => {
    const fetchMock = stubFetch(() => jsonResponse(reportBody()))
    wrapper = await mountReport()

    await currentRouter.push('/reports/orders?platform=TemuPop')
    await flushPromises()

    const last = String(reportCalls(fetchMock).at(-1)![0])
    expect(last).toContain('page=1')
    expect(last).toContain('platform=TemuPop')
  })

  it('Given URL 同时含有效店铺与空白/超长/重复键 When 首次请求 Then 只发送合法且去重后的键', async () => {
    const fetchMock = stubFetch(() => jsonResponse(reportBody()))
    const longKey = 'x'.repeat(1200)
    const query = [
      'platform=TemuPop',
      'shops=opaque%3Ashop%2F30',
      'shops=',
      'shops=%20%20',
      `shops=${longKey}`,
      'shops=opaque%3Ashop%2F30',
    ].join('&')
    wrapper = await mountReport(`/reports/orders?${query}`)

    const first = String(reportCalls(fetchMock)[0]![0])
    expect(new URL(first, 'https://local.test').searchParams.getAll('shops')).toEqual([])
    const applied = String(reportCalls(fetchMock).at(-1)![0])
    const params = new URL(applied, 'https://local.test').searchParams
    expect(params.getAll('shops')).toEqual(['opaque:shop/30'])
    expect(params.get('platform')).toBe('TemuPop')
    expect(wrapper.text()).toContain('已选 1 家店铺')
  })
})
