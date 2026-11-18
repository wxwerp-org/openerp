import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import router from '../../src/router'
import NotFoundView from '../../src/views/NotFoundView.vue'

describe('未知路径', () => {
  it('Given 部署后的 SPA 路由表 When 解析未知路径 Then 落到 not-found 路由', () => {
    expect(router.resolve('/不存在的页面').name).toBe('not-found')
  })

  it('Given 页面不存在视图 When 点击返回首页 Then 回到首页', async () => {
    const localRouter = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        { path: '/:pathMatch(.*)*', name: 'not-found', component: NotFoundView },
      ],
    })
    await localRouter.push('/不存在的页面')
    await localRouter.isReady()

    const wrapper = mount(NotFoundView, { global: { plugins: [localRouter] } })
    await wrapper.get('button').trigger('click')
    await flushPromises()

    expect(localRouter.currentRoute.value.path).toBe('/')
    wrapper.unmount()
  })
})
