import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import AppButton from '../../../src/ui/AppButton.vue'

describe('AppButton', () => {
  it('Given 无参数 When 渲染 Then 原生 button 默认 type=button 且带默认变体状态', () => {
    const wrapper = mount(AppButton, { slots: { default: '刷新' } })

    const button = wrapper.get('button')
    expect(button.attributes('type')).toBe('button')
    expect(button.attributes('data-variant')).toBe('default')
    expect(button.attributes('data-size')).toBe('md')
    expect(button.text()).toBe('刷新')
  })

  it.each(['default', 'primary', 'soft', 'text'] as const)(
    'Given %s 变体 When 渲染 Then 暴露对应变体状态',
    (variant) => {
      const wrapper = mount(AppButton, { props: { variant }, slots: { default: variant } })

      expect(wrapper.get('button').attributes('data-variant')).toBe(variant)
    },
  )

  it('Given 大尺寸按钮 When 渲染 Then 暴露尺寸状态', () => {
    const wrapper = mount(AppButton, { props: { size: 'lg' }, slots: { default: '保存' } })

    expect(wrapper.get('button').attributes('data-size')).toBe('lg')
  })

  it('Given 提交按钮 When 渲染 Then 透传 type=submit 与 disabled', () => {
    const wrapper = mount(AppButton, {
      props: { type: 'submit', variant: 'primary', disabled: true },
      slots: { default: '保存' },
    })

    const button = wrapper.get('button')
    expect(button.attributes('type')).toBe('submit')
    expect(button.attributes('data-variant')).toBe('primary')
    expect((button.element as HTMLButtonElement).disabled).toBe(true)
  })

  it('Given 可点击按钮 When 点击 Then 触发原生点击', async () => {
    const onClick = vi.fn()
    const wrapper = mount(AppButton, { attrs: { onClick }, slots: { default: '查看' } })

    await wrapper.get('button').trigger('click')
    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('Given 禁用按钮 When 点击 Then 不触发点击', async () => {
    const onClick = vi.fn()
    const wrapper = mount(AppButton, {
      props: { disabled: true },
      attrs: { onClick },
      slots: { default: '查看' },
    })

    await wrapper.get('button').trigger('click')
    expect(onClick).not.toHaveBeenCalled()
  })

  it('Given href When 渲染 Then 使用链接语义而不是按钮', () => {
    const wrapper = mount(AppButton, {
      props: { href: '/shops' },
      slots: { default: '去授权' },
    })

    expect(wrapper.find('button').exists()).toBe(false)
    expect(wrapper.get('a').attributes('href')).toBe('/shops')
  })
})
