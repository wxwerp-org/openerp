import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppSegmented from '../../../src/ui/AppSegmented.vue'

describe('AppSegmented', () => {
  const options = ['全部', '普通产品', '组合产品']

  it('Given 分段控件 When 渲染 Then 画布内按钮暴露当前选中状态', () => {
    const wrapper = mount(AppSegmented, {
      props: { options, modelValue: '普通产品' },
      attrs: { 'aria-label': '产品类型' },
    })

    const group = wrapper.get('[role="group"]')
    expect(group.attributes('aria-label')).toBe('产品类型')

    const buttons = group.findAll('button')
    expect(buttons).toHaveLength(3)
    for (const button of buttons) {
      expect(button.attributes('type')).toBe('button')
    }
    expect(buttons.map((button) => button.attributes('aria-pressed'))).toEqual([
      'false',
      'true',
      'false',
    ])
  })

  it('Given 分段控件 When 点击未选项 Then 只发出新值事件', async () => {
    const wrapper = mount(AppSegmented, {
      props: { options, modelValue: '全部' },
      attrs: { 'aria-label': '产品类型' },
    })

    const target = wrapper.findAll('button').find((button) => button.text() === '组合产品')!
    await target.trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([['组合产品']])
  })

  it('Given 只有一项被选中 When 切换 Then 选中态始终唯一', async () => {
    const wrapper = mount(AppSegmented, {
      props: { options, modelValue: '全部' },
      attrs: { 'aria-label': '产品类型' },
    })

    await wrapper.findAll('button')[1]!.trigger('click')
    await wrapper.setProps({ modelValue: '普通产品' })

    const pressed = wrapper
      .findAll('button')
      .filter((button) => button.attributes('aria-pressed') === 'true')
    expect(pressed).toHaveLength(1)
    expect(pressed[0]!.text()).toBe('普通产品')
  })

  it('Given 尺寸变体 When 渲染 Then 暴露尺寸状态', () => {
    const wrapper = mount(AppSegmented, {
      props: { options, modelValue: '全部', size: 'md' },
      attrs: { 'aria-label': '产品类型' },
    })

    expect(wrapper.attributes('data-size')).toBe('md')
  })
})
