import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import RealtimeSalesCard from '../../src/components/dashboard/RealtimeSalesCard.vue'
import HomeView from '../../src/views/HomeView.vue'
import ShopsView from '../../src/views/ShopsView.vue'
import OrdersView from '../../src/views/OrdersView.vue'
import ProductListView from '../../src/views/ProductListView.vue'
import UsersView from '../../src/views/UsersView.vue'

describe('数据驱动控件与文档结构', () => {
  it('Given 实时销量 When 切换到昨日 Then 选中态真实更新', async () => {
    const wrapper = mount(RealtimeSalesCard)
    const yesterday = wrapper
      .findAll('.mini-seg button')
      .find((button) => button.text() === '昨日')!

    await yesterday.trigger('click')

    expect(yesterday.classes()).toContain('active')
    expect(
      wrapper.findAll('.mini-seg button').filter((button) => button.classes().includes('active')),
    ).toHaveLength(1)
  })

  it('Given 产品和订单分段控件 When 选择其他视图 Then 当前选项随状态变化', async () => {
    const products = mount(ProductListView)
    const spu = products.findAll('.toggle-sku button').find((button) => button.text() === '按SPU')!
    await spu.trigger('click')
    expect(spu.classes()).toContain('active')

    const orders = mount(OrdersView)
    const multiChannel = orders.findAll('.subtab').find((button) => button.text() === '多渠道订单')!
    await multiChannel.trigger('click')
    expect(multiChannel.classes()).toContain('active')
  })

  it('Given 用户创建时间 When 连续排序 Then 行顺序和 aria-sort 同步变化', async () => {
    const wrapper = mount(UsersView)
    const header = wrapper.get('th[data-sort="created"]')
    const button = header.get('button')

    await button.trigger('click')
    expect(header.attributes('aria-sort')).toBe('ascending')
    expect(wrapper.get('#userTableBody tr td.user-name').text()).toBe('alex.chen')

    await button.trigger('click')
    expect(header.attributes('aria-sort')).toBe('descending')
    expect(wrapper.get('#userTableBody tr td.user-name').text()).toBe('lucy.lin')
  })

  it('Given 列表与页面 When 渲染文档结构 Then 每页有 h1 且表格有 caption 和列 scope', () => {
    for (const component of [ProductListView, OrdersView, UsersView]) {
      const wrapper = mount(component)
      expect(wrapper.findAll('h1')).toHaveLength(1)
      expect(wrapper.find('table caption').exists()).toBe(true)
      expect(wrapper.findAll('th[scope="col"]').length).toBeGreaterThan(0)
    }
  })

  it('Given 既有服务页面 When 渲染于单主内容外壳 Then 页面自身不重复创建 main landmark', () => {
    expect(mount(HomeView).element.tagName).toBe('SECTION')
    expect(mount(ShopsView).element.tagName).toBe('SECTION')
  })
})
