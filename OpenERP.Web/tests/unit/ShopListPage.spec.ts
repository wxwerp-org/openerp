import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

// 统计 /api/erp/shops 的请求：pageSize=1 的是授权状态角标统计，其余是列表查询
const api = vi.hoisted(() => ({ calls: [] as Record<string, unknown>[] }))

const counts: Record<string, Record<string, number>> = {
  '': { valid: 5, invalid: 2, unknown: 1 },
  TemuPop: { valid: 3, invalid: 2, unknown: 0 },
  Amazon: { valid: 2, invalid: 0, unknown: 1 }
}

const json = (body: unknown) =>
  new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } })

vi.mock('@/utils/http', () => ({
  default: {
    get: ({ url }: { url: string }) => {
      if (url === '/api/erp/platforms') {
        return Promise.resolve({
          items: [
            {
              name: 'TemuPop',
              displayName: 'Temu 半托管',
              icon: null,
              isProduction: true,
              shopCount: 5
            },
            { name: 'Amazon', displayName: 'Amazon', icon: null, isProduction: true, shopCount: 3 }
          ]
        })
      }
      return Promise.resolve({ configured: true, orderSyncEnabled: true })
    }
  },
  readErrorMessage: () => Promise.resolve('错误'),
  requestRaw: ({ params }: { params: Record<string, unknown> }) => {
    api.calls.push(params)
    if (params.pageSize === 1) {
      const total = counts[String(params.platform ?? '')]![String(params.status)]!
      return Promise.resolve(
        json({ items: [], page: 1, pageSize: 1, totalCount: total, pageCount: 1 })
      )
    }
    const shop = {
      sid: '1001',
      name: '示例店铺',
      platform: 'TemuPop',
      region: 'US',
      isTokenOk: true
    }
    return Promise.resolve(
      json({ items: [shop], page: 1, pageSize: 20, totalCount: 1, pageCount: 1 })
    )
  }
}))
vi.mock('@/hooks/business/useErpHub', () => ({
  useErpHub: () => ({
    state: ref('connected'),
    status: ref(null),
    connect: () => Promise.resolve(),
    disconnect: () => Promise.resolve(),
    refreshStatus: () => Promise.resolve(),
    startShopSync: () => Promise.resolve({ accepted: true })
  })
}))
vi.mock('@/api/auth', () => ({ account: ref({ role: 'Administrator' }) }))

const ArtTableHeaderStub = defineComponent({
  name: 'ArtTableHeader',
  setup:
    (_, { slots }) =>
    () =>
      h('div', slots.left?.())
})

async function mountPage() {
  const { default: ShopList } = await import('@/views/shop/list/index.vue')
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { render: () => null } }]
  })
  const wrapper = mount(ShopList, {
    global: { plugins: [router], stubs: { ArtTableHeader: ArtTableHeaderStub } }
  })
  await flushPromises()
  return wrapper
}

/** 授权状态单选按钮上的角标文字，按 全部/有效/无效/未知 顺序 */
const badges = (wrapper: Awaited<ReturnType<typeof mountPage>>) =>
  wrapper.findAll('.status-option .status-count').map((badge) => badge.text())

describe('授权店铺页：授权状态角标', () => {
  beforeEach(() => {
    api.calls.length = 0
  })

  it('Given 两个平台 When 打开页面 Then 初始化时一次性统计“全部+各平台”x 三种状态，并显示全部平台的数量', async () => {
    const wrapper = await mountPage()

    const countRequests = api.calls.filter((params) => params.pageSize === 1)
    expect(countRequests).toHaveLength((1 + 2) * 3)
    expect(badges(wrapper)).toEqual(['8', '5', '2', '1'])
  })

  it('Given 已统计完成 When 切换平台标签页 Then 角标换成该平台的数量，且不再发起统计请求', async () => {
    const wrapper = await mountPage()
    const before = api.calls.filter((params) => params.pageSize === 1).length

    await wrapper.findAll('.el-tabs__item')[1]!.trigger('click')
    await flushPromises()

    // 数量为 0 的状态不显示角标
    expect(badges(wrapper)).toEqual(['5', '3', '2'])
    expect(api.calls.filter((params) => params.pageSize === 1)).toHaveLength(before)
    // 切换只重新查询店铺列表
    expect(api.calls.at(-1)).toMatchObject({ platform: 'TemuPop', pageSize: 20 })
  })

  it('Given 平台标签页 When 渲染 Then 标签上不带统计角标，平台以首字母 logo 展示', async () => {
    const wrapper = await mountPage()

    expect(wrapper.findAll('.el-tabs__item .el-badge')).toHaveLength(0)
    const logos = wrapper.findAll('.el-tabs__item .platform-tag__logo').map((logo) => logo.text())
    expect(logos).toEqual(['T', 'A'])
  })
})
