import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import DashboardView from '../../src/views/DashboardView.vue'

describe('工作台组件', () => {
  it('Given 原型工作台数据 When 渲染工作台 Then 全部卡片与图表按区域显示', () => {
    const wrapper = mount(DashboardView)

    for (const title of [
      '初始化引导',
      '实时销量',
      '实时广告',
      '业绩走势',
      '排行榜',
      '待处理事项',
      '运营待办',
      '订单分布',
      '店铺绩效',
      '评论统计',
      '库存概览',
      '补货建议',
      '物流看板',
      '结算利润',
    ]) {
      expect(wrapper.text()).toContain(title)
    }

    expect(wrapper.find('[data-testid="trend-chart"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="order-distribution-chart"]').exists()).toBe(true)
    expect(wrapper.findAll('[data-testid="ranking-table"] tbody tr')).toHaveLength(5)
    expect(wrapper.text()).toContain('$24,860.00')
  })
})
