import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'

// 普通函数而不是 vi.fn：避免被拒绝的 Promise 被 spy 的记录回调变成未处理的拒绝
const api = vi.hoisted(() => ({
  calls: [] as Record<string, unknown>[],
  respond: (): Promise<unknown> => Promise.resolve(null)
}))

vi.mock('@/api/order', () => ({
  fetchOrders: (params: Record<string, unknown>) => {
    api.calls.push(params)
    return api.respond()
  },
  deleteOrders: () => Promise.resolve({ deleted: 0 })
}))
vi.mock('@/hooks/business/useErpHub', () => ({
  useErpHub: () => ({
    state: ref('idle'),
    orderSyncTasks: ref({}),
    connect: () => Promise.resolve(),
    disconnect: () => Promise.resolve(),
    startOrderSync: () => Promise.resolve({ accepted: true }),
    startAllOrderSync: () => Promise.resolve({ accepted: true, startedCount: 0, busyCount: 0 })
  })
}))

const page = {
  items: [
    {
      id: 'o1',
      sid: 1001,
      shopName: '示例店铺 A',
      platform: 'AliExpress',
      platformOrders: [{ number: 'TM1001', status: '已发货', matched: true }],
      created: '2026-11-11T10:20:30',
      dispatched: null,
      lineCount: 1,
      itemQuantity: 2,
      lines: [
        {
          productId: 'P1',
          skuCode: 'SKU-A',
          name: '无线蓝牙耳机',
          imageUrl: 'https://img.example/a.jpg',
          specs: 'Color: Black; Size: M',
          quantity: 2,
          unitPrice: 9.9,
          currency: 'USD',
          logistics: 'Cainiao Standard',
          details: [{ label: '子订单状态', value: 'WAIT_SELLER_SEND_GOODS', group: 'status' }]
        }
      ],
      status: 'WAIT_SELLER_SEND_GOODS',
      buyer: 'buyer01',
      totalAmount: 19.8,
      totalCurrency: 'USD',
      logistics: 'Cainiao Standard',
      paid: '2026-11-11 10:25:00',
      details: [{ label: '支付方式', value: 'Credit Card', group: 'payment' }]
    },
    {
      id: 'o2',
      sid: 1002,
      shopName: null,
      platform: 'AliExpress',
      platformOrders: [],
      created: null,
      dispatched: null,
      lineCount: 0,
      itemQuantity: 0,
      lines: [],
      status: null,
      buyer: null,
      totalAmount: null,
      totalCurrency: null,
      logistics: null,
      paid: null,
      details: []
    }
  ],
  total: 2,
  page: 1,
  pageSize: 20,
  options: {
    shops: [{ sid: 1001, name: '示例店铺 A' }],
    platforms: ['AliExpress'],
    statuses: ['已发货']
  }
}

const ArtTableHeaderStub = defineComponent({
  name: 'ArtTableHeader',
  setup:
    (_, { slots }) =>
    () =>
      h('div', slots.left?.())
})

async function mountPage() {
  const { default: OrderList } = await import('@/views/order/list/index.vue')
  const wrapper = mount(OrderList, {
    global: { stubs: { ArtTableHeader: ArtTableHeaderStub } }
  })
  await flushPromises()
  return wrapper
}

describe('订单列表页', () => {
  beforeEach(() => {
    api.calls.length = 0
    api.respond = () => Promise.resolve(page)
  })

  it('Given 服务端返回 2 笔订单 When 打开页面 Then 以第 1 页、20 条请求并展示店铺、平台与订单号', async () => {
    const wrapper = await mountPage()

    expect(api.calls[0]).toMatchObject({ page: 1, pageSize: 20 })
    const text = wrapper.text()
    expect(text).toContain('示例店铺 A')
    expect(text).toContain('TM1001')
    expect(text).toContain('2026-11-11 10:20:30')
  })

  it('Given 订单含商品行 When 渲染 Then 展示商品名称、规格标签、SKU、数量与单价', async () => {
    const wrapper = await mountPage()

    expect(wrapper.findAll('[data-testid="order-line"]')).toHaveLength(1)
    const text = wrapper.text()
    expect(text).toContain('无线蓝牙耳机')
    expect(text).toContain('Color')
    expect(text).toContain('Black')
    expect(text).toContain('Size')
    expect(text).toContain('SKU-A')
    expect(text).toContain('×2')
    expect(text).toContain('USD 9.90')
  })

  it('Given 订单列表 When 渲染 Then 每行有详情与删除操作，勾选后出现悬浮的批量操作条', async () => {
    const wrapper = await mountPage()
    const labels = wrapper.findAll('button').map((button) => button.text())
    expect(labels).toContain('详情')
    expect(labels).toContain('删除')
    expect(wrapper.find('[data-testid="bulk-bar"]').exists()).toBe(false)

    await wrapper.findAll('tbody input[type="checkbox"]')[0].setValue(true)
    await flushPromises()

    const bar = wrapper.get('[data-testid="bulk-bar"]')
    expect(bar.text()).toContain('已选择 1 个订单')
    expect(bar.text()).toContain('批量删除')
  })

  it('Given 订单含金额、买家、物流与补充信息 When 渲染 Then 按平台无关的结构展示', async () => {
    const wrapper = await mountPage()

    const text = wrapper.text()
    expect(text).toContain('USD 19.80')
    expect(text).toContain('买家 buyer01')
    expect(text).toContain('Cainiao Standard')
    expect(text).toContain('支付方式')
    expect(text).toContain('Credit Card')
    expect(text).toContain('付款')
  })

  it('Given 订单缺少店铺名和平台订单 When 渲染 Then 回退为“店铺 #sid”与占位符', async () => {
    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('店铺 #1002')
  })

  it('Given 填写平台订单号 When 点击搜索 Then 带着订单号从第 1 页重新请求', async () => {
    const wrapper = await mountPage()

    await wrapper.get('input[placeholder="平台订单号"]').setValue('  TM1001 ')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(api.calls.at(-1)).toMatchObject({ page: 1, number: 'TM1001' })
  })

  it('Given 已筛选 When 清空订单号并搜索 Then 不再带该条件重新请求，且没有重置按钮', async () => {
    const wrapper = await mountPage()
    await wrapper.get('input[placeholder="平台订单号"]').setValue('TM1001')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    await wrapper.get('input[placeholder="平台订单号"]').setValue('')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(api.calls.at(-1)).toMatchObject({ page: 1, number: '' })
    expect(wrapper.findAll('button').some((button) => button.text() === '重置')).toBe(false)
  })

  it('Given 无权查看所选店铺（403） When 请求 Then 提示调整筛选且不展示订单', async () => {
    const { ApiError } = await import('@/utils/http')
    api.respond = () => Promise.reject(new ApiError('forbidden', 403))

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('无权查看所选店铺，请调整筛选。')
    expect(wrapper.text()).not.toContain('TM1001')
  })

  it('Given 其他加载失败 When 请求 Then 显示错误文案并提供重试', async () => {
    api.respond = () => Promise.reject(new Error('无法加载订单，请稍后重试。'))

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('无法加载订单，请稍后重试。')
    expect(wrapper.findAll('button').some((button) => button.text() === '重试')).toBe(true)
  })
})
