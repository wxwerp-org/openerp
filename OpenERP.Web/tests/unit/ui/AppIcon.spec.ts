import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppIcon from '../../../src/ui/AppIcon.vue'

describe('AppIcon', () => {
  it('Given 图标名 When 渲染 Then 引用 symbol 且对辅助技术隐藏', () => {
    const wrapper = mount(AppIcon, { props: { name: 'i-search' } })

    const svg = wrapper.get('svg')
    expect(svg.attributes('aria-hidden')).toBe('true')
    expect(svg.get('use').attributes('href')).toBe('#i-search')
  })

  it('Given 显式尺寸 When 渲染 Then 宽高使用该尺寸', () => {
    const wrapper = mount(AppIcon, { props: { name: 'i-search', size: 18 } })

    const style = wrapper.get('svg').attributes('style') ?? ''
    expect(style).toContain('width: 18px')
    expect(style).toContain('height: 18px')
  })
})
