import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const api = vi.hoisted(() => ({
  ids: [] as string[],
  respond: (): Promise<unknown> => Promise.resolve(null)
}))

vi.mock('@/api/order', () => ({
  fetchOrder: (id: string) => {
    api.ids.push(id)
    return api.respond()
  },
  deleteOrders: () => Promise.resolve({ deleted: 1 })
}))
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: encodeURIComponent('AliExpress:30:1000') } }),
  useRouter: () => ({ push: () => Promise.resolve() })
}))

const order = {
  id: 'AliExpress:30:1000',
  sid: 30,
  shopName: '示例店铺 A',
  platform: 'AliExpress',
  platformOrders: [{ number: '1000', status: 'WAIT_SELLER_SEND_GOODS', matched: false }],
  created: '2026-11-11T10:20:30',
  dispatched: null,
  lineCount: 1,
  itemQuantity: 2,
  lines: [
    {
      productId: 'P1',
      skuCode: 'SKU-A',
      name: '无线蓝牙耳机',
      imageUrl: null,
      specs: 'Color: Black',
      quantity: 2,
      unitPrice: 9.9,
      currency: 'USD',
      logistics: 'Cainiao',
      details: [{ label: '子订单状态', value: 'WAIT', group: 'status' }]
    }
  ],
  status: 'WAIT_SELLER_SEND_GOODS',
  buyer: 'buyer01',
  totalAmount: 19.8,
  totalCurrency: 'USD',
  logistics: 'Cainiao',
  paid: '2026-11-11 10:25:00',
  details: [{ label: '支付方式', value: 'Credit Card', group: 'payment' }]
}

async function mountPage() {
  const { default: OrderDetail } = await import('@/views/order/detail/index.vue')
  const wrapper = mount(OrderDetail)
  await flushPromises()
  return wrapper
}

describe('订单详情页', () => {
  beforeEach(() => {
    api.ids.length = 0
    api.respond = () => Promise.resolve(order)
  })

  it('Given 路由带订单号 When 打开页面 Then 用解码后的订单号请求并展示摘要、商品与分组信息', async () => {
    const wrapper = await mountPage()

    expect(api.ids).toEqual(['AliExpress:30:1000'])
    const text = wrapper.text()
    expect(text).toContain('示例店铺 A')
    expect(text).toContain('USD 19.80')
    expect(text).toContain('无线蓝牙耳机')
    expect(text).toContain('SKU-A')
    expect(text).toContain('USD 19.80') // 商品小计 = 单价 × 数量
    expect(text).toContain('支付方式')
    expect(text).toContain('Credit Card')
    expect(text).toContain('订单创建')
    expect(text).toContain('2026-11-11 10:20:30')
  })

  it('Given 订单不存在 When 请求 404 Then 显示错误文案并提供重试', async () => {
    api.respond = () => Promise.reject(new Error('订单不存在，或无权查看。'))

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('订单不存在，或无权查看。')
    expect(wrapper.findAll('button').some((button) => button.text() === '重试')).toBe(true)
  })
})
