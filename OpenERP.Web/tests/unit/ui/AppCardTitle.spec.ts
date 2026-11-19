import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppCardTitle from '../../../src/ui/AppCardTitle.vue'

describe('AppCardTitle', () => {
  it('Given 卡片标题 When 渲染 Then 默认输出二级标题', () => {
    const wrapper = mount(AppCardTitle, { slots: { default: '实时销量' } })

    expect(wrapper.element.tagName).toBe('H2')
    expect(wrapper.text()).toBe('实时销量')
  })

  it('Given 指定标题层级 When 渲染 Then 使用该层级', () => {
    const wrapper = mount(AppCardTitle, { props: { as: 'h3' }, slots: { default: '库存' } })

    expect(wrapper.element.tagName).toBe('H3')
  })
})
