import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppControl from '../../../src/ui/AppControl.vue'

describe('AppControl', () => {
  it('Given 默认下拉控件 When 渲染 Then 使用按钮语义并带装饰箭头', () => {
    const wrapper = mount(AppControl, { slots: { default: '全部平台' } })

    const button = wrapper.get('button')
    expect(button.attributes('type')).toBe('button')
    expect(button.attributes('data-muted')).toBe('false')
    expect(button.text()).toBe('全部平台')
    expect(button.get('svg').attributes('aria-hidden')).toBe('true')
  })

  it('Given 占位态 When 渲染 Then 暴露占位状态', () => {
    const wrapper = mount(AppControl, { props: { muted: true }, slots: { default: '分类' } })

    expect(wrapper.get('button').attributes('data-muted')).toBe('true')
  })

  it('Given 日期区间变体 When 渲染 Then 使用日历图标代替箭头', () => {
    const wrapper = mount(AppControl, {
      props: { variant: 'date-range' },
      slots: { default: '开始日期 - 结束日期' },
    })

    const use = wrapper.get('button use')
    expect(use.attributes('href')).toBe('#i-calendar')
  })

  it('Given summary 语义 When 渲染 Then 输出 summary 元素', () => {
    const wrapper = mount(AppControl, { props: { as: 'summary' }, slots: { default: '店铺' } })

    expect(wrapper.element.tagName).toBe('SUMMARY')
  })
})
