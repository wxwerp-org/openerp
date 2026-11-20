import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import RealtimeSalesCard from '../../src/views/prototype/dashboard/RealtimeSalesCard.vue'
import ShopsView from '../../src/views/ShopsView.vue'
import OrdersView from '../../src/views/OrdersView.vue'
import ProductPrototypeView from '../../src/views/prototype/ProductPrototypeView.vue'
import UsersView from '../../src/views/UsersView.vue'

describe('数据驱动控件与文档结构', () => {
  it('Given 实时销量 When 切换到昨日 Then 选中态真实更新', async () => {
    const wrapper = mount(RealtimeSalesCard)
    const group = wrapper.get('[role="group"][aria-label="实时销量周期"]')
    const yesterday = group.findAll('button').find((button) => button.text() === '昨日')!

    await yesterday.trigger('click')

    const pressed = wrapper
      .get('[role="group"][aria-label="实时销量周期"]')
      .findAll('button')
      .filter((button) => button.attributes('aria-pressed') === 'true')
    expect(pressed).toHaveLength(1)
    expect(pressed[0]!.text()).toBe('昨日')
  })

  it('Given 产品视图分段控件 When 选择按 SPU Then 当前选项随状态变化', async () => {
    const products = mount(ProductPrototypeView)
    const group = products.get('[role="group"][aria-label="产品维度"]')
    const spu = group.findAll('button').find((button) => button.text() === '按SPU')!

    await spu.trigger('click')

    const pressed = products
      .get('[role="group"][aria-label="产品维度"]')
      .findAll('button')
      .filter((button) => button.attributes('aria-pressed') === 'true')
    expect(pressed).toHaveLength(1)
    expect(pressed[0]!.text()).toBe('按SPU')
  })

  it('Given 真实用户创建时间及未知时间 When 连续排序 Then 行顺序和 aria-sort 同步变化', async () => {
    const makeUser = (username: string, createdAt: string | null) => ({
      id: username,
      username,
      name: username,
      role: 'User',
      createdAt,
      phone: null,
      email: null,
      lastLoginAt: null,
    })
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        Response.json([
          makeUser('admin', null),
          makeUser('bob', '2026-11-18T04:00:00Z'),
          makeUser('alice', '2026-11-17T04:00:00Z'),
        ]),
      ),
    )
    const wrapper = mount(UsersView)
    try {
      await flushPromises()
      const header = wrapper.get('th[data-sort="created"]')
      const usernames = () => wrapper.findAll('tbody th[scope="row"]').map((cell) => cell.text())
      expect(wrapper.findAll('tbody tr')[0].get('[data-testid="user-created-at"]').text()).toBe('—')

      await header.get('button').trigger('click')
      expect(header.attributes('aria-sort')).toBe('ascending')
      expect(usernames()).toEqual(['alice', 'bob', 'admin'])

      await header.get('button').trigger('click')
      expect(header.attributes('aria-sort')).toBe('descending')
      expect(usernames()).toEqual(['bob', 'alice', 'admin'])
    } finally {
      wrapper.unmount()
      vi.unstubAllGlobals()
    }
  })

  it('Given 列表与页面 When 渲染文档结构 Then 每页有 h1 且表格有 caption 和列 scope', () => {
    for (const component of [ProductPrototypeView, OrdersView, UsersView]) {
      const wrapper = mount(component)
      expect(wrapper.findAll('h1')).toHaveLength(1)
      expect(wrapper.find('table caption').exists()).toBe(true)
      expect(wrapper.findAll('th[scope="col"]').length).toBeGreaterThan(0)
    }
  })

  it('Given 既有服务页面 When 渲染于单主内容外壳 Then 页面自身不重复创建 main landmark', () => {
    expect(mount(ShopsView).element.tagName).toBe('SECTION')
  })
})
