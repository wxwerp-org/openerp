import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppSelect from '../../../src/ui/AppSelect.vue'

describe('AppSelect', () => {
  const options = [
    { value: 20, label: '20条/页' },
    { value: 50, label: '50条/页' },
  ]

  it('Given 选项 When 渲染 Then 使用原生 select 保留选项值类型', () => {
    const wrapper = mount(AppSelect, {
      props: { modelValue: 20, options },
      attrs: { 'aria-label': '每页条数' },
    })

    const select = wrapper.get('select')
    expect(select.attributes('aria-label')).toBe('每页条数')
    expect(select.findAll('option').map((option) => option.text())).toEqual(['20条/页', '50条/页'])
    expect(select.get('option').attributes('value')).toBe('20')
  })

  it('Given 切换选项 When 用户选择 Then 发出原始类型的值', async () => {
    const wrapper = mount(AppSelect, { props: { modelValue: 20, options } })

    await wrapper.get('select').setValue('50')
    expect(wrapper.emitted('update:modelValue')).toEqual([[50]])
  })

  it('Given 禁用状态 When 渲染 Then 原生 select 禁用', () => {
    const wrapper = mount(AppSelect, { props: { modelValue: 20, options, disabled: true } })

    expect((wrapper.get('select').element as HTMLSelectElement).disabled).toBe(true)
  })

  it('Given 不同尺寸 When 渲染 Then 暴露尺寸状态', () => {
    expect(mount(AppSelect, { props: { modelValue: 20, options } }).attributes('data-size')).toBe(
      'md',
    )
    expect(
      mount(AppSelect, { props: { modelValue: 20, options, size: 'sm' } }).attributes('data-size'),
    ).toBe('sm')
  })

  it('Given 额外原生属性 When 渲染 Then 落在原生 select 而不是外层容器', () => {
    const wrapper = mount(AppSelect, {
      props: { modelValue: 20, options },
      attrs: { id: 'pageSize', name: 'pageSize', 'aria-describedby': 'pageSizeHint' },
    })

    const select = wrapper.get('select')
    expect(select.attributes('id')).toBe('pageSize')
    expect(select.attributes('name')).toBe('pageSize')
    expect(select.attributes('aria-describedby')).toBe('pageSizeHint')
  })
})
