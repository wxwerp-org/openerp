import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppInput from '../../../src/ui/AppInput.vue'

describe('AppInput', () => {
  it('Given 受控输入 When 用户输入 Then 发出新值', async () => {
    const wrapper = mount(AppInput, {
      props: { modelValue: '' },
    })

    const input = wrapper.get('input')
    await input.setValue('lucy.lin')
    expect(wrapper.emitted('update:modelValue')).toEqual([['lucy.lin']])
  })

  it('Given 表单输入 When 渲染 Then 原生输入属性透传', () => {
    const wrapper = mount(AppInput, {
      props: {
        id: 'profileName',
        modelValue: '林沁',
        placeholder: '请输入姓名',
        required: true,
        maxlength: 20,
      },
    })

    const input = wrapper.get('input')
    expect(input.attributes('id')).toBe('profileName')
    expect(input.attributes('placeholder')).toBe('请输入姓名')
    expect(input.attributes('required')).toBeDefined()
    expect(input.attributes('maxlength')).toBe('20')
    expect((input.element as HTMLInputElement).value).toBe('林沁')
  })

  it('Given 校验失败或只读 When 渲染 Then 暴露状态且不可编辑', () => {
    const invalid = mount(AppInput, { props: { invalid: true } })
    expect(invalid.get('input').attributes('aria-invalid')).toBe('true')

    const readonly = mount(AppInput, { props: { readonly: true, modelValue: '固定值' } })
    expect((readonly.get('input').element as HTMLInputElement).readOnly).toBe(true)

    const disabled = mount(AppInput, { props: { disabled: true } })
    expect((disabled.get('input').element as HTMLInputElement).disabled).toBe(true)
  })

  it('Given 小尺寸输入 When 渲染 Then 暴露尺寸状态', () => {
    const wrapper = mount(AppInput, { props: { size: 'sm' } })

    expect(wrapper.attributes('data-size')).toBe('sm')
  })
})
