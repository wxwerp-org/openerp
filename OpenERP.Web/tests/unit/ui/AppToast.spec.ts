import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppToast from '../../../src/ui/AppToast.vue'

describe('AppToast', () => {
  it('Given 提示消息 When 渲染 Then 作为状态区域展示文本', () => {
    const wrapper = mount(AppToast, { props: { message: '个人信息已保存' } })

    expect(wrapper.attributes('id')).toBeUndefined()
    expect(wrapper.attributes('role')).toBe('status')
    expect(wrapper.text()).toBe('个人信息已保存')
    expect(wrapper.attributes('style') ?? '').not.toContain('display: none')
  })

  it('Given 空消息 When 渲染 Then 隐藏但保留在无障碍状态区域', () => {
    const wrapper = mount(AppToast, { props: { message: '' }, attrs: { id: 'toast' } })

    expect(wrapper.attributes('role')).toBe('status')
    expect(wrapper.attributes('id')).toBe('toast')
    expect(wrapper.attributes('style')).toContain('display: none')
  })
})
