import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import PerformanceTrendCard from '../../src/components/dashboard/PerformanceTrendCard.vue'
import RankingCard from '../../src/components/dashboard/RankingCard.vue'
import OrderDistributionCard from '../../src/components/dashboard/OrderDistributionCard.vue'
import InventoryOverviewCard from '../../src/components/dashboard/InventoryOverviewCard.vue'
import ProfitCard from '../../src/components/dashboard/ProfitCard.vue'
import DashboardView from '../../src/views/DashboardView.vue'
import ProductListView from '../../src/views/ProductListView.vue'
import OrdersView from '../../src/views/OrdersView.vue'
import UsersView from '../../src/views/UsersView.vue'
import BusinessConfigView from '../../src/views/BusinessConfigView.vue'
import NewDevView from '../../src/views/NewDevView.vue'
import ProductFormDialog from '../../src/forms/ProductFormDialog.vue'
import OrderNoticeDialog from '../../src/forms/OrderNoticeDialog.vue'
import ProfileDialog from '../../src/account/ProfileDialog.vue'
import PasswordDialog from '../../src/account/PasswordDialog.vue'
import { adminAccount } from './adminAccount'

describe('工作台数据控件与无障碍质量', () => {
  it.each([
    [PerformanceTrendCard, '业绩周期', '近7天'],
    [RankingCard, '排行商品维度', '父ASIN'],
    [RankingCard, '排行周期', '昨日'],
    [OrderDistributionCard, '订单类型', '退货'],
    [OrderDistributionCard, '订单周期', '前14天'],
    [InventoryOverviewCard, '库存指标', '货值'],
    [ProfitCard, '利润类型', '订单利润'],
    [ProfitCard, '利润周期', '前30天'],
  ])(
    'Given 分段控件 %s When 选择 %s 中的 %s Then 选中态和 aria-pressed 同步',
    (component, label, option) => {
      const wrapper = mount(component)
      const group = wrapper.get(`[role="group"][aria-label="${label}"]`)
      const target = group.findAll('button').find((button) => button.text() === option)!

      return target.trigger('click').then(() => {
        expect(target.attributes('aria-pressed')).toBe('true')
        const pressed = wrapper
          .get(`[role="group"][aria-label="${label}"]`)
          .findAll('button')
          .filter((button) => button.attributes('aria-pressed') === 'true')
        expect(pressed).toHaveLength(1)
        expect(pressed[0]!.text()).toBe(option)
      })
    },
  )

  it('Given 产品空表 When 展示排序图标 Then 排序按钮明确禁用', () => {
    const wrapper = mount(ProductListView)
    const controls = wrapper.findAll('[data-testid="product-sort"]')
    expect(controls.length).toBeGreaterThan(0)
    for (const control of controls) {
      expect(control.element).toBeInstanceOf(HTMLButtonElement)
      expect((control.element as HTMLButtonElement).disabled).toBe(true)
    }
  })

  it('Given 原型主要页面和弹窗 When 使用图标按钮 Then 每个按钮有可访问名称且装饰 SVG 不重复朗读', () => {
    const components = [
      DashboardView,
      ProductListView,
      OrdersView,
      UsersView,
      BusinessConfigView,
      NewDevView,
      ProductFormDialog,
      OrderNoticeDialog,
      ProfileDialog,
      PasswordDialog,
    ]

    const dialogComponents: unknown[] = [
      ProductFormDialog,
      OrderNoticeDialog,
      ProfileDialog,
      PasswordDialog,
    ]

    for (const component of components) {
      const wrapper = mount(component, {
        props: dialogComponents.includes(component) ? { open: false, account: adminAccount } : {},
      })
      for (const button of wrapper.findAll('button').filter((item) => !item.text().trim())) {
        expect(button.attributes('aria-label'), `${component.name} 存在无名称图标按钮`).toBeTruthy()
      }
      for (const icon of wrapper.findAll('svg.ic')) {
        expect(icon.attributes('aria-hidden'), `${component.name} 存在未隐藏装饰图标`).toBe('true')
      }
    }
  })

  it('Given 复杂图表 When 辅助技术读取 Then 图表具有名称和可见数据描述', () => {
    for (const [component, selector] of [
      [PerformanceTrendCard, '.trend-chart'],
      [OrderDistributionCard, '.donut-chart'],
      [ProfitCard, '.profit-chart svg'],
    ] as const) {
      const wrapper = mount(component)
      const chart = wrapper.get(selector)
      expect(chart.attributes('role')).toBe('img')
      expect(chart.attributes('aria-label')).toBeTruthy()
      const descriptionId = chart.attributes('aria-describedby')
      expect(descriptionId).toBeTruthy()
      expect(wrapper.get(`#${descriptionId}`).text()).not.toBe('')
    }
  })

  it('Given 工作台数据表 When 键盘和辅助技术浏览 Then 表格有标题和可聚焦滚动区域', () => {
    for (const [component, selector] of [
      [RankingCard, '.rank-scroll'],
      [InventoryOverviewCard, '.inventory-table-wrap'],
    ] as const) {
      const wrapper = mount(component)
      const region = wrapper.get(selector)
      expect(wrapper.find('table caption').exists()).toBe(true)
      expect(region.attributes('role')).toBe('region')
      expect(region.attributes('tabindex')).toBe('0')
      expect(region.attributes('aria-label')).toBeTruthy()
    }
  })
})
