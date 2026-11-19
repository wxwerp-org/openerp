import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppTable from '../../../src/ui/AppTable.vue'

describe('AppTable', () => {
  it('Given 数据表格 When 渲染 Then 滚动区可聚焦并带可访问名称', () => {
    const wrapper = mount(AppTable, {
      props: { caption: '用户管理列表', label: '用户管理表格' },
      slots: { default: '<tbody><tr><td>lucy.lin</td></tr></tbody>' },
    })

    const region = wrapper.get('[role="region"]')
    expect(region.attributes('aria-label')).toBe('用户管理表格')
    expect(region.attributes('tabindex')).toBe('0')
    expect(wrapper.get('table caption').text()).toBe('用户管理列表')
    expect(wrapper.text()).toContain('lucy.lin')
  })

  it('Given 页脚与空态插槽 When 渲染 Then 分别落在表格下方与表格区域内', () => {
    const wrapper = mount(AppTable, {
      props: { caption: '用户管理列表', label: '用户管理表格' },
      slots: { default: '<tbody />', footer: '共 4 条', overlay: '暂无数据' },
    })

    expect(wrapper.text()).toContain('共 4 条')
    expect(wrapper.text()).toContain('暂无数据')
  })

  it('Given 纵向滚动与换行模式 When 渲染 Then 暴露状态供样式与测试使用', () => {
    const wrapper = mount(AppTable, {
      props: { caption: '用户管理列表', label: '用户管理表格', scrollY: true, wrap: true },
    })

    expect(wrapper.get('[role="region"]').attributes('data-scroll-y')).toBe('true')
    expect(wrapper.get('table').attributes('data-wrap')).toBe('true')
  })
})
