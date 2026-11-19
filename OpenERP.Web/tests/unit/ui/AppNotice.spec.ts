import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppNotice from '../../../src/ui/AppNotice.vue'

describe('AppNotice', () => {
  it('Given 默认提示 When 渲染 Then 输出标题与说明并暴露色调', () => {
    const wrapper = mount(AppNotice, {
      props: { title: '授权失效', description: '历史数据仍可读' },
    })

    expect(wrapper.attributes('data-tone')).toBe('default')
    expect(wrapper.text()).toContain('授权失效')
    expect(wrapper.text()).toContain('历史数据仍可读')
  })

  it('Given 警告提示 When 渲染 Then 使用警告色调与可访问图标', () => {
    const wrapper = mount(AppNotice, {
      props: { tone: 'amber', icon: 'i-warning', title: '连接已断开' },
    })

    expect(wrapper.attributes('data-tone')).toBe('amber')
    expect(wrapper.get('svg').attributes('aria-hidden')).toBe('true')
    expect(wrapper.get('use').attributes('href')).toBe('#i-warning')
  })

  it('Given 附加动作与状态角色 When 渲染 Then 透传插槽与属性', () => {
    const wrapper = mount(AppNotice, {
      props: { tone: 'red', title: '加载失败' },
      attrs: { role: 'alert' },
      slots: { actions: '<button type="button">重试</button>' },
    })

    expect(wrapper.attributes('role')).toBe('alert')
    expect(wrapper.get('button').text()).toBe('重试')
  })
})
