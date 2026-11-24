import { describe, expect, it } from 'vitest'
import {
  errorMessage,
  parsePlatforms,
  parseShopsResponse,
  problemDetail,
} from '../../src/components/shops/responses'

describe('授权店铺页的接口响应校验', () => {
  it('Given 只有店铺列表 When 解析 Then 缺失的分页与汇总字段使用兜底值', () => {
    const parsed = parseShopsResponse({
      items: [{ sid: 7, name: '店铺 7', platform: 'TemuPop' }],
    })

    expect(parsed.items[0]).toMatchObject({ sid: '7', region: null, isTokenOk: null })
    expect(parsed.page).toBe(1)
    expect(parsed.pageSize).toBe(20)
    expect(parsed.totalCount).toBe(1)
    expect(parsed.pageCount).toBe(0)
    expect(parsed.summary).toEqual({
      valid: 0,
      invalid: 0,
      unknown: 0,
      lastSyncedAt: null,
      syncVersion: 0,
    })
  })

  it('Given 店铺缺少名称或编号 When 解析 Then 拒绝整个响应', () => {
    expect(() => parseShopsResponse({ items: [{ sid: '1', platform: 'TemuPop' }] })).toThrow(
      '本地店铺响应格式无效。',
    )
    expect(() => parseShopsResponse({ items: [{ sid: '', name: 'A', platform: 'P' }] })).toThrow()
    expect(() => parseShopsResponse(null)).toThrow()
  })

  it('Given 平台选项 When 缺少显示名 Then 回落到平台名并忽略无效项', () => {
    expect(
      parsePlatforms({
        items: [
          { name: 'TemuPop', shopCount: 3 },
          { name: 'AliExpress', displayName: '速卖通', isProduction: false },
          { displayName: '没有名称' },
        ],
      }),
    ).toEqual([
      { name: 'TemuPop', displayName: 'TemuPop', icon: null, isProduction: true, shopCount: 3 },
      { name: 'AliExpress', displayName: '速卖通', icon: null, isProduction: false, shopCount: 0 },
    ])
    expect(parsePlatforms({})).toEqual([])
  })

  it('Given 失败 When 取错误文案 Then 网络错误用兜底文案，业务错误保留原因', () => {
    expect(errorMessage(new TypeError('Failed to fetch'), '无法获取')).toBe('无法获取')
    expect(errorMessage(new Error('响应格式无效'), '无法获取')).toBe('响应格式无效')
  })

  it('Given ProblemDetails 响应 When 取详情 Then 优先返回 detail，否则兜底', async () => {
    const withDetail = new Response(JSON.stringify({ detail: '令牌已过期' }), { status: 400 })
    expect(await problemDetail(withDetail, '失败')).toBe('令牌已过期')
    expect(await problemDetail(new Response('oops', { status: 500 }), '失败')).toBe('失败')
  })
})
