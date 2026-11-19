import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppTd from '../../../src/ui/AppTd.vue'

describe('AppTd', () => {
  it('Given 数据单元格 When 渲染 Then 输出普通单元格', () => {
    const wrapper = mount(AppTd, { slots: { default: 'lucy.lin' } })

    expect(wrapper.element.tagName).toBe('TD')
    expect(wrapper.attributes('data-sticky')).toBe('none')
    expect(wrapper.attributes('data-numeric')).toBe('false')
  })

  it('Given 数值与行首固定列 When 渲染 Then 暴露对齐和固定状态', () => {
    const wrapper = mount(AppTd, {
      props: { numeric: true, sticky: 'left' },
      slots: { default: '268' },
    })

    expect(wrapper.attributes('data-numeric')).toBe('true')
    expect(wrapper.attributes('data-sticky')).toBe('left')
  })
})
