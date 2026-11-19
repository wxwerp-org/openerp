import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppProgress from '../../../src/ui/AppProgress.vue'

describe('AppProgress', () => {
  it('Given 同步进度 When 渲染 Then 暴露进度条三值与名称', () => {
    const wrapper = mount(AppProgress, { props: { value: 42, label: '历史订单同步进度' } })

    const bar = wrapper.get('[role="progressbar"]')
    expect(bar.attributes('aria-label')).toBe('历史订单同步进度')
    expect(bar.attributes('aria-valuemin')).toBe('0')
    expect(bar.attributes('aria-valuemax')).toBe('100')
    expect(bar.attributes('aria-valuenow')).toBe('42')
  })

  it('Given 越界进度 When 渲染 Then ARIA 值与显示宽度都钳制在合法范围', () => {
    const above = mount(AppProgress, { props: { value: 120, label: '同步进度' } })
    expect(above.get('[role="progressbar"]').attributes('aria-valuenow')).toBe('100')
    expect(above.get('span').attributes('style')).toContain('width: 100%')

    const below = mount(AppProgress, { props: { value: -10, label: '同步进度' } })
    expect(below.get('[role="progressbar"]').attributes('aria-valuenow')).toBe('0')
    expect(below.get('span').attributes('style')).toContain('width: 0%')
  })

  it('Given 语义色调 When 渲染 Then 使用对应主题颜色', () => {
    const wrapper = mount(AppProgress, { props: { value: 30, label: '同步进度', tone: 'danger' } })

    expect(wrapper.get('span').attributes('style')).toContain('var(--color-danger)')
  })
})
