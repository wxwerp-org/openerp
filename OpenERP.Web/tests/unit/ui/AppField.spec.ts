import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppField from '../../../src/ui/AppField.vue'

describe('AppField', () => {
  it('Given 表单字段 When 渲染 Then 标签与控件通过 for 关联并展示必填标记', () => {
    const wrapper = mount(AppField, {
      props: { label: '姓名', htmlFor: 'profileName', required: true },
      slots: { default: '<input id="profileName" />' },
    })

    const label = wrapper.get('label')
    expect(label.attributes('for')).toBe('profileName')
    expect(label.text()).toContain('姓名')
    expect(label.text()).toContain('*')
    expect(wrapper.get('input').attributes('id')).toBe('profileName')
  })

  it('Given 字段校验失败 When 渲染 Then 展示可关联的错误说明', () => {
    const wrapper = mount(AppField, {
      props: {
        label: '姓名',
        htmlFor: 'profileName',
        error: '请输入姓名',
        errorId: 'profileNameError',
      },
      slots: { default: '<input id="profileName" />' },
    })

    const error = wrapper.get('[role="alert"]')
    expect(error.attributes('id')).toBe('profileNameError')
    expect(error.text()).toBe('请输入姓名')
  })

  it('Given 无错误 When 渲染 Then 不渲染错误说明', () => {
    const wrapper = mount(AppField, {
      props: { label: '姓名', htmlFor: 'profileName' },
      slots: { default: '<input id="profileName" />' },
    })

    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })
})
