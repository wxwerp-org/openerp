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

  it('Given 越界进度 When 渲染 Then 钳制显示宽度且保留原始语义值', () => {
    const wrapper = mount(AppProgress, { props: { value: 120, label: '同步进度' } })

    const bar = wrapper.get('[role="progressbar"]')
    expect(bar.attributes('aria-valuenow')).toBe('120')
    expect(wrapper.get('span').attributes('style')).toContain('width: 100%')
  })
})
