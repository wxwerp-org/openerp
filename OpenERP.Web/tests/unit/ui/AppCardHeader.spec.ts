import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppCardHeader from '../../../src/ui/AppCardHeader.vue'

describe('AppCardHeader', () => {
  it('Given 卡片头 When 渲染 Then 左侧标题与右侧操作同时存在', () => {
    const wrapper = mount(AppCardHeader, {
      slots: { default: '实时销量', right: '操作区' },
    })

    expect(wrapper.text()).toContain('实时销量')
    expect(wrapper.text()).toContain('操作区')
  })
})
