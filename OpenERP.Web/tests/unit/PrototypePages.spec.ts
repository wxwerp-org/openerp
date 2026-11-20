import { mount } from '@vue/test-utils'
import { beforeAll, describe, expect, it } from 'vitest'
import BusinessConfigView from '../../src/views/BusinessConfigView.vue'
import OrdersView from '../../src/views/OrdersView.vue'
import ProductListView from '../../src/views/ProductListView.vue'
import { installNativeDialogShim } from './installNativeDialogShim'
import { createSettingsRouter } from './settingsRouter'

beforeAll(installNativeDialogShim)

describe('原型列表与配置组件', () => {
  it('Given 产品列表 When 打开添加产品并空值保存 Then 全屏表单展示七分区且品名校验失败', async () => {
    const wrapper = mount(ProductListView)

    expect(wrapper.find('[data-testid="product-empty"]').exists()).toBe(true)
    await wrapper.get('#openProductForm').trigger('click')
    expect(wrapper.get('#productFormLayer').attributes('open')).toBeDefined()
    expect(wrapper.findAll('[aria-label="产品表单分区"] button')).toHaveLength(7)

    await wrapper.get('#saveProductForm').trigger('click')
    expect(wrapper.get('[data-invalid="true"]').text()).toContain('请输入产品名称')
  })

  it('Given 订单列表 When 打开订单公告 Then 原型公告弹窗显示', async () => {
    const wrapper = mount(OrdersView)

    await wrapper.get('#openNotice').trigger('click')
    expect(wrapper.get('#noticeLayer').attributes('open')).toBeDefined()
    expect(wrapper.get('#noticeTitle').text()).toBe('【订单列表】优化公告')
  })

  it('Given 默认基础配置 When 渲染设置页 Then 关键配置与信息图标正确显示且无模板残留', () => {
    const wrapper = mount(BusinessConfigView, { global: { plugins: [createSettingsRouter()] } })

    expect(wrapper.get('#settingsContent').text()).toContain('数据权限管理')
    expect(wrapper.get('#settingsContent').text()).toContain('周起始日')
    expect(wrapper.get('#settingsContent').text()).toContain('图片显示')
    expect(wrapper.get('#settingsContent').text()).not.toContain('svgIcon')
    expect(wrapper.findAll('[role="radiogroup"] svg').length).toBeGreaterThan(0)
  })

  it('Given 业务配置多平台分组 When 选择店铺设置 Then 三栏配置结构显示对应内容', async () => {
    const wrapper = mount(BusinessConfigView, { global: { plugins: [createSettingsRouter()] } })

    await wrapper.get('[data-settings-group="multiplatform"]').trigger('click')
    const shopButton = wrapper
      .findAll('[data-settings-item]')
      .find((button) => button.text() === '店铺设置')
    expect(shopButton).toBeDefined()
    await shopButton!.trigger('click')

    expect(wrapper.get('#settingsSecondTitle').text()).toBe('多平台')
    expect(wrapper.get('#settingsContent').text()).toContain('店铺设置')
    expect(wrapper.get('#settingsContent').text()).toContain('配置多平台模块下的店铺设置能力')
  })
})
