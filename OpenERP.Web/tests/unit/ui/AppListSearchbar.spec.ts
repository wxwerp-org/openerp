import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import AppListSearchbar from '../../../src/ui/AppListSearchbar.vue'

describe('AppListSearchbar', () => {
  it('Given 列表搜索区 When 渲染 Then 使用 search 语义并带可访问名称', () => {
    const wrapper = mount(AppListSearchbar, {
      props: { label: '筛选用户' },
      slots: { default: '筛选项' },
    })

    expect(wrapper.element.tagName).toBe('SEARCH')
    expect(wrapper.attributes('aria-label')).toBe('筛选用户')
    expect(wrapper.text()).toBe('筛选项')
  })

  it('Given 需要回车提交 When 渲染为表单 Then 透传提交事件', async () => {
    const onSubmit = vi.fn((event: Event) => event.preventDefault())
    const wrapper = mount(AppListSearchbar, {
      props: { as: 'form', label: '筛选订单' },
      attrs: { onSubmit },
    })

    expect(wrapper.element.tagName).toBe('FORM')
    await wrapper.get('form').trigger('submit')
    expect(onSubmit).toHaveBeenCalledTimes(1)
  })
})
