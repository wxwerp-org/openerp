import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppPagination from '../../../src/ui/AppPagination.vue'

function mountPagination(props: Record<string, unknown> = {}) {
  return mount(AppPagination, {
    props: { page: 2, pageCount: 7, pageSize: 20, totalText: '共 130 条', ...props },
  })
}

const jumpInput = 'input[aria-label="前往页码"]'

describe('AppPagination', () => {
  it('Given 多页数据 When 渲染 Then 展示总数、翻页、每页条数与前往页码', () => {
    const wrapper = mountPagination()

    expect(wrapper.text()).toContain('共 130 条')
    expect(wrapper.find('button[aria-label="上一页"]').exists()).toBe(true)
    expect(wrapper.get('select[aria-label="每页条数"]').text()).toContain('20条/页')
    expect(wrapper.get(jumpInput).element).toHaveProperty('value', '2')
  })

  it('Given 未提供总数文字 When 渲染 Then 不显示总数', () => {
    const wrapper = mountPagination({ totalText: undefined })

    expect(wrapper.text()).not.toContain('共')
  })

  it('Given 输入页码 When 按回车 Then 提交该页', async () => {
    const wrapper = mountPagination()

    await wrapper.get(jumpInput).setValue('5')
    await wrapper.get(jumpInput).trigger('keydown.enter')

    expect(wrapper.emitted('update:page')).toEqual([[5]])
  })

  it('Given 页码超出范围 When 提交 Then 收敛到最大页并回写输入框', async () => {
    const wrapper = mountPagination()

    await wrapper.get(jumpInput).setValue('99')
    await wrapper.get(jumpInput).trigger('keydown.enter')

    expect(wrapper.emitted('update:page')).toEqual([[7]])
    expect(wrapper.get(jumpInput).element).toHaveProperty('value', '7')
  })

  it('Given 输入非数字 When 失焦 Then 不翻页并恢复当前页', async () => {
    const wrapper = mountPagination()

    await wrapper.get(jumpInput).setValue('abc')
    await wrapper.get(jumpInput).trigger('blur')

    expect(wrapper.emitted('update:page')).toBeUndefined()
    expect(wrapper.get(jumpInput).element).toHaveProperty('value', '2')
  })

  it('Given 目标页就是当前页 When 提交 Then 不重复请求', async () => {
    const wrapper = mountPagination()

    await wrapper.get(jumpInput).setValue('2')
    await wrapper.get(jumpInput).trigger('keydown.enter')

    expect(wrapper.emitted('update:page')).toBeUndefined()
  })

  it('Given 加载中 When 输入框失焦 Then 忽略提交并保持当前页', async () => {
    const wrapper = mountPagination()

    await wrapper.get(jumpInput).setValue('6')
    await wrapper.setProps({ disabled: true })
    // Chromium 在输入框因加载被禁用时会触发 blur；test-utils 的 trigger 不会向禁用元素派发事件，因此直接派发。
    wrapper.get(jumpInput).element.dispatchEvent(new Event('blur'))
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('update:page')).toBeUndefined()
    expect(wrapper.get(jumpInput).element).toHaveProperty('value', '2')
    expect(wrapper.get('select[aria-label="每页条数"]').attributes('disabled')).toBeDefined()
  })

  it('Given 选择每页条数 When 切换 Then 发出新的页大小', async () => {
    const wrapper = mountPagination()

    await wrapper.get('select[aria-label="每页条数"]').setValue('50')

    expect(wrapper.emitted('update:pageSize')).toEqual([[50]])
  })

  it('Given 外部页码变化 When 更新 Then 输入框同步', async () => {
    const wrapper = mountPagination()

    await wrapper.setProps({ page: 4 })

    expect(wrapper.get(jumpInput).element).toHaveProperty('value', '4')
  })
})
