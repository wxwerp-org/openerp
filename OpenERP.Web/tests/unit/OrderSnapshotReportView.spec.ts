import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import OrderSnapshotReportView from '../../src/views/OrderSnapshotReportView.vue'

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
        lineId: 'line-1', productId: 'product-1', skuId: 'sku-1', skuCode: 'SKU-A',
        quantity: 2, disabled: false, appended: false,
      },
      {
        lineId: 'line-2', productId: null, skuId: null, skuCode: 'SKU-B',
        quantity: 3, disabled: true, appended: true,
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
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) =>
    handler(String(input), init),
  )
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
  return fetchMock.mock.calls.filter(([input]) => String(input).startsWith('/api/reports/order-snapshot?'))
}

async function mountReport(): Promise<VueWrapper> {
  const wrapper = mount(OrderSnapshotReportView)
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

  it('Given 已登录且报表可查询 When 打开商品量报表 Then 请求第一页并展示口径、汇总、全筛选店铺分组和展开明细', async () => {
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
    expect(wrapper.get('[aria-label="订单总数"]').text()).toBe('3')
    expect(wrapper.get('[aria-label="商品件数"]').text()).toBe('12')
    expect(wrapper.text()).toContain('Temu 半托管')
    expect(wrapper.text()).toContain('AliExpress')
    expect(wrapper.text()).toContain('店铺 30')
    expect(wrapper.text()).toContain('店铺 12')
    expect(wrapper.text()).toContain('查询时间')
    expect(wrapper.text()).not.toContain('下单时间')
    expect(wrapper.text()).not.toContain('2026-11-17T01:02:03Z')

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

  it('Given 筛选请求尚未完成 When 新筛选结果先返回且旧响应晚到 Then 只展示最新查询结果', async () => {
    const oldResponse = deferred<Response>()
    const fetchMock = stubFetch((url) => {
      const query = new URL(url, 'https://local.test').searchParams
      if (query.get('platform') === 'AliExpress') return oldResponse.promise
      return jsonResponse(reportBody({
        platforms: ['TemuPop', 'AliExpress'],
        summary: { orderCount: 7, itemQuantity: 19 },
        groups: [{ shop: shop(), orderCount: 7, itemQuantity: 19 }],
        orders: [order({ number: 'LATEST-ORDER', itemQuantity: 19 })],
      }))
    })
    wrapper = mount(OrderSnapshotReportView)
    await wrapper.get('select[aria-label="平台筛选"]').setValue('AliExpress')
    expect(wrapper.get('[role="status"]').text()).toContain('正在加载')
    await wrapper.get('select[aria-label="平台筛选"]').setValue('TemuPop')
    await flushPromises()
    oldResponse.resolve(jsonResponse(reportBody({
      summary: { orderCount: 99, itemQuantity: 999 },
      orders: [order({ number: 'STALE-ORDER', itemQuantity: 999 })],
    })))
    await flushPromises()

    expect(reportCalls(fetchMock)).toHaveLength(3)
    expect(wrapper.text()).toContain('LATEST-ORDER')
    expect(wrapper.text()).not.toContain('STALE-ORDER')
    expect(wrapper.get('[aria-label="订单总数"]').text()).toBe('7')
    expect(wrapper.get('[aria-label="商品件数"]').text()).toBe('19')
    expect(wrapper.text()).not.toContain('999')
  })

  it('Given 报表接口返回空结果 When 查询完成 Then 显示零汇总和空态且仍保留第一页', async () => {
    stubFetch(() => jsonResponse(reportBody({
      summary: { orderCount: 0, itemQuantity: 0 },
      groups: [],
      orders: [],
      page: 1,
      pageCount: 1,
    })))
    wrapper = await mountReport()

    expect(wrapper.text()).toContain('暂无符合条件的本地订单')
    expect(wrapper.get('[aria-label="订单总数"]').text()).toBe('0')
    expect(wrapper.get('[aria-label="商品件数"]').text()).toBe('0')
    expect(wrapper.get('button[aria-label="上一页"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[aria-label="下一页"]').attributes('disabled')).toBeDefined()
  })

  it('Given 数据服务暂不可用 When 请求失败后重试 Then 显示 ProblemDetails 标题并恢复报表', async () => {
    let failed = true
    const fetchMock = stubFetch(() => {
      if (failed) return jsonResponse({ title: '报表数据暂不可用' }, 503)
      return jsonResponse(reportBody())
    })
    wrapper = await mountReport()

    expect(wrapper.get('[role="alert"]').text()).toContain('报表数据暂不可用')
    expect(wrapper.find('button[aria-label="重试"]').exists()).toBe(true)
    failed = false
    await wrapper.get('button[aria-label="重试"]').trigger('click')
    await flushPromises()

    expect(reportCalls(fetchMock)).toHaveLength(2)
    expect(wrapper.text()).toContain('PO-001')
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })
})
