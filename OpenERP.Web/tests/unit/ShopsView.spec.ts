import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import ShopsView from '../../src/views/ShopsView.vue'

type Shop = {
  sid: string
  name: string
  platform: string
  region: string | null
  isTokenOk: boolean | null
  statusError: string | null
}

const baseShop: Shop = {
  sid: '42',
  name: '测试店铺',
  platform: 'Temu',
  region: 'US',
  isTokenOk: true,
  statusError: null,
}

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function configBody(overrides: Record<string, unknown> = {}) {
  return {
    configured: true,
    baseUrl: 'https://erp.test',
    orderSyncEnabled: false,
    ...overrides,
  }
}

function shopsBody(
  items: Shop[],
  overrides: { page?: number; pageSize?: number; totalCount?: number; pageCount?: number } = {},
) {
  return {
    items,
    page: overrides.page ?? 1,
    pageSize: overrides.pageSize ?? 20,
    totalCount: overrides.totalCount ?? items.length,
    pageCount: overrides.pageCount ?? (items.length > 0 ? 1 : 0),
  }
}

function stubbedShopPage(pageCount: number, totalCount = 45) {
  const requested: string[] = []
  const fetchMock = stubFetch((url) => {
    if (url === '/api/erp/config') return jsonResponse(configBody())
    const params = new URL(url, 'https://local.test').searchParams
    const page = Number(params.get('page'))
    const pageSize = Number(params.get('pageSize'))
    requested.push(`page=${page}&pageSize=${pageSize}`)
    return jsonResponse(
      shopsBody(
        Array.from({ length: pageSize }, (_, index): Shop => ({
          ...baseShop,
          sid: String((page - 1) * pageSize + index + 1),
          name: `测试店铺 ${(page - 1) * pageSize + index + 1}`,
        })),
        { page, pageSize, totalCount, pageCount },
      ),
    )
  })
  return { requested, fetchMock }
}

type Handler = (url: string, init?: RequestInit) => Response | Promise<Response>

function stubFetch(handler: Handler) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) =>
    handler(String(input), init),
  )
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((fulfill) => {
    resolve = fulfill
  })
  return { promise, resolve }
}

function shopListCalls(fetchMock: ReturnType<typeof stubFetch>) {
  return fetchMock.mock.calls.filter(([input]) => String(input).startsWith('/api/erp/shops?'))
}

async function mountShops(): Promise<VueWrapper> {
  const wrapper = mount(ShopsView)
  await flushPromises()
  return wrapper
}

describe('授权店铺页', () => {
  let wrapper: VueWrapper | undefined

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    vi.unstubAllGlobals()
  })

  it('Given 配置已就绪 When 打开授权店铺 Then 使用共享表格结构且没有 Element Plus 节点', async () => {
    stubFetch((url) => {
      if (url === '/api/erp/config') return jsonResponse(configBody())
      if (url.startsWith('/api/erp/shops?')) return jsonResponse(shopsBody([baseShop]))
      throw new Error(`意外的请求：${url}`)
    })
    wrapper = await mountShops()

    expect(wrapper.find('.el-table').exists()).toBe(false)
    expect(wrapper.find('.el-pagination').exists()).toBe(false)
    expect(wrapper.find('[class^="el-"], [class*=" el-"]').exists()).toBe(false)
    expect(wrapper.get('.page').classes()).toContain('page-shops')
    expect(wrapper.get('.page').attributes('aria-labelledby')).toBe('shops-title')
    expect(wrapper.get('h1').attributes('id')).toBe('shops-title')
    expect(wrapper.get('h1').text()).toBe('授权店铺')
    expect(wrapper.get('.grid-table').classes()).toContain('is-wrap')
    expect(wrapper.get('.table-scroll').classes()).toContain('is-y')

    const scroll = wrapper.get('.table-scroll')
    expect(scroll.attributes('role')).toBe('region')
    expect(scroll.attributes('tabindex')).toBe('0')
    expect(scroll.attributes('aria-label')).toBe('授权店铺表格')
    expect(wrapper.get('caption').text()).toBe('授权店铺列表')
    expect(wrapper.findAll('th[scope="col"]')).toHaveLength(5)
    expect(wrapper.findAll('tbody tr')).toHaveLength(1)

    expect(wrapper.text()).toContain('共 1 家店铺')
    expect(wrapper.text()).toContain('，共 1 页')
    expect(wrapper.text()).toContain('私有 ERP AccessToken 已配置')
    expect(wrapper.text()).toContain('店铺列表已更新')
    expect(wrapper.get('button[aria-label="刷新"]').attributes('disabled')).toBeUndefined()
    expect(wrapper.get('.table-pagination').text()).toContain('共 1 条')
  })

  it('Given 未配置 Token When 加载店铺页 Then 引导到系统设置并保留空态且不请求店铺列表', async () => {
    const fetchMock = stubFetch((url) => {
      if (url === '/api/erp/config') return jsonResponse(configBody({ configured: false }))
      throw new Error(`意外的请求：${url}`)
    })
    wrapper = await mountShops()

    expect(wrapper.text()).toContain('尚未配置私有 ERP AccessToken')
    expect(wrapper.text()).toContain('请前往系统设置 → 基础 → ERP 接入填写 Token 后重试。')
    expect(wrapper.text()).toContain('配置完成后，这里会显示 TemuPop 授权店铺')
    expect(wrapper.text()).toContain('完成 ERP 接入设置后可查看店铺')
    expect(wrapper.get('.notice').classes()).toContain('amber')
    expect(wrapper.get('.notice').attributes('role')).toBe('alert')
    expect(shopListCalls(fetchMock)).toHaveLength(0)
  })

  it('Given 配置读取失败 When 加载 Then 显示错误提示与重试入口', async () => {
    stubFetch((url) => {
      if (url === '/api/erp/config') {
        return jsonResponse({ title: '配置服务不可用', detail: '稍后再试' }, 500)
      }
      throw new Error(`意外的请求：${url}`)
    })
    wrapper = await mountShops()

    expect(wrapper.text()).toContain('配置服务不可用：稍后再试')
    expect(wrapper.get('.notice').classes()).toContain('red')
    expect(wrapper.get('.notice').attributes('role')).toBe('alert')
    expect(wrapper.get('button[aria-label="上一页"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('button[aria-label="重试"]').exists()).toBe(true)
  })

  it('Given 店铺列表请求失败 When 加载 Then 显示既有错误文案与重试入口', async () => {
    stubFetch((url) => {
      if (url === '/api/erp/config') return jsonResponse(configBody())
      return new Response('boom', { status: 500 })
    })
    wrapper = await mountShops()

    expect(wrapper.text()).toContain('无法获取授权店铺（HTTP 500）')
    expect(wrapper.get('.notice').classes()).toContain('red')
    expect(wrapper.get('.notice').attributes('role')).toBe('alert')
    expect(wrapper.get('button[aria-label="上一页"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('button[aria-label="重试"]').exists()).toBe(true)
  })

  it.each([
    { isTokenOk: true, label: '有效', className: 'green' },
    { isTokenOk: false, label: '无效', className: 'red' },
    { isTokenOk: null, label: '未知', className: 'gray' },
  ])(
    'Given 授权状态 $label When 渲染 Then 使用对应状态标签并展示说明',
    async ({ isTokenOk, label, className }) => {
      stubFetch((url) => {
        if (url === '/api/erp/config') return jsonResponse(configBody())
        return jsonResponse(shopsBody([{ ...baseShop, isTokenOk, statusError: '授权校验失败' }]))
      })
      wrapper = await mountShops()

      const tag = wrapper.findAll('.tag').find((candidate) => candidate.text() === label)
      expect(tag).toBeDefined()
      expect(tag!.classes()).toContain(className)
      expect(wrapper.text()).toContain('授权校验失败')
    },
  )

  it('Given 列表为空 When 加载成功 Then 显示暂无授权店铺且分页栏可见', async () => {
    stubFetch((url) => {
      if (url === '/api/erp/config') return jsonResponse(configBody())
      return jsonResponse(shopsBody([], { pageCount: 0 }))
    })
    wrapper = await mountShops()

    expect(wrapper.get('.empty-overlay').text()).toContain('暂无授权店铺')
    expect(wrapper.findAll('tbody tr')).toHaveLength(0)
    expect(wrapper.get('.table-pagination').text()).toContain('共 0 条')
    expect(wrapper.get('button[aria-label="上一页"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[aria-label="下一页"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('.page-size select').attributes('disabled')).toBeDefined()
    expect(wrapper.get('.page-jump input').attributes('disabled')).toBeDefined()
  })

  it('Given 多页店铺 When 切换页码 Then 按对应页码请求并同步当前页与边界禁用', async () => {
    const { requested } = stubbedShopPage(3)
    wrapper = await mountShops()

    expect(wrapper.get('.page-num').text()).toBe('1')
    expect(wrapper.get('.page-num').attributes('aria-current')).toBe('page')
    expect(wrapper.get('button[aria-label="上一页"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[aria-label="下一页"]').attributes('disabled')).toBeUndefined()

    await wrapper.get('button[aria-label="下一页"]').trigger('click')
    await flushPromises()

    expect(requested.at(-1)).toBe('page=2&pageSize=20')
    expect(wrapper.get('.page-num').text()).toBe('2')

    const thirdPage = wrapper.findAll('.page-btn').find((button) => button.text() === '3')
    expect(thirdPage).toBeDefined()
    await thirdPage!.trigger('click')
    await flushPromises()

    expect(requested.at(-1)).toBe('page=3&pageSize=20')
    expect(wrapper.get('.page-num').text()).toBe('3')
    expect(wrapper.get('button[aria-label="下一页"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[aria-label="上一页"]').attributes('disabled')).toBeUndefined()
  })

  it('Given 停在第 3 页 When 刷新后仅剩 1 页 Then 自动请求第 1 页并标记当前页', async () => {
    const requested: string[] = []
    let shrunk = false
    stubFetch((url) => {
      if (url === '/api/erp/config') return jsonResponse(configBody())
      const params = new URL(url, 'https://local.test').searchParams
      const page = Number(params.get('page'))
      const pageSize = Number(params.get('pageSize'))
      requested.push(`page=${page}&pageSize=${pageSize}`)
      if (shrunk) {
        return jsonResponse(shopsBody([baseShop], { page, pageSize, totalCount: 1, pageCount: 1 }))
      }
      return jsonResponse(
        shopsBody(
          Array.from({ length: pageSize }, (_, index): Shop => ({
            ...baseShop,
            sid: String((page - 1) * pageSize + index + 1),
            name: `测试店铺 ${(page - 1) * pageSize + index + 1}`,
          })),
          { page, pageSize, totalCount: 45, pageCount: 3 },
        ),
      )
    })
    wrapper = await mountShops()
    await wrapper.get('button[aria-label="下一页"]').trigger('click')
    await flushPromises()
    await wrapper.get('button[aria-label="下一页"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('.page-num').text()).toBe('3')

    shrunk = true
    await wrapper.get('button[aria-label="刷新"]').trigger('click')
    await flushPromises()

    expect(requested.at(-1)).toBe('page=1&pageSize=20')
    expect(wrapper.get('.page-num').text()).toBe('1')
    expect(wrapper.get('.page-num').attributes('aria-current')).toBe('page')
    expect(wrapper.findAll('tbody tr')).toHaveLength(1)
    expect(wrapper.text()).not.toContain('暂无授权店铺')
  })

  it('Given 多页店铺 When 修改每页条数 Then 回到第 1 页并按新条数请求', async () => {
    const { requested } = stubbedShopPage(3)
    wrapper = await mountShops()
    await wrapper.get('button[aria-label="下一页"]').trigger('click')
    await flushPromises()

    await wrapper.get('.page-size select').setValue('50')
    await flushPromises()

    expect(requested.at(-1)).toBe('page=1&pageSize=50')
    expect((wrapper.get('.page-size select').element as HTMLSelectElement).value).toBe('50')
  })

  it('Given 多页店铺 When 输入页码并回车或失焦 Then 钳制到有效范围', async () => {
    const { requested } = stubbedShopPage(3)
    wrapper = await mountShops()
    const input = wrapper.get('.page-jump input')

    await input.setValue('5')
    await input.trigger('keyup.enter')
    await flushPromises()

    expect(requested.at(-1)).toBe('page=3&pageSize=20')
    expect(wrapper.get('.page-num').text()).toBe('3')
    expect((wrapper.get('.page-jump input').element as HTMLInputElement).value).toBe('3')

    await input.setValue('0')
    await input.trigger('blur')
    await flushPromises()

    expect(requested.at(-1)).toBe('page=1&pageSize=20')
    expect(wrapper.get('.page-num').text()).toBe('1')

    const callCount = requested.length
    await input.setValue('abc')
    await input.trigger('blur')
    await flushPromises()

    expect(requested).toHaveLength(callCount)
    expect((wrapper.get('.page-jump input').element as HTMLInputElement).value).toBe('1')
  })

  it('Given 店铺列表加载中 When 查看分页 Then 分页栏保留且全部禁用', async () => {
    const shopsResponse = (() => {
      let resolve!: (value: Response) => void
      const promise = new Promise<Response>((fulfill) => {
        resolve = fulfill
      })
      return { promise, resolve }
    })()
    stubFetch((url) => {
      if (url === '/api/erp/config') return jsonResponse(configBody())
      return shopsResponse.promise
    })
    const pendingWrapper = mount(ShopsView)
    wrapper = pendingWrapper
    await flushPromises()

    expect(wrapper.find('.table-pagination').exists()).toBe(true)
    expect(wrapper.get('[role="status"]').text()).toContain('正在加载店铺列表…')
    for (const button of wrapper.findAll('.table-pagination button')) {
      expect(button.attributes('disabled')).toBeDefined()
    }
    expect(wrapper.get('.table-pagination select').attributes('disabled')).toBeDefined()
    expect(wrapper.get('.page-jump input').attributes('disabled')).toBeDefined()

    shopsResponse.resolve(jsonResponse(shopsBody([baseShop])))
    await flushPromises()
    expect(wrapper.get('button[aria-label="刷新"]').attributes('disabled')).toBeUndefined()
  })

  it('Given 已加载多页店铺 When 切换页码加载中 Then 分页保留总数与当前页并禁用', async () => {
    let shopsCall = 0
    const secondPage = deferred<Response>()
    stubFetch((url) => {
      if (url === '/api/erp/config') return jsonResponse(configBody())
      shopsCall += 1
      if (shopsCall === 1) {
        return jsonResponse(
          shopsBody(
            Array.from({ length: 20 }, (_, index) => ({ ...baseShop, sid: String(index + 1) })),
            { totalCount: 45, pageCount: 3 },
          ),
        )
      }
      return secondPage.promise
    })
    wrapper = await mountShops()

    expect(wrapper.get('.page-num').text()).toBe('1')
    expect(wrapper.get('.table-pagination').text()).toContain('共 45 条')

    await wrapper.get('button[aria-label="下一页"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.get('.table-pagination').text()).toContain('共 45 条')
    expect(wrapper.get('.page-num').text()).toBe('2')
    for (const button of wrapper.findAll('.table-pagination button')) {
      expect(button.attributes('disabled')).toBeDefined()
    }

    secondPage.resolve(
      jsonResponse(
        shopsBody(
          Array.from({ length: 20 }, (_, index) => ({ ...baseShop, sid: String(index + 21) })),
          { page: 2, totalCount: 45, pageCount: 3 },
        ),
      ),
    )
    await flushPromises()
    expect(wrapper.get('.page-num').text()).toBe('2')
    expect(wrapper.findAll('tbody tr')).toHaveLength(20)
  })

  it('Given 同步能力关闭 When 加载 Then 不渲染历史订单列与同步按钮', async () => {
    stubFetch((url) => {
      if (url === '/api/erp/config') return jsonResponse(configBody())
      return jsonResponse(shopsBody([baseShop]))
    })
    wrapper = await mountShops()

    expect(wrapper.findAll('th').map((header) => header.text())).not.toContain('历史订单')
    expect(wrapper.findAll('button').some((button) => button.text().includes('同步历史订单'))).toBe(
      false,
    )
  })
})
