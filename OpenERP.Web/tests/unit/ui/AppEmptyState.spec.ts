import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppEmptyState from '../../../src/ui/AppEmptyState.vue'

describe('AppEmptyState', () => {
  it('Given 表格内空态 When 渲染 Then 默认使用覆盖层并暴露变体状态', () => {
    const wrapper = mount(AppEmptyState, { props: { description: '暂无数据' } })

    expect(wrapper.attributes('data-variant')).toBe('overlay')
    expect(wrapper.text()).toBe('暂无数据')
  })

  it('Given 页面级空态 When 渲染 Then 使用块级变体', () => {
    const wrapper = mount(AppEmptyState, {
      props: { variant: 'block', title: '没有店铺', description: '同步后可查看' },
    })

    expect(wrapper.attributes('data-variant')).toBe('block')
    expect(wrapper.text()).toContain('没有店铺')
    expect(wrapper.text()).toContain('同步后可查看')
  })

  it('Given 自定义插槽 When 渲染 Then 插槽内容取代预设文案', () => {
    const wrapper = mount(AppEmptyState, {
      props: { description: '默认文案' },
      slots: { default: '自定义空态' },
    })

    expect(wrapper.text()).toBe('自定义空态')
  })

  it('Given 图标 When 渲染 Then 图标对辅助技术隐藏', () => {
    const wrapper = mount(AppEmptyState, { props: { icon: 'i-warning' } })

    expect(wrapper.get('svg').attributes('aria-hidden')).toBe('true')
  })
})
