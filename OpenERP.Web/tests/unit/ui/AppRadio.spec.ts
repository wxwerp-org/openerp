import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppRadio from '../../../src/ui/AppRadio.vue'

describe('AppRadio', () => {
  it('Given 单选组 When 渲染 Then 选中项与模型值一致并带分组名', () => {
    const wrapper = mount(AppRadio, {
      props: { modelValue: 'b', value: 'a', name: 'mode', label: '自动' },
    })

    const input = wrapper.get('input[type="radio"]')
    expect(input.attributes('name')).toBe('mode')
    expect((input.element as HTMLInputElement).checked).toBe(false)
    expect(wrapper.text()).toBe('自动')
  })

  it('Given 点击未选项 When 触发变更 Then 发出该选项值', async () => {
    const wrapper = mount(AppRadio, { props: { modelValue: 'a', value: 'b' } })

    await wrapper.get('input').setValue(true)
    expect(wrapper.emitted('update:modelValue')).toEqual([['b']])
  })

  it('Given 禁用单选 When 渲染 Then 原生禁用且保留文案', () => {
    const wrapper = mount(AppRadio, {
      props: { modelValue: 'a', value: 'a', disabled: true, label: '手动' },
    })

    expect((wrapper.get('input').element as HTMLInputElement).disabled).toBe(true)
    expect(wrapper.text()).toBe('手动')
  })
})
