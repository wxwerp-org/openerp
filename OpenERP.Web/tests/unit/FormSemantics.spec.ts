import { mount } from '@vue/test-utils'
import { beforeAll, describe, expect, it } from 'vitest'
import { nextTick } from 'vue'
import PasswordDialog from '../../src/account/PasswordDialog.vue'
import ProfileDialog from '../../src/account/ProfileDialog.vue'
import ProductFormDialog from '../../src/forms/ProductFormDialog.vue'
import BaseConfig from '../../src/settings/BaseConfig.vue'
import { installNativeDialogShim } from './installNativeDialogShim'
import { adminAccount } from './adminAccount'

beforeAll(installNativeDialogShim)

describe('表单与配置语义', () => {
  it('Given 个人信息表单 When 空姓名提交 Then 标签关联、错误关联和首错焦点完整', async () => {
    const wrapper = mount(ProfileDialog, {
      props: { open: true, account: adminAccount },
      attachTo: document.body,
    })
    await nextTick()
    await wrapper.get('#profileName').setValue('  ')
    await wrapper.get('#saveProfile').trigger('click')

    expect(wrapper.find('label[for="profileName"]').exists()).toBe(true)
    const input = wrapper.get('#profileName')
    expect(input.attributes('required')).toBeDefined()
    expect(input.attributes('aria-invalid')).toBe('true')
    expect(input.attributes('aria-describedby')).toBe('profileNameError')
    expect(wrapper.get('#profileNameError').attributes('role')).toBe('alert')
    expect(document.activeElement?.id).toBe('profileName')
  })

  it('Given 密码表单 When 两次密码不一致 Then 确认密码字段获得错误语义和焦点', async () => {
    const wrapper = mount(PasswordDialog, { props: { open: true }, attachTo: document.body })
    await nextTick()
    await wrapper.get('#oldPassword').setValue('old-password')
    await wrapper.get('#newPassword').setValue('new-password')
    await wrapper.get('#confirmPassword').setValue('different')
    await wrapper.get('#savePassword').trigger('click')

    const input = wrapper.get('#confirmPassword')
    expect(wrapper.find('label[for="confirmPassword"]').exists()).toBe(true)
    expect(input.attributes('required')).toBeDefined()
    expect(input.attributes('aria-invalid')).toBe('true')
    expect(input.attributes('aria-describedby')).toBe('confirmPasswordError')
    expect(wrapper.get('#confirmPasswordError').attributes('role')).toBe('alert')
    expect(document.activeElement?.id).toBe('confirmPassword')
  })

  it('Given 添加产品表单 When 初始展示并空值提交 Then 不提前报错且提交后关联品名错误并聚焦', async () => {
    const wrapper = mount(ProductFormDialog, { props: { open: true }, attachTo: document.body })
    await nextTick()

    expect(wrapper.find('[data-invalid="true"]').exists()).toBe(false)
    expect(wrapper.find('label[for="productNameInput"]').exists()).toBe(true)
    expect(wrapper.find('label[for="productSkuInput"]').exists()).toBe(true)
    expect(wrapper.find('label[for="productTypeSelect"]').exists()).toBe(true)

    await wrapper.get('#saveProductForm').trigger('click')
    const input = wrapper.get('#productNameInput')
    expect(input.attributes('required')).toBeDefined()
    expect(input.attributes('aria-invalid')).toBe('true')
    expect(input.attributes('aria-describedby')).toBe('productNameError')
    expect(wrapper.get('#productNameError').attributes('role')).toBe('alert')
    expect(document.activeElement?.id).toBe('productNameInput')
  })

  it('Given 基础配置 When 点击编辑 Then 与通用配置一样发出 edit 事件', async () => {
    const wrapper = mount(BaseConfig)

    await wrapper.get('[data-testid="config-row"] button').trigger('click')

    expect(wrapper.emitted('edit')).toHaveLength(1)
    expect(wrapper.find('input[name]').exists()).toBe(true)
  })
})
