import { describe, expect, it } from 'vitest'
import type { OrderSnapshotReport, ReportShop } from '../../src/reports/orderSnapshot'
import {
  averageItemsPerOrder,
  formatAverageItems,
  formatSnapshotQuantity,
  platformDistribution,
  shopRanking,
} from '../../src/reports/orderSnapshotPresentation'

function shop(partial: Partial<ReportShop> & { key: string }): ReportShop {
  return {
    source: 'erp',
    platform: 'TemuPop',
    shopId: partial.key,
    label: partial.key,
    ...partial,
  }
}

function groups(entries: Array<[ReportShop, number, number]>): OrderSnapshotReport['groups'] {
  return entries.map(([shop, orderCount, itemQuantity]) => ({
    shop,
    orderCount,
    itemQuantity,
  }))
}

describe('数量格式化', () => {
  it('Given 大数量 When 格式化 Then 使用 zh-CN 千分位', () => {
    expect(formatSnapshotQuantity(0)).toBe('0')
    expect(formatSnapshotQuantity(5)).toBe('5')
    expect(formatSnapshotQuantity(1234)).toBe('1,234')
    expect(formatSnapshotQuantity(1234567)).toBe('1,234,567')
  })

  it('Given 平均每单件数 When 格式化 Then 整数不带小数、非整数保留一位小数且千分位统一', () => {
    expect(formatAverageItems(4)).toBe('4')
    expect(formatAverageItems(4.26)).toBe('4.3')
    expect(formatAverageItems(2500)).toBe('2,500')
    expect(formatAverageItems(1234.56)).toBe('1,234.6')
  })
})

describe('平均每单件数', () => {
  it('Given 汇总订单 3、商品 12 When 计算平均值 Then 结果为 4', () => {
    expect(averageItemsPerOrder({ orderCount: 3, itemQuantity: 12 })).toBe(4)
  })

  it('Given 空汇总 When 计算平均值 Then 返回 null 而不是除零结果', () => {
    expect(averageItemsPerOrder({ orderCount: 0, itemQuantity: 0 })).toBeNull()
  })
})

describe('店铺排行', () => {
  const multiShopGroups = groups([
    [shop({ key: 'shop-a', platform: 'TemuPop' }), 4, 10],
    [shop({ key: 'shop-b', platform: 'Amazon' }), 2, 20],
    [shop({ key: 'shop-c', platform: 'TemuPop' }), 6, 6],
  ])

  it('Given 多店铺分组 When 按商品件数排行 Then 按数量降序且保留店铺身份与原始数量', () => {
    const rows = shopRanking(multiShopGroups, 'itemQuantity')

    expect(rows.map((row) => row.shop.key)).toEqual(['shop-b', 'shop-a', 'shop-c'])
    expect(rows[0]).toMatchObject({
      shop: expect.objectContaining({ key: 'shop-b' }),
      orderCount: 2,
      itemQuantity: 20,
    })
  })

  it('Given 数量并列的店铺 When 排行 Then 按店铺键稳定决胜', () => {
    const tied = groups([
      [shop({ key: 'shop-c' }), 3, 5],
      [shop({ key: 'shop-a' }), 5, 5],
      [shop({ key: 'shop-b' }), 1, 5],
    ])

    expect(shopRanking(tied, 'itemQuantity').map((row) => row.shop.key)).toEqual([
      'shop-a',
      'shop-b',
      'shop-c',
    ])
  })

  it('Given 超过 5 家店铺 When 默认排行 Then 只返回 Top 5 且按数量降序', () => {
    const many = groups(
      Array.from({ length: 8 }, (_, index) => [
        shop({ key: `shop-${index}` }),
        index + 1,
        index + 1,
      ]) as Array<[ReportShop, number, number]>,
    )

    const rows = shopRanking(many, 'orderCount')

    expect(rows).toHaveLength(5)
    expect(rows.map((row) => row.shop.key)).toEqual([
      'shop-7',
      'shop-6',
      'shop-5',
      'shop-4',
      'shop-3',
    ])
  })

  it('Given 自定义 limit When 排行 Then 按 limit 截断', () => {
    const rows = shopRanking(multiShopGroups, 'orderCount', 2)

    expect(rows.map((row) => row.shop.key)).toEqual(['shop-c', 'shop-a'])
  })

  it('Given 展示集合 When 计算条形长度 Then 相对当前集合最大值且最大值为 1', () => {
    const rows = shopRanking(multiShopGroups, 'orderCount')

    expect(rows.map((row) => row.barPercentage)).toEqual([1, 4 / 6, 2 / 6])
    expect(rows.every((row) => Number.isFinite(row.barPercentage))).toBe(true)
  })

  it('Given 全部数量为零的店铺 When 排行 Then 条形长度为 0 且无数值无效', () => {
    const zero = groups([
      [shop({ key: 'shop-a' }), 0, 0],
      [shop({ key: 'shop-b' }), 0, 0],
    ])

    const rows = shopRanking(zero, 'orderCount')

    expect(rows.map((row) => row.barPercentage)).toEqual([0, 0])
  })

  it('Given 空分组 When 排行 Then 返回空数组', () => {
    expect(shopRanking([], 'itemQuantity')).toEqual([])
  })
})

describe('平台分布', () => {
  const multiPlatformGroups = groups([
    [shop({ key: 'shop-a', platform: 'TemuPop' }), 3, 9],
    [shop({ key: 'shop-b', platform: 'Amazon' }), 2, 4],
    [shop({ key: 'shop-c', platform: 'TemuPop' }), 5, 5],
    [shop({ key: 'shop-d', platform: 'Amazon' }), 1, 2],
  ])

  it('Given 多平台多店铺分组 When 按平台聚合 Then 数量降序、标识稳定决胜且总和等于汇总订单数', () => {
    const rows = platformDistribution(multiPlatformGroups, 11)

    expect(rows.map((row) => row.platform)).toEqual(['TemuPop', 'Amazon'])
    expect(rows.map((row) => row.orderCount)).toEqual([8, 3])
    expect(rows.reduce((sum, row) => sum + row.orderCount, 0)).toBe(11)
  })

  it('Given 平台订单数并列 When 聚合 Then 按平台标识稳定决胜', () => {
    const tied = groups([
      [shop({ key: 'shop-a', platform: 'TemuPop' }), 2, 2],
      [shop({ key: 'shop-b', platform: 'Amazon' }), 2, 2],
    ])

    expect(platformDistribution(tied, 4).map((row) => row.platform)).toEqual(['Amazon', 'TemuPop'])
  })

  it('Given 平台与汇总订单数 When 计算占比 Then 以汇总订单数为分母并保留原始数量', () => {
    const rows = platformDistribution(multiPlatformGroups, 11)

    expect(rows[0]).toMatchObject({
      platform: 'TemuPop',
      label: 'Temu 半托管',
      orderCount: 8,
      percentage: 8 / 11,
    })
    expect(rows[1]).toMatchObject({ platform: 'Amazon', label: 'Amazon', percentage: 3 / 11 })
  })

  it('Given 平台顺序超过色板长度 When 分配颜色 Then 复用工作台蓝色系并循环', () => {
    const many = groups(
      Array.from({ length: 6 }, (_, index) => [
        shop({ key: `shop-${index}`, platform: `platform-${index}` }),
        1,
        1,
      ]) as Array<[ReportShop, number, number]>,
    )

    const colors = platformDistribution(many, 6).map((row) => row.color)

    expect(colors).toEqual([
      'var(--color-primary)',
      'var(--color-chart-2)',
      'var(--color-chart-3)',
      'var(--color-chart-4)',
      'var(--color-primary)',
      'var(--color-chart-2)',
    ])
  })

  it('Given 汇总订单数为 0 When 计算占比 Then 占比为 0 且数量保持原始值', () => {
    const zero = groups([[shop({ key: 'shop-a', platform: 'Amazon' }), 0, 0]])

    const rows = platformDistribution(zero, 0)

    expect(rows).toHaveLength(1)
    expect(rows[0]).toMatchObject({ platform: 'Amazon', orderCount: 0, percentage: 0 })
  })

  it('Given 空分组 When 聚合 Then 返回空数组', () => {
    expect(platformDistribution([], 0)).toEqual([])
  })
})
