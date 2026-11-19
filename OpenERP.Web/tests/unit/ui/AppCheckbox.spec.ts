import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppCheckbox from '../../../src/ui/AppCheckbox.vue'

describe('AppCheckbox', () => {
  it('Given 受控复选框 When 勾选 Then 发出布尔值', async () => {
    const wrapper = mount(AppCheckbox, { props: { modelValue: false } })

    const input = wrapper.get('input[type="checkbox"]')
    await input.setValue(true)
    expect(wrapper.emitted('update:modelValue')).toEqual([[true]])
  })

  it('Given 无障碍名称 When 渲染 Then 名称落在原生复选框上', () => {
    const wrapper = mount(AppCheckbox, { attrs: { 'aria-label': '全选' } })

    expect(wrapper.get('input').attributes('aria-label')).toBe('全选')
  })

  it('Given 带文案复选框 When 渲染 Then 文案与控件成对出现', () => {
    const wrapper = mount(AppCheckbox, { props: { label: '启用同步' } })

    expect(wrapper.element.tagName).toBe('LABEL')
    expect(wrapper.get('input').element.parentElement?.tagName).toBe('LABEL')
    expect(wrapper.text()).toBe('启用同步')
  })

  it('Given 禁用复选框 When 渲染 Then 原生禁用', () => {
    const wrapper = mount(AppCheckbox, { props: { disabled: true } })

    expect((wrapper.get('input').element as HTMLInputElement).disabled).toBe(true)
  })
})
