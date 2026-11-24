import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppListPage from '../../../src/ui/AppListPage.vue'

describe('AppListPage', () => {
  it('Given 页面标题 When 渲染 Then 标题只对读屏器可见并作为页面唯一 h1', () => {
    const wrapper = mount(AppListPage, { props: { title: '订单列表' }, slots: { default: '表格' } })

    const heading = wrapper.get('h1')
    expect(heading.text()).toBe('订单列表')
    expect(heading.classes()).toContain('sr-only')
    expect(wrapper.text()).toContain('表格')
  })

  it('Given 筛选条与工具条 When 渲染 Then 按提示区、筛选、工具条、表格的顺序排列', () => {
    const wrapper = mount(AppListPage, {
      props: { title: '授权店铺', filtersLabel: '店铺筛选' },
      slots: {
        header: '<p>提示区</p>',
        filters: '<span>筛选项</span>',
        toolbar: '<span>主操作</span>',
        'toolbar-right': '<span>刷新</span>',
        default: '<p>表格区</p>',
      },
    })

    const text = wrapper.text()
    const order = ['提示区', '筛选项', '主操作', '刷新', '表格区'].map((part) => text.indexOf(part))
    expect(order.every((index) => index >= 0)).toBe(true)
    expect([...order].sort((a, b) => a - b)).toEqual(order)
    expect(wrapper.get('[role="search"], search').attributes('aria-label')).toBe('店铺筛选')
  })

  it('Given 没有筛选内容 When 渲染 Then 不出现筛选区域', () => {
    const wrapper = mount(AppListPage, {
      props: { title: '用户管理' },
      slots: { toolbar: '<span>添加用户</span>', default: '表格' },
    })

    expect(wrapper.find('search, [role="search"], form').exists()).toBe(false)
    expect(wrapper.text()).toContain('添加用户')
  })

  it('Given 表单式筛选 When 提交 Then 通知页面查询而不刷新浏览器', async () => {
    const wrapper = mount(AppListPage, {
      props: { title: '订单列表', filtersLabel: '筛选订单', filtersAs: 'form' },
      slots: { filters: '<button type="submit">搜索</button>', default: '表格' },
    })

    await wrapper.get('form[aria-label="筛选订单"]').trigger('submit')

    expect(wrapper.emitted('search')).toHaveLength(1)
  })
})
