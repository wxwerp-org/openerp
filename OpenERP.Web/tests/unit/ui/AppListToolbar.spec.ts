import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppListToolbar from '../../../src/ui/AppListToolbar.vue'

describe('AppListToolbar', () => {
  it('Given 列表工具栏 When 渲染 Then 左中右内容分别落在各自区域', () => {
    const wrapper = mount(AppListToolbar, {
      slots: { default: '批量操作', right: '刷新' },
    })

    expect(wrapper.element.tagName).toBe('DIV')
    expect(wrapper.text()).toContain('批量操作')
    expect(wrapper.text()).toContain('刷新')
  })

  it('Given 指定语义标签 When 渲染 Then 使用该标签', () => {
    const wrapper = mount(AppListToolbar, { props: { as: 'header' }, slots: { default: '订单' } })

    expect(wrapper.element.tagName).toBe('HEADER')
  })
})
