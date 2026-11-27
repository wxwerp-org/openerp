import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const fetchErpConfig = vi.fn()
const saveErpToken = vi.fn()
const validateErpToken = vi.fn()
const revokeErpConfig = vi.fn()
const fetchPlatformAbilities = vi.fn()
const fetchPlatformApiSchema = vi.fn()
vi.mock('@/api/erp', () => ({
  fetchErpConfig,
  saveErpToken,
  validateErpToken,
  revokeErpConfig,
  fetchPlatformAbilities,
  fetchPlatformApiSchema
}))

const { route, replace } = vi.hoisted(() => ({
  route: { query: {} as Record<string, string> },
  replace: vi.fn()
}))
vi.mock('vue-router', () => ({
  useRoute: () => route,
  useRouter: () => ({ replace })
}))

const { messageSuccess, messageError, confirm } = vi.hoisted(() => ({
  messageSuccess: vi.fn(),
  messageError: vi.fn(),
  confirm: vi.fn()
}))
vi.mock('element-plus', async (importOriginal) => {
  const actual = await importOriginal<typeof import('element-plus')>()
  return {
    ...actual,
    ElMessage: Object.assign(vi.fn(), { success: messageSuccess, error: messageError }),
    ElMessageBox: { confirm }
  }
})

const cfg = (configured: boolean, independent = false) => ({
  configured,
  independent,
  baseUrl: independent ? 'https://123.wxwerp.com' : ''
})

async function mountPage() {
  const { default: Page } = await import('@/views/system/erp-binding/index.vue')
  // 抽屉通过 Teleport 渲染到 body；用就地渲染的替身便于断言其内容。
  const ElDrawer = {
    props: ['modelValue'],
    template: '<div v-if="modelValue" class="drawer"><slot /></div>'
  }
  const wrapper = mount(Page, { global: { stubs: { ElDrawer } } })
  await flushPromises()
  return wrapper
}

type Wrapper = Awaited<ReturnType<typeof mountPage>>
const button = (wrapper: Wrapper, text: string) =>
  wrapper.findAll('button').find((b) => b.text().replace(/\s/g, '') === text)

describe('API权限绑定页', () => {
  beforeEach(() => {
    for (const fn of [
      fetchErpConfig,
      saveErpToken,
      validateErpToken,
      revokeErpConfig,
      fetchPlatformAbilities,
      fetchPlatformApiSchema,
      replace,
      messageSuccess,
      messageError,
      confirm
    ])
      fn.mockReset()
    route.query = {}
  })

  it('Given 地址带 tab=abilities&platform=ozon When 打开页面 Then 展示该平台的接口数量、分类与接口，查看文档读取 Schema', async () => {
    fetchErpConfig.mockResolvedValue(cfg(true))
    route.query = { tab: 'abilities', platform: 'ozon' }
    const ability = (key: string, name: string, apis: unknown[] = []) => ({
      key,
      name,
      description: `${name} 说明`,
      officialUrl: 'https://docs.ozon.ru/api/seller/',
      apiPath: `/open-api/agent/${key}/invoke`,
      requestExample: '{"sid":0}',
      steps: ['传入 AccessToken'],
      categories: [{ id: 1, name: '促销活动', count: 1, frozen: false }],
      apis
    })
    fetchPlatformAbilities.mockResolvedValue([
      ability('shopee', 'Shopee'),
      ability('ozon', 'Ozon', [
        {
          name: 'v1-actions',
          api: '/v1/actions',
          method: 'GET',
          category: '促销活动',
          categoryId: 1,
          description: '可参加的促销活动',
          frozen: false,
          schemaName: 'v1-actions'
        }
      ])
    ])
    fetchPlatformApiSchema.mockResolvedValue({
      name: 'v1-actions',
      platformApi: { api: '/v1/actions', method: 'GET' },
      requestFields: [{ name: 'sid', type: 'number', required: '_是', description: '_店铺 ID' }],
      responseFields: [],
      requestExample: { sid: 0 }
    })

    const wrapper = await mountPage()

    expect(fetchPlatformAbilities).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('Ozon')
    expect(wrapper.text()).toContain('可用接口')
    expect(wrapper.text()).toContain('/v1/actions')
    expect(wrapper.text()).toContain('可参加的促销活动')

    await button(wrapper, '查看文档')!.trigger('click')
    await flushPromises()

    expect(fetchPlatformApiSchema).toHaveBeenCalledWith('ozon', 'v1-actions')
    expect(wrapper.text()).toContain('店铺 ID')
  })

  it('Given 平台没有接口清单 When 打开平台API能力 Then 提示只提供代理调用，仍显示调用方案', async () => {
    fetchErpConfig.mockResolvedValue(cfg(true))
    route.query = { tab: 'abilities' }
    fetchPlatformAbilities.mockResolvedValue([
      {
        key: 'walmart',
        name: 'Walmart',
        description: '说明',
        apiPath: '/open-api/agent/walmart/invoke',
        steps: ['传入 AccessToken'],
        categories: [],
        apis: []
      }
    ])

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('暂未提供接口清单')
    expect(wrapper.text()).toContain('传入 AccessToken')
  })

  it('Given 尚未配置 When 打开页面 Then 停在第 1 步，检测按钮在无输入时禁用', async () => {
    fetchErpConfig.mockResolvedValue(cfg(false))

    const wrapper = await mountPage()

    expect(wrapper.find('input[type="password"]').exists()).toBe(true)
    expect(button(wrapper, '检测可用性')!.attributes('disabled')).toBeDefined()
  })

  it('Given 任意状态 When 打开页面 Then 底部有帮助面板，链接指向获取 AccessToken 的文档', async () => {
    fetchErpConfig.mockResolvedValue(cfg(false))

    const wrapper = await mountPage()

    const link = wrapper.get('a[href^="https://help.wxwerp.com/"]')
    expect(link.attributes('href')).toBe(
      'https://help.wxwerp.com/module-tutorial/system.open_api.api?q=api&lang=zh'
    )
    expect(link.attributes('target')).toBe('_blank')
    expect(link.text()).toContain('AccessToken')
  })

  it('Given 已配置 When 打开页面 Then 向导停在最后一步，显示已配置、重新配置与撤销配置', async () => {
    fetchErpConfig.mockResolvedValue(cfg(true))

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('密钥已加密保存')
    expect(wrapper.find('input[type="password"]').exists()).toBe(false)
    expect(wrapper.find('table').exists()).toBe(false)
    expect(button(wrapper, '重新配置')).toBeDefined()
    expect(button(wrapper, '撤销配置')).toBeDefined()
  })

  it('Given 已配置 When 点击重新配置 Then 回到第 1 步', async () => {
    fetchErpConfig.mockResolvedValue(cfg(true))
    const wrapper = await mountPage()

    await button(wrapper, '重新配置')!.trigger('click')

    expect(wrapper.find('input[type="password"]').exists()).toBe(true)
  })

  it('Given 已配置 When 确认撤销配置 Then 调用撤销并回到第 1 步', async () => {
    fetchErpConfig.mockResolvedValue(cfg(true, true))
    confirm.mockResolvedValue('confirm')
    revokeErpConfig.mockResolvedValue(undefined)
    const wrapper = await mountPage()

    await button(wrapper, '撤销配置')!.trigger('click')
    await flushPromises()

    expect(revokeErpConfig).toHaveBeenCalledOnce()
    expect(messageSuccess).toHaveBeenCalledWith('已撤销配置')
    expect(wrapper.find('input[type="password"]').exists()).toBe(true)
    expect(wrapper.find('input[placeholder="https://123.wxwerp.com"]').exists()).toBe(false)
  })

  it('Given 已配置 When 取消撤销确认 Then 不调用撤销，仍是已配置', async () => {
    fetchErpConfig.mockResolvedValue(cfg(true))
    confirm.mockRejectedValue('cancel')
    const wrapper = await mountPage()

    await button(wrapper, '撤销配置')!.trigger('click')
    await flushPromises()

    expect(revokeErpConfig).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('密钥已加密保存')
  })

  it('Given 已配置 When 撤销失败 Then 提示错误并保持已配置', async () => {
    fetchErpConfig.mockResolvedValue(cfg(true))
    confirm.mockResolvedValue('confirm')
    revokeErpConfig.mockRejectedValue(new Error('撤销失败，请重试。'))
    const wrapper = await mountPage()

    await button(wrapper, '撤销配置')!.trigger('click')
    await flushPromises()

    expect(messageError).toHaveBeenCalledWith('撤销失败，请重试。')
    expect(wrapper.text()).toContain('密钥已加密保存')
  })

  it('Given 第 1 步 When 检测通过并保存 Then 先检测后保存，最后显示已配置', async () => {
    fetchErpConfig.mockResolvedValue(cfg(false))
    validateErpToken.mockResolvedValue({ valid: true, message: 'AccessToken 可用' })
    saveErpToken.mockResolvedValue(undefined)
    const wrapper = await mountPage()

    await wrapper.get('input[type="password"]').setValue('secret-token')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(validateErpToken).toHaveBeenCalledWith('secret-token', undefined)
    expect(saveErpToken).not.toHaveBeenCalled()

    await button(wrapper, '保存')!.trigger('click')
    await flushPromises()

    expect(saveErpToken).toHaveBeenCalledWith('secret-token', undefined)
    expect(messageSuccess).toHaveBeenCalledWith('保存成功')
    expect(wrapper.text()).toContain('密钥已加密保存')
  })

  it('Given 第 1 步 When 默认状态 Then 单选默认旺销王公共SaaS且没有地址输入框', async () => {
    fetchErpConfig.mockResolvedValue(cfg(false))

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('旺销王公共SaaS')
    const publicRadio = wrapper.get('input[type="radio"]').element as HTMLInputElement
    expect(publicRadio.checked).toBe(true)
    expect(wrapper.find('input[placeholder="https://123.wxwerp.com"]').exists()).toBe(false)
  })

  it('Given 选择独立部署 When 填写地址检测并保存 Then 地址随检测与保存提交', async () => {
    fetchErpConfig.mockResolvedValue(cfg(false))
    validateErpToken.mockResolvedValue({ valid: true, message: 'AccessToken 可用' })
    saveErpToken.mockResolvedValue(undefined)
    const wrapper = await mountPage()

    await wrapper.findAll('input[type="radio"]')[1].setValue(true)
    const url = wrapper.get('input[placeholder="https://123.wxwerp.com"]')
    expect(button(wrapper, '检测可用性')!.attributes('disabled')).toBeDefined()
    await url.setValue('https://123.wxwerp.com')
    await wrapper.get('input[type="password"]').setValue('secret-token')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(validateErpToken).toHaveBeenCalledWith('secret-token', 'https://123.wxwerp.com')

    await button(wrapper, '保存')!.trigger('click')
    await flushPromises()

    expect(saveErpToken).toHaveBeenCalledWith('secret-token', 'https://123.wxwerp.com')
    expect(wrapper.text()).toContain('独立部署：https://123.wxwerp.com')
  })

  it('Given 第 1 步 When 检测不通过 Then 留在第 1 步显示原因，不能保存', async () => {
    fetchErpConfig.mockResolvedValue(cfg(false))
    validateErpToken.mockResolvedValue({ valid: false, message: 'ERP 拒绝了该 AccessToken' })
    const wrapper = await mountPage()

    await wrapper.get('input[type="password"]').setValue('bad')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('ERP 拒绝了该 AccessToken')
    expect(button(wrapper, '保存')).toBeUndefined()
    expect(saveErpToken).not.toHaveBeenCalled()
  })

  it('Given 读取配置失败 When 点击重试且成功 Then 显示已配置', async () => {
    fetchErpConfig.mockRejectedValueOnce(new Error('x')).mockResolvedValueOnce(cfg(true))
    const wrapper = await mountPage()
    expect(wrapper.text()).toContain('读取失败')

    await button(wrapper, '重试')!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('密钥已加密保存')
  })
})
