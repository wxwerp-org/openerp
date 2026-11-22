import { describe, expect, it } from 'vitest'
import {
  formatCount,
  formatDateTime,
  parseShopDirectory,
  platformTotals,
  shopNameBySid,
  shopNameMap,
} from '../../src/workspace/workspaceData'

const adminDirectory = {
  items: [
    {
      sid: '30',
      name: '三十店',
      platform: 'TemuPop',
      region: 'US',
      memberId: 'm-30',
      isTokenOk: true,
      tokenError: null,
      syncedAt: '2026-11-20T05:00:00Z',
    },
  ],
  summary: {
    valid: 1,
    invalid: 0,
    unknown: 0,
    lastSyncedAt: '2026-11-20T05:00:00Z',
    syncVersion: 3,
  },
  page: 1,
  pageSize: 100,
  totalCount: 1,
  pageCount: 1,
}

const employeeDirectory = {
  items: [{ sid: '30', name: '三十店', platform: 'TemuPop', region: 'US' }],
  page: 1,
  pageSize: 20,
  totalCount: 1,
  pageCount: 1,
}

describe('工作台数据解析', () => {
  it('Given 管理员目录响应 When 解析 Then 保留授权汇总与健康字段', () => {
    const directory = parseShopDirectory(adminDirectory)
    expect(directory.summary?.valid).toBe(1)
    expect(directory.summary?.lastSyncedAt).toBe('2026-11-20T05:00:00Z')
    expect(directory.items[0]?.isTokenOk).toBe(true)
  })

  it('Given 目录缺少可选状态字段 When 解析 Then 没有汇总且健康字段为未知', () => {
    const directory = parseShopDirectory(employeeDirectory)
    expect(directory.summary).toBeNull()
    expect(directory.items[0]).toEqual({
      sid: '30',
      name: '三十店',
      platform: 'TemuPop',
      region: 'US',
      isTokenOk: null,
      tokenError: null,
      syncedAt: null,
    })
  })

  it('Given 格式错误的目录 When 解析 Then 抛出可展示错误', () => {
    expect(() => parseShopDirectory({ items: [{}] })).toThrow('店铺目录中包含格式无效的店铺')
    expect(() => parseShopDirectory({})).toThrow('店铺目录数据格式无效')
  })

  it('Given 目录 When 建店名索引 Then 完整键与 SID 都能命中真实店名', () => {
    const directory = parseShopDirectory(adminDirectory)
    expect(shopNameMap(directory).get('TemuPop/30')).toBe('三十店')
    expect(shopNameBySid(directory).get('30')).toBe('三十店')
  })

  it('Given 时间与数量 When 格式化 Then 使用本地时区与千分位', () => {
    expect(formatDateTime(null)).toBe('—')
    expect(formatDateTime('not-a-date')).toBe('—')
    expect(formatDateTime('2026-11-20T05:00:00Z')).toMatch(/^2026-11-20 \d{2}:\d{2}$/)
    expect(formatCount(1234)).toBe('1,234')
    expect(formatCount(null)).toBe('—')
  })

  it('Given 平台分组 When 聚合 Then 同平台合并并按订单数降序', () => {
    const totals = platformTotals([
      { shop: { platform: 'TemuPop' }, orderCount: 2 },
      { shop: { platform: 'Amazon' }, orderCount: 5 },
      { shop: { platform: 'TemuPop' }, orderCount: 3 },
    ])
    expect(totals).toEqual([
      { platform: 'Amazon', orderCount: 5 },
      { platform: 'TemuPop', orderCount: 5 },
    ])
  })
})
