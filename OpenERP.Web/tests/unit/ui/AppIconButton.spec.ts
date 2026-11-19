import { mount } from '@vue/test-utils'
import { h } from 'vue'
import { describe, expect, it, vi } from 'vitest'
import AppIcon from '../../../src/ui/AppIcon.vue'
import AppIconButton from '../../../src/ui/AppIconButton.vue'

describe('AppIconButton', () => {
  it('Given 图标按钮 When 渲染 Then 名称同时写入 aria-label 与 title', () => {
    const wrapper = mount(AppIconButton, {
      props: { label: '刷新' },
      slots: { default: () => h(AppIcon, { name: 'i-refresh' }) },
    })

    const button = wrapper.get('button')
    expect(button.attributes('type')).toBe('button')
    expect(button.attributes('aria-label')).toBe('刷新')
    expect(button.attributes('title')).toBe('刷新')
    expect(button.get('svg').attributes('aria-hidden')).toBe('true')
  })

  it('Given 禁用图标按钮 When 点击 Then 不触发点击', async () => {
    const onClick = vi.fn()
    const wrapper = mount(AppIconButton, {
      props: { label: '刷新', disabled: true },
      attrs: { onClick },
    })

    await wrapper.get('button').trigger('click')
    expect(onClick).not.toHaveBeenCalled()
  })
})
