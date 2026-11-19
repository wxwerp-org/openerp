import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppTag from '../../../src/ui/AppTag.vue'

describe('AppTag', () => {
  it('Given 默认标签 When 渲染 Then 输出纯文本且无色调状态', () => {
    const wrapper = mount(AppTag, { slots: { default: '草稿' } })

    expect(wrapper.element.tagName).toBe('SPAN')
    expect(wrapper.text()).toBe('草稿')
    expect(wrapper.attributes('data-tone')).toBe('default')
  })

  it.each(['red', 'green', 'blue', 'amber', 'gray'] as const)(
    'Given 色调 %s When 渲染 Then 暴露对应色调状态',
    (tone) => {
      const wrapper = mount(AppTag, { props: { tone }, slots: { default: tone } })

      expect(wrapper.attributes('data-tone')).toBe(tone)
      expect(wrapper.text()).toBe(tone)
    },
  )
})
