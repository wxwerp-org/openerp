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
      props: { modelValue: '林沁' },
      attrs: {
        id: 'profileName',
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

  it('Given 额外原生属性 When 渲染 Then 落在原生输入而不是外层容器', () => {
    const wrapper = mount(AppInput, {
      attrs: {
        inputmode: 'numeric',
        pattern: '[0-9]*',
        'aria-label': '验证码',
        'aria-describedby': 'codeHint',
      },
    })

    const input = wrapper.get('input')
    expect(input.attributes('inputmode')).toBe('numeric')
    expect(input.attributes('pattern')).toBe('[0-9]*')
    expect(input.attributes('aria-label')).toBe('验证码')
    expect(input.attributes('aria-describedby')).toBe('codeHint')
    expect(wrapper.attributes('aria-label')).toBeUndefined()
  })

  it('Given 中文输入法组合中 When 输入 Then 不提前更新模型，组合结束再提交', async () => {
    const wrapper = mount(AppInput, { props: { modelValue: '' } })
    const input = wrapper.get('input')

    await input.trigger('compositionstart')
    await input.setValue('林')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()

    ;(input.element as HTMLInputElement).value = '林沁'
    await input.trigger('compositionend')
    expect(wrapper.emitted('update:modelValue')).toEqual([['林沁']])
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
