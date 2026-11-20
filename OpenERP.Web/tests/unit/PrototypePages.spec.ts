import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import BusinessConfigView from '../../src/views/BusinessConfigView.vue'
import OrdersView from '../../src/views/OrdersView.vue'
import DashboardPrototypeView from '../../src/views/prototype/DashboardPrototypeView.vue'
import ProductPrototypeView from '../../src/views/prototype/ProductPrototypeView.vue'
import { installNativeDialogShim } from './installNativeDialogShim'

beforeAll(installNativeDialogShim)

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('原型页面与收敛后的真实页面', () => {
  it('Given 产品原型 When 打开添加产品并空值保存 Then 七分区表单与品名校验仍按冻结版本工作', async () => {
    const wrapper = mount(ProductPrototypeView)

    expect(wrapper.find('[data-testid="product-empty"]').exists()).toBe(true)
    await wrapper.get('#openProductForm').trigger('click')
    expect(wrapper.get('#productFormLayer').attributes('open')).toBeDefined()
    expect(wrapper.findAll('[aria-label="产品表单分区"] button')).toHaveLength(7)

    await wrapper.get('#saveProductForm').trigger('click')
    expect(wrapper.get('[data-invalid="true"]').text()).toContain('请输入产品名称')
  })

  it('Given 原型工作台 When 渲染 Then 仍为冻结的假数据版本', () => {
    const wrapper = mount(DashboardPrototypeView)

    expect(wrapper.text()).toContain('初始化引导')
    expect(wrapper.text()).toContain('11-12 15:47')
  })

  it('Given 订单列表 When 打开 Then 不再出现订单公告入口与弹窗', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        Response.json({
          items: [],
          total: 0,
          page: 1,
          pageSize: 20,
          options: { shops: [], platforms: [], statuses: [] },
        }),
      ),
    )
    const wrapper = mount(OrdersView)
    await flushPromises()

    expect(wrapper.find('#openNotice').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('订单公告')
    expect(wrapper.find('#noticeLayer').exists()).toBe(false)
  })

  it('Given 业务配置 When 打开 Then 只保留 ERP 接入配置', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = new URL(String(input), 'http://localhost')
        if (url.pathname === '/api/erp/config')
          return Response.json({ configured: true, baseUrl: 'https://erp.example' })
        return new Response(null, { status: 404 })
      }),
    )
    const wrapper = mount(BusinessConfigView)
    await flushPromises()

    expect(wrapper.text()).toContain('ERP 接入')
    expect(wrapper.text()).toContain('配置状态')
    expect(wrapper.text()).toContain('已配置')
    expect(wrapper.text()).not.toContain('数据权限管理')
    expect(wrapper.text()).not.toContain('业务配置')
  })
})
