import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import AppDialog from '../../../src/ui/AppDialog.vue'
import { installNativeDialogShim } from '../installNativeDialogShim'

beforeAll(installNativeDialogShim)

describe('AppDialog', () => {
  it('Given 打开弹窗 When 渲染 Then 使用原生 dialog 且不重复声明 aria-modal', async () => {
    const wrapper = mount(AppDialog, {
      props: { open: true, id: 'profileLayer', ariaLabelledby: 'profileTitle' },
      slots: { default: '<h2 id="profileTitle">修改个人信息</h2>' },
    })
    await nextTick()

    const dialog = wrapper.get('dialog')
    expect(dialog.attributes('open')).toBeDefined()
    expect(dialog.attributes('aria-modal')).toBeUndefined()
    expect(dialog.attributes('aria-labelledby')).toBe('profileTitle')
    expect(dialog.attributes('data-layer')).toBe('center')
    expect(wrapper.text()).toContain('修改个人信息')
  })

  it('Given 打开弹窗 When 用户按 Esc Then 阻断原生关闭并请求宿主关闭', async () => {
    const wrapper = mount(AppDialog, {
      props: { open: true, id: 'testLayer', ariaLabel: '测试弹窗' },
    })
    await nextTick()

    const cancel = new Event('cancel', { cancelable: true })
    const dialog = wrapper.get('dialog')
    dialog.element.dispatchEvent(cancel)

    expect(cancel.defaultPrevented).toBe(true)
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('Given 不可关闭弹窗 When 用户按 Esc Then 阻断关闭且不请求宿主动作', async () => {
    const wrapper = mount(AppDialog, {
      props: { open: true, id: 'testLayer', closable: false, ariaLabel: '测试弹窗' },
    })
    await nextTick()

    const cancel = new Event('cancel', { cancelable: true })
    wrapper.get('dialog').element.dispatchEvent(cancel)

    expect(cancel.defaultPrevented).toBe(true)
    expect(wrapper.emitted('close')).toBeUndefined()
  })

  it('Given 弹窗已关闭 When 关闭事件触发 Then 通知宿主收尾', async () => {
    const wrapper = mount(AppDialog, {
      props: { open: true, id: 'testLayer', ariaLabel: '测试弹窗' },
    })
    await nextTick()

    ;(wrapper.get('dialog').element as HTMLDialogElement).close()
    expect(wrapper.emitted('closed')).toHaveLength(1)
  })

  it('Given 打开且无可访问名称 When 渲染 Then 开发期发出告警', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    mount(AppDialog, { props: { open: true, id: 'unnamedLayer' } })
    await nextTick()

    expect(warn).toHaveBeenCalledWith(expect.stringContaining('缺少可访问名称'))
    warn.mockRestore()
  })

  it('Given 分层弹窗 When 渲染 Then 暴露层类型与遮罩供定位样式使用', () => {
    const wrapper = mount(AppDialog, {
      props: { open: false, id: 'formLayer', layer: 'form' },
    })
    expect(wrapper.get('dialog').attributes('data-layer')).toBe('form')
    expect(wrapper.get('dialog').attributes('data-backdrop')).toBe('none')

    const dimmed = mount(AppDialog, {
      props: { open: false, id: 'shopLayer', backdrop: 'dim' },
    })
    expect(dimmed.get('dialog').attributes('data-backdrop')).toBe('dim')
  })
})
