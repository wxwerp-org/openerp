import { describe, expect, it } from 'vitest'
import {
  httpErrorMessage,
  parseConfig,
  parseShopsResponse,
  parseSyncSettings,
  parseSyncShopSelection
} from '@/hooks/business/orderSyncResponses'

describe('订单同步面板的接口响应校验', () => {
  it('Given 完整的配置响应 When 解析 Then 返回订单同步开关与支持的平台', () => {
    expect(
      parseConfig({ configured: true, orderSyncEnabled: false, platforms: ['AliExpress'] })
    ).toEqual({ configured: true, orderSyncEnabled: false, platforms: ['AliExpress'] })
    expect(() => parseConfig({ configured: 'yes', orderSyncEnabled: true, platforms: [] })).toThrow(
      '服务器配置响应格式无效。'
    )
    expect(() =>
      parseConfig({ configured: true, orderSyncEnabled: true, platforms: [1] })
    ).toThrow()
  })

  it('Given 店铺列表响应 When 字段缺失或类型错误 Then 拒绝并说明原因', () => {
    const shop = {
      sid: 1,
      name: '店铺',
      platform: 'AliExpress',
      region: null,
      syncSupported: true
    }
    const page = { items: [shop], page: 1, pageSize: 20, totalCount: 1, pageCount: 1 }

    expect(parseShopsResponse(page).items).toEqual([{ ...shop, sid: '1' }])
    expect(() => parseShopsResponse({ ...page, page: '1' })).toThrow('店铺列表响应格式无效。')
    expect(() =>
      parseShopsResponse({ ...page, items: [{ ...shop, syncSupported: 'yes' }] })
    ).toThrow('店铺列表中包含格式无效的店铺信息。')
  })

  it('Given 定时同步设置 When 周期或店铺编号非法 Then 拒绝', () => {
    expect(parseSyncSettings({ enabled: true, shopSids: [1, 2], intervalMinutes: 30 })).toEqual({
      enabled: true,
      shopSids: [1, 2],
      intervalMinutes: 30
    })
    expect(() => parseSyncSettings({ enabled: true, shopSids: [0], intervalMinutes: 30 })).toThrow()
    expect(() => parseSyncSettings({ enabled: true, shopSids: [1], intervalMinutes: 0 })).toThrow()
  })

  it('Given 可选店铺响应 When 编号不是正整数字符串 Then 拒绝', () => {
    expect(parseSyncShopSelection({ items: [{ sid: '12', name: 'A' }] })).toEqual([
      { sid: '12', name: 'A' }
    ])
    expect(() => parseSyncShopSelection({ items: [{ sid: '012', name: 'A' }] })).toThrow()
  })

  it('Given 失败响应 When 取错误文案 Then 优先用 ProblemDetails，缺失时回落到 HTTP 状态', async () => {
    const problem = new Response(JSON.stringify({ title: '无权访问', detail: '请联系管理员' }), {
      status: 403
    })
    expect(await httpErrorMessage(problem, '读取失败')).toBe('无权访问：请联系管理员')

    const plain = new Response('boom', { status: 500 })
    expect(await httpErrorMessage(plain, '读取失败')).toBe('读取失败（HTTP 500）')
  })
})
