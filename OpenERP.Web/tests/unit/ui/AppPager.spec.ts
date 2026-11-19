import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppPager from '../../../src/ui/AppPager.vue'

describe('AppPager', () => {
  it('Given 多页分页 When 渲染 Then 当前页可识别且翻页按钮可用', () => {
    const wrapper = mount(AppPager, { props: { page: 3, pageCount: 10 } })

    expect(wrapper.element.tagName).toBe('NAV')
    expect(wrapper.attributes('aria-label')).toBe('分页')
    expect(wrapper.get('[aria-current="page"]').text()).toBe('3')
    expect(wrapper.get('button[aria-label="上一页"]').attributes('disabled')).toBeUndefined()
    expect(wrapper.get('button[aria-label="下一页"]').attributes('disabled')).toBeUndefined()
  })

  it('Given 点击下一页 When 未到末页 Then 发出目标页码', async () => {
    const wrapper = mount(AppPager, { props: { page: 2, pageCount: 10 } })

    await wrapper.get('button[aria-label="下一页"]').trigger('click')
    expect(wrapper.emitted('update:page')).toEqual([[3]])
  })

  it('Given 只有一页 When 渲染 Then 上下页均禁用', () => {
    const wrapper = mount(AppPager, { props: { page: 1, pageCount: 1 } })

    expect(wrapper.get('button[aria-label="上一页"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[aria-label="下一页"]').attributes('disabled')).toBeDefined()
  })

  it('Given 页数较多 When 渲染 Then 中段折叠为省略号', () => {
    const wrapper = mount(AppPager, { props: { page: 5, pageCount: 10 } })

    expect(wrapper.text()).toContain('…')
    const pages = wrapper.findAll('button').filter((button) => /^\d+$/.test(button.text()))
    expect(pages.map((button) => button.text())).toContain('4')
  })

  it('Given 只保留翻页 When 关闭页码列表 Then 不渲染数字页按钮', () => {
    const wrapper = mount(AppPager, {
      props: { page: 1, pageCount: 3, showPageList: false },
    })

    expect(wrapper.findAll('button').filter((button) => /^\d+$/.test(button.text()))).toHaveLength(
      0,
    )
  })
})
