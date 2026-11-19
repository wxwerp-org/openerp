import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppTh from '../../../src/ui/AppTh.vue'

describe('AppTh', () => {
  it('Given 表头单元格 When 渲染 Then 默认声明列作用域', () => {
    const wrapper = mount(AppTh, { slots: { default: '用户名' } })

    expect(wrapper.element.tagName).toBe('TH')
    expect(wrapper.attributes('scope')).toBe('col')
    expect(wrapper.attributes('data-sticky')).toBe('none')
  })

  it('Given 数值列与固定列 When 渲染 Then 暴露对齐和固定状态', () => {
    const wrapper = mount(AppTh, {
      props: { numeric: true, sticky: 'right' },
      slots: { default: '操作' },
    })

    expect(wrapper.attributes('data-numeric')).toBe('true')
    expect(wrapper.attributes('data-sticky')).toBe('right')
  })
})
