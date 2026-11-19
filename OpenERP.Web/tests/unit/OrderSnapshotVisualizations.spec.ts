import { mount, type VueWrapper } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { OrderSnapshotReport, ReportShop } from '../../src/reports/orderSnapshot'
import OrderSnapshotShopRanking from '../../src/components/reports/OrderSnapshotShopRanking.vue'
import OrderSnapshotPlatformDistribution from '../../src/components/reports/OrderSnapshotPlatformDistribution.vue'

type Groups = OrderSnapshotReport['groups']

function shop(partial: Partial<ReportShop> & { key: string }): ReportShop {
  return {
    source: 'erp',
    platform: 'TemuPop',
    shopId: partial.key,
    label: partial.key,
    ...partial,
  }
}

function groups(entries: Array<[ReportShop, number, number]>): Groups {
  return entries.map(([shop, orderCount, itemQuantity]) => ({
    shop,
    orderCount,
    itemQuantity,
  }))
}

// 商品件数降序：B20 / E15 / F12 / A10 / D8；订单数降序：C10 / E8 / D6 / F5 / B4。
const rankingGroups = groups([
  [shop({ key: 'shop-a', label: '店铺 A' }), 2, 10],
  [shop({ key: 'shop-b', label: '店铺 B', platform: 'Amazon' }), 4, 20],
  [shop({ key: 'shop-c', label: '店铺 C' }), 10, 6],
  [shop({ key: 'shop-d', label: '店铺 D' }), 6, 8],
  [shop({ key: 'shop-e', label: '店铺 E' }), 8, 15],
  [shop({ key: 'shop-f', label: '店铺 F' }), 5, 12],
])

const distributionGroups = groups([
  [shop({ key: 'shop-a', platform: 'TemuPop' }), 3, 9],
  [shop({ key: 'shop-b', platform: 'Amazon' }), 2, 4],
  [shop({ key: 'shop-c', platform: 'TemuPop' }), 5, 5],
  [shop({ key: 'shop-d', platform: 'Amazon' }), 1, 2],
])

function widthOf(element: Element): number {
  return Number.parseFloat(element.getAttribute('style')?.match(/width:\s*([\d.]+)%/)?.[1] ?? 'NaN')
}

// 排行图的可视行是 figure 内语义列表的 li；单元格顺序为排名、店铺、条形槽、条形值。
function rankRows(wrapper: VueWrapper) {
  return wrapper.findAll('figure ul > li')
}

function rankShops(wrapper: VueWrapper): string[] {
  return rankRows(wrapper).map((row) => row.findAll('span')[1]!.text())
}

function rankValues(wrapper: VueWrapper): string[] {
  return rankRows(wrapper).map((row) => row.get('b').text())
}

function rankBarWidths(wrapper: VueWrapper): number[] {
  return rankRows(wrapper).map((row) => widthOf(row.findAll('span')[3]!.element))
}

// 分布图的可视图例与无障碍等价表分别由测试钩子和 table 语义定位。
function distributionLegend(wrapper: VueWrapper) {
  return wrapper.get('[data-testid="report-platform-legend"]')
}

function distributionRows(wrapper: VueWrapper) {
  return wrapper.findAll('[data-testid="report-platform-legend"] > div')
}

describe('店铺排行组件', () => {
  it('Given 多店铺分组 When 挂载 Then 默认按商品件数展示 Top 5、按钮状态与条形同步', () => {
    const wrapper = mount(OrderSnapshotShopRanking, { props: { groups: rankingGroups } })

    const buttons = wrapper.get('[aria-label="排行指标"]').findAll('button')
    expect(buttons.map((button) => button.text())).toEqual(['商品件数', '订单数'])
    expect(buttons[0]!.attributes('aria-pressed')).toBe('true')
    expect(buttons[1]!.attributes('aria-pressed')).toBe('false')

    expect(rankRows(wrapper)).toHaveLength(5)
    expect(rankShops(wrapper)).toEqual(['店铺 B', '店铺 E', '店铺 F', '店铺 A', '店铺 D'])
    expect(rankValues(wrapper)).toEqual(['20', '15', '12', '10', '8'])
    expect(rankBarWidths(wrapper)).toEqual([100, 75, 60, 50, 40])
  })

  it('Given 多店铺分组 When 切换到订单数 Then 选中按钮、Top 5 顺序、原始数量和条形同步更新', async () => {
    const wrapper = mount(OrderSnapshotShopRanking, { props: { groups: rankingGroups } })

    await wrapper.get('[aria-label="排行指标"]').findAll('button')[1]!.trigger('click')

    const buttons = wrapper.get('[aria-label="排行指标"]').findAll('button')
    expect(buttons[0]!.attributes('aria-pressed')).toBe('false')
    expect(buttons[1]!.attributes('aria-pressed')).toBe('true')

    expect(rankShops(wrapper)).toEqual(['店铺 C', '店铺 E', '店铺 D', '店铺 F', '店铺 B'])
    expect(rankValues(wrapper)).toEqual(['10', '8', '6', '5', '4'])
    expect(rankBarWidths(wrapper)).toEqual([100, 80, 60, 50, 40])
  })

  it('Given 任意数据 When 渲染 Then 提供 figure 可见说明与视觉隐藏等价数据表', () => {
    const wrapper = mount(OrderSnapshotShopRanking, { props: { groups: rankingGroups } })

    const figure = wrapper.get('figure')
    expect(figure.get('figcaption').text()).toContain('前 5')
    expect(figure.get('figcaption').classes()).not.toContain('sr-only')

    const table = figure.get('table')
    expect(table.classes()).toContain('sr-only')
    expect(table.get('caption').text()).toContain('排行')
    expect(table.findAll('th[scope="col"]')).toHaveLength(3)
    expect(table.findAll('tbody tr')).toHaveLength(5)
    expect(table.findAll('tbody tr')[0]!.text()).toContain('店铺 B')
  })

  it('Given 空分组 When 挂载 Then 显示空态且不渲染排行条形', () => {
    const wrapper = mount(OrderSnapshotShopRanking, { props: { groups: [] } })

    expect(wrapper.get('p').text()).toContain('暂无')
    expect(wrapper.find('figure ul > li').exists()).toBe(false)
    expect(wrapper.find('figure').exists()).toBe(false)
  })

  it('Given 加载中 When 挂载 Then 显示中性加载占位且不渲染空态或图表', () => {
    const wrapper = mount(OrderSnapshotShopRanking, { props: { groups: [], loading: true } })

    expect(wrapper.get('p').text()).toContain('正在加载')
    expect(wrapper.text()).not.toContain('暂无')
    expect(wrapper.find('figure').exists()).toBe(false)
  })

  it('Given 加载完成且为空 When 挂载 Then 仍显示空态而不是加载占位', () => {
    const wrapper = mount(OrderSnapshotShopRanking, { props: { groups: [], loading: false } })

    expect(wrapper.get('p').text()).toContain('暂无')
    expect(wrapper.text()).not.toContain('正在加载')
  })

  it('Given 大数量 When 挂载 Then 可视值与等价表统一千分位', () => {
    const wrapper = mount(OrderSnapshotShopRanking, {
      props: { groups: groups([[shop({ key: 'shop-big', label: '店铺 大' }), 1, 12345]]) },
    })

    expect(rankValues(wrapper)).toEqual(['12,345'])
    expect(wrapper.get('figure table tbody tr').text()).toContain('12,345')
  })
})

describe('平台分布组件', () => {
  it('Given 多平台分组与总订单数 When 挂载 Then 展示总订单数、平台数量和百分比', () => {
    const wrapper = mount(OrderSnapshotPlatformDistribution, {
      props: { groups: distributionGroups, totalOrders: 11 },
    })

    const figure = wrapper.get('figure')
    expect(figure.get('strong').text()).toBe('11')
    expect(figure.get('strong + span').text()).toContain('总订单')

    const rows = distributionRows(wrapper)
    expect(rows).toHaveLength(2)
    expect(rows[0]!.text()).toContain('Temu 半托管')
    expect(rows[0]!.text()).toContain('8')
    expect(rows[0]!.text()).toContain('72.7%')
    expect(rows[1]!.text()).toContain('Amazon')
    expect(rows[1]!.text()).toContain('3')
    expect(rows[1]!.text()).toContain('27.3%')
  })

  it('Given 多平台分组 When 挂载 Then 环形图按 pathLength=100 渲染且为装饰性 SVG', () => {
    const wrapper = mount(OrderSnapshotPlatformDistribution, {
      props: { groups: distributionGroups, totalOrders: 11 },
    })

    const svg = wrapper.get('[data-testid="report-platform-chart"]')
    expect(svg.attributes('aria-hidden')).toBe('true')
    const arcs = svg.findAll('circle[pathLength="100"]')
    expect(arcs).toHaveLength(2)
    const first = Number.parseFloat(arcs[0]!.attributes('stroke-dasharray')!.split(' ')[0]!)
    const second = Number.parseFloat(arcs[1]!.attributes('stroke-dasharray')!.split(' ')[0]!)
    expect(first).toBeCloseTo((8 / 11) * 100, 5)
    expect(second).toBeCloseTo((3 / 11) * 100, 5)
    expect(Number.parseFloat(arcs[1]!.attributes('stroke-dashoffset')!)).toBeCloseTo(
      -(8 / 11) * 100,
      5,
    )
  })

  it('Given 任意数据 When 渲染 Then 提供 figure 可见说明与视觉隐藏等价数据表', () => {
    const wrapper = mount(OrderSnapshotPlatformDistribution, {
      props: { groups: distributionGroups, totalOrders: 11 },
    })

    const figure = wrapper.get('figure')
    expect(figure.get('figcaption').text()).toContain('平台')
    expect(figure.get('figcaption').classes()).not.toContain('sr-only')

    const table = figure.get('table')
    expect(table.classes()).toContain('sr-only')
    expect(table.get('caption').text()).toContain('平台')
    expect(table.findAll('th[scope="col"]')).toHaveLength(3)
    expect(table.findAll('tbody tr')).toHaveLength(2)
    expect(table.findAll('tbody tr')[0]!.text()).toContain('Temu 半托管')
  })

  it('Given 空分组 When 挂载 Then 显示空态且不渲染 SVG 环、不计算比例', () => {
    const wrapper = mount(OrderSnapshotPlatformDistribution, {
      props: { groups: [], totalOrders: 0 },
    })

    expect(wrapper.get('p').text()).toContain('暂无')
    expect(wrapper.find('svg').exists()).toBe(false)
    expect(wrapper.find('[data-testid="report-platform-legend"] > div').exists()).toBe(false)
    expect(wrapper.find('figure').exists()).toBe(false)
  })

  it('Given 加载中 When 挂载 Then 显示中性加载占位且不渲染空态或图表', () => {
    const wrapper = mount(OrderSnapshotPlatformDistribution, {
      props: { groups: [], totalOrders: 0, loading: true },
    })

    expect(wrapper.get('p').text()).toContain('正在加载')
    expect(wrapper.text()).not.toContain('暂无')
    expect(wrapper.find('figure').exists()).toBe(false)
  })

  it('Given 加载完成且为空 When 挂载 Then 仍显示空态而不是加载占位', () => {
    const wrapper = mount(OrderSnapshotPlatformDistribution, {
      props: { groups: [], totalOrders: 0, loading: false },
    })

    expect(wrapper.get('p').text()).toContain('暂无')
    expect(wrapper.text()).not.toContain('正在加载')
  })

  it('Given 大数量 When 挂载 Then 总订单数、可视值与等价表统一千分位', () => {
    const wrapper = mount(OrderSnapshotPlatformDistribution, {
      props: { groups: groups([[shop({ key: 'shop-big' }), 12345, 1]]), totalOrders: 12345 },
    })

    expect(wrapper.get('figure strong').text()).toBe('12,345')
    expect(distributionLegend(wrapper).text()).toContain('12,345')
    expect(wrapper.get('figure table').text()).toContain('12,345')
  })
})
