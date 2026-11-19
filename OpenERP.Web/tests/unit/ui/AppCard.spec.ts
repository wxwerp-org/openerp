import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppCard from '../../../src/ui/AppCard.vue'

describe('AppCard', () => {
  it('Given 卡片 When 渲染 Then 默认使用 section 容器承载内容', () => {
    const wrapper = mount(AppCard, { slots: { default: '实时销量' } })

    expect(wrapper.element.tagName).toBe('SECTION')
    expect(wrapper.text()).toBe('实时销量')
  })

  it('Given 自定义语义 When 渲染 Then 使用指定标签', () => {
    const wrapper = mount(AppCard, { props: { as: 'article' }, slots: { default: '内容' } })

    expect(wrapper.element.tagName).toBe('ARTICLE')
  })
})
