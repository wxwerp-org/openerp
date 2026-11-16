import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { beforeAll, describe, expect, it } from 'vitest'
import PasswordDialog from '../../src/account/PasswordDialog.vue'
import ProfileDialog from '../../src/account/ProfileDialog.vue'
import OrderNoticeDialog from '../../src/forms/OrderNoticeDialog.vue'
import ProductFormDialog from '../../src/forms/ProductFormDialog.vue'
import { installNativeDialogShim } from './installNativeDialogShim'
import { adminAccount } from './adminAccount'

beforeAll(installNativeDialogShim)

describe('原生弹窗语义', () => {
  it.each([
    ['添加产品', ProductFormDialog, 'productFormLayer'],
    ['订单公告', OrderNoticeDialog, 'noticeLayer'],
    ['个人信息', ProfileDialog, 'profileLayer'],
    ['修改密码', PasswordDialog, 'passwordLayer'],
  ])('Given %s弹窗 When 使用模态行为 Then 使用原生 dialog 且不重复声明 aria-modal', async (_, component, id) => {
    const wrapper = mount(component, {
      props: component === ProfileDialog ? { open: true, account: adminAccount } : { open: true },
    })
    await nextTick()
    const dialog = wrapper.get(`#${id}`)

    expect(dialog.element.tagName).toBe('DIALOG')
    expect(dialog.attributes('aria-modal')).toBeUndefined()
    expect(dialog.attributes('open')).toBeDefined()
  })
})
