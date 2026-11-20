import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { installNativeDialogShim } from './installNativeDialogShim'
import OrdersView from '../../src/views/OrdersView.vue'

const order = {
  id: 'internal-key',
  sid: 42,
  shopName: '东南亚店铺',
  platform: 'TemuPop',
  platformOrders: [
    { number: 'PO-001', status: '已发货', matched: false },
    { number: 'PO-002', status: '待发货', matched: true },
  ],
  created: '2026-11-05T15:00:00+08:00',
  dispatched: null,
  lineCount: 2,
  itemQuantity: 7,
}

function orderPage(overrides = {}) {
  return {
    items: [order],
    total: 1,
    page: 1,
    pageSize: 20,
    options: {
      shops: [
        { sid: 42, name: '东南亚店铺' },
        { sid: 43, name: null },
      ],
      platforms: ['TemuPop'],
      statuses: ['已发货', '待发货'],
    },
    ...overrides,
  }
}

function stubOrders(handler: (query: URLSearchParams) => Response) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string) => {
      const url = new URL(input, 'http://localhost')
      if (url.pathname !== '/api/orders') return new Response(null, { status: 404 })
      return handler(url.searchParams)
    }),
  )
}

describe('订单列表', () => {
  let wrapper: VueWrapper

  afterEach(() => {
    wrapper.unmount()
    vi.unstubAllGlobals()
  })

  it('Given 本地订单包含两笔平台订单 When 打开订单页 Then 默认查询全历史并按主单展示一行及整单数量', async () => {
    const queries: URLSearchParams[] = []
    stubOrders((query) => {
      queries.push(query)
      return Response.json(orderPage())
    })

    wrapper = mount(OrdersView, { global: { stubs: ['RouterLink'] } })
    await flushPromises()

    expect(queries.map((query) => Object.fromEntries(query))).toEqual([
      { page: '1', pageSize: '20' },
    ])
    expect(wrapper.findAll('tbody tr')).toHaveLength(1)
    const row = wrapper.get('tbody tr')
    expect(row.findAll('td').map((cell) => cell.text())).toEqual([
      '东南亚店铺',
      'Temu 半托管',
      'PO-001已发货PO-002待发货',
      '2026-11-05 15:00:00',
      '—',
      '2',
      '7',
    ])
    expect(row.text()).not.toContain('internal-key')
    expect(wrapper.text()).toContain('共1条')
  })

  it('Given 已加载筛选选项 When 用店铺、平台、状态、订单号和日期组合搜索 Then 保留整单并突出命中的平台订单', async () => {
    const queries: Record<string, string>[] = []
    stubOrders((query) => {
      queries.push(Object.fromEntries(query))
      return Response.json(orderPage())
    })
    wrapper = mount(OrdersView)
    await flushPromises()

    expect(wrapper.get('select[name="shopSid"]').text()).toContain('店铺 #43')
    await wrapper.get('select[name="shopSid"]').setValue('42')
    await wrapper.get('select[name="platform"]').setValue('TemuPop')
    await wrapper.get('select[name="status"]').setValue('待发货')
    await wrapper.get('input[name="number"]').setValue('  PO-002  ')
    await wrapper.get('input[name="createdFrom"]').setValue('2026-11-01')
    await wrapper.get('input[name="createdTo"]').setValue('2026-11-05')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(queries).toHaveLength(2)
    expect(queries[1]).toEqual({
      shopSid: '42',
      platform: 'TemuPop',
      status: '待发货',
      number: 'PO-002',
      createdFrom: '2026-11-01',
      createdTo: '2026-11-05',
      page: '1',
      pageSize: '20',
    })
    const row = wrapper.get('tbody tr')
    expect(row.findAll('li').map((item) => item.text())).toEqual(['PO-001已发货', 'PO-002待发货'])
    expect(row.get('mark').text()).toBe('PO-002待发货')
    expect(row.findAll('[data-numeric="true"]').map((cell) => cell.text())).toEqual(['2', '7'])
  })

  it('Given 订单遍及两页 When 翻页再更改筛选 Then 显示目标页且新搜索返回第一页', async () => {
    const queries: Record<string, string>[] = []
    stubOrders((query) => {
      queries.push(Object.fromEntries(query))
      if (query.get('page') === '2') {
        return Response.json(
          orderPage({
            items: [{ ...order, id: 'another-key', sid: 43, shopName: null }],
            page: 2,
            total: 21,
          }),
        )
      }
      return Response.json(orderPage({ total: 21 }))
    })
    wrapper = mount(OrdersView)
    await flushPromises()

    expect(wrapper.get('[aria-label="上一页"]').attributes()).toHaveProperty('disabled')
    await wrapper.get('[aria-label="下一页"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[aria-current="page"]').text()).toBe('2')
    expect(wrapper.get('tbody tr').text()).toContain('店铺 #43')
    expect(wrapper.get('[aria-label="下一页"]').attributes()).toHaveProperty('disabled')

    await wrapper.get('input[name="number"]').setValue('PO')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(queries).toEqual([
      { page: '1', pageSize: '20' },
      { page: '2', pageSize: '20' },
      { page: '1', pageSize: '20', number: 'PO' },
    ])
    expect(wrapper.get('[aria-current="page"]').text()).toBe('1')
    expect(wrapper.get('tbody tr').text()).toContain('东南亚店铺')
  })

  it('Given 尚未同步订单 When 从空态同步 Then 打开具名面板且订单公告入口已移除', async () => {
    stubOrders(() => Response.json(orderPage({ items: [], total: 0 })))
    installNativeDialogShim()
    wrapper = mount(OrdersView, { global: { stubs: ['RouterLink'] } })
    await flushPromises()

    expect(wrapper.get('[role="status"]').text()).toContain('暂无同步订单')
    await wrapper.get('[data-testid="orders-message"] button').trigger('click')
    await flushPromises()

    const dialog = wrapper.get('dialog[open][aria-labelledby]')
    expect(dialog.text()).toContain('同步历史订单')
    expect(wrapper.find('#openNotice').exists()).toBe(false)
  })

  it('Given 有已同步订单 When 筛选无结果并重置 Then 区分无结果状态且恢复全历史订单', async () => {
    stubOrders((query) =>
      query.has('number')
        ? Response.json(orderPage({ items: [], total: 0 }))
        : Response.json(orderPage()),
    )
    wrapper = mount(OrdersView)
    await flushPromises()

    const orderNumber = wrapper.get<HTMLInputElement>('input[name="number"]')
    await orderNumber.setValue('不存在')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="status"]').text()).toContain('未找到符合筛选条件的订单')
    expect(wrapper.find('tbody tr').exists()).toBe(false)

    await wrapper.get('[aria-label="重置筛选"]').trigger('click')
    await flushPromises()

    expect(orderNumber.element.value).toBe('')
    expect(wrapper.get('tbody tr').text()).toContain('PO-001')
    expect(wrapper.text()).toContain('共1条')
  })

  it('Given 订单接口暂时失败 When 用户重试 Then 失败状态消失并显示订单', async () => {
    let unavailable = true
    stubOrders(() =>
      unavailable ? new Response(null, { status: 503 }) : Response.json(orderPage()),
    )
    wrapper = mount(OrdersView)
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toBe('无法加载订单，请稍后重试。')
    expect(wrapper.find('tbody tr').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('暂无同步订单')

    unavailable = false
    await wrapper.get('[aria-label="重新加载订单"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(wrapper.get('tbody tr').text()).toContain('PO-001')
  })
})
