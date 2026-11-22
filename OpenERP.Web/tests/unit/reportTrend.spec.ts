import { describe, expect, it } from 'vitest'
import { parseOrderSnapshotReport } from '../../src/reports/orderSnapshot'
import { dataTrendFixture, salesFixture, trendFixture } from '../reportFixtures'

function reportBody(overrides: Record<string, unknown> = {}) {
  return {
    generatedAtUtc: '2026-11-20T07:47:00Z',
    summary: { orderCount: 1, itemQuantity: 2 },
    sales: salesFixture(12.5, 'USD', 1, 0),
    trend: dataTrendFixture(),
    platforms: ['TemuPop'],
    shops: [],
    groups: [],
    orders: [],
    page: 1,
    pageSize: 20,
    pageCount: 1,
    ...overrides,
  }
}

describe('报表销售与走势契约解析', () => {
  it('Given 完整 trend 与 sales When 解析 Then 保留窗口、环比与金额语义', () => {
    const report = parseOrderSnapshotReport(reportBody())
    expect(report.sales.amount).toBe(12.5)
    expect(report.sales.currency).toBe('USD')
    expect(report.trend.days).toBe(7)
    expect(report.trend.points).toHaveLength(7)
    expect(report.trend.points[0]?.orderCount).toBe(1)
    expect(report.trend.previous?.orderCount).toBe(5)
  })

  it('Given 30 天窗口 When 解析 Then 接受 30 个点', () => {
    const report = parseOrderSnapshotReport(
      reportBody({ trend: trendFixture({ days: 30, orderCounts: [1] }) }),
    )
    expect(report.trend.days).toBe(30)
    expect(report.trend.points).toHaveLength(30)
  })

  it('Given 缺少 sales 或 trend When 解析 Then 拒绝不完整契约', () => {
    const withoutSales: Record<string, unknown> = reportBody()
    delete withoutSales.sales
    expect(() => parseOrderSnapshotReport(withoutSales)).toThrow('报表数据格式无效')
    const withoutTrend: Record<string, unknown> = reportBody()
    delete withoutTrend.trend
    expect(() => parseOrderSnapshotReport(withoutTrend)).toThrow('报表数据格式无效')
  })

  it('Given 走势点数量与 days 不一致 When 解析 Then 拒绝半截窗口', () => {
    const trend = { ...trendFixture(), points: trendFixture().points.slice(0, 5) }
    expect(() => parseOrderSnapshotReport(reportBody({ trend }))).toThrow('报表数据格式无效')
  })

  it('Given 非法窗口天数或非法日期 When 解析 Then 拒绝', () => {
    expect(() =>
      parseOrderSnapshotReport(reportBody({ trend: { ...trendFixture(), days: 0 } })),
    ).toThrow('报表数据格式无效')
    expect(() =>
      parseOrderSnapshotReport(reportBody({ trend: { ...trendFixture(), days: 367 } })),
    ).toThrow('报表数据格式无效')
    expect(() =>
      parseOrderSnapshotReport(reportBody({ trend: { ...trendFixture(), days: 7.5 } })),
    ).toThrow('报表数据格式无效')
    expect(() =>
      parseOrderSnapshotReport(reportBody({ trend: { ...trendFixture(), end: '2026-13-01' } })),
    ).toThrow('报表数据格式无效')
    expect(() =>
      parseOrderSnapshotReport(reportBody({ trend: { ...trendFixture(), start: '2026-02-30' } })),
    ).toThrow('报表数据格式无效')
    const point = { ...trendFixture().points[0]!, date: '2026-02-30' }
    expect(() =>
      parseOrderSnapshotReport(
        reportBody({
          trend: { ...trendFixture(), points: [point, ...trendFixture().points.slice(1)] },
        }),
      ),
    ).toThrow('报表数据格式无效')
  })

  it('Given 自定义区间 When 解析 Then 接受 1 到 366 天的任意窗口', () => {
    for (const days of [1, 14, 92, 366]) {
      const trend = trendFixture({ days, end: '2026-11-20' })
      const report = parseOrderSnapshotReport(reportBody({ trend }))
      expect(report.trend.days).toBe(days)
      expect(report.trend.start).toBe(trend.points[0]!.date)
      expect(report.trend.end).toBe(trend.points[trend.points.length - 1]!.date)
      expect(report.trend.points).toHaveLength(days)
    }
  })

  it('Given 缺少 start 或 start 与首点不一致 When 解析 Then 拒绝', () => {
    const withoutStart: Record<string, unknown> = { ...trendFixture() }
    delete withoutStart.start
    expect(() => parseOrderSnapshotReport(reportBody({ trend: withoutStart }))).toThrow(
      '报表数据格式无效',
    )
    const trend = trendFixture()
    expect(() =>
      parseOrderSnapshotReport(reportBody({ trend: { ...trend, start: '2026-11-01' } })),
    ).toThrow('报表数据格式无效')
  })

  it('Given 未知金额 When 解析 Then 保留 null 而不是补 0', () => {
    const trend = trendFixture({
      orderCounts: [1],
      salesAmounts: [null],
      amountKnownOrderCount: 0,
      amountUnknownOrderCount: 1,
    })
    const report = parseOrderSnapshotReport(
      reportBody({ sales: salesFixture(null, null, 0, 1), trend }),
    )
    expect(report.trend.points[0]?.salesAmount).toBeNull()
    expect(report.sales.amount).toBeNull()
  })
})

describe('报表走势窗口一致性', () => {
  it('Given 逐日日期不连续或倒序 When 解析 Then 拒绝', () => {
    const trend = trendFixture()
    const points = [...trend.points]
    points[1] = { ...points[1]!, date: points[0]!.date }
    expect(() => parseOrderSnapshotReport(reportBody({ trend: { ...trend, points } }))).toThrow(
      '报表数据格式无效',
    )
    const reversed = [...trend.points].reverse()
    expect(() =>
      parseOrderSnapshotReport(reportBody({ trend: { ...trend, points: reversed } })),
    ).toThrow('报表数据格式无效')
  })

  it('Given 尾点日期不等于 end When 解析 Then 拒绝', () => {
    const trend = trendFixture()
    expect(() =>
      parseOrderSnapshotReport(reportBody({ trend: { ...trend, end: '2026-11-21' } })),
    ).toThrow('报表数据格式无效')
  })

  it('Given 无订单日却带销售额 When 解析 Then 拒绝补零语义错误', () => {
    const trend = trendFixture()
    const points = [...trend.points]
    points[0] = { ...points[0]!, orderCount: 0, salesAmount: 100 }
    expect(() => parseOrderSnapshotReport(reportBody({ trend: { ...trend, points } }))).toThrow(
      '报表数据格式无效',
    )
  })

  it('Given 金额计数与逐日订单数不一致 When 解析 Then 拒绝', () => {
    const trend = trendFixture({ orderCounts: [2] })
    expect(() =>
      parseOrderSnapshotReport(
        reportBody({ trend: { ...trend, amountKnownOrderCount: 0, amountUnknownOrderCount: 1 } }),
      ),
    ).toThrow('报表数据格式无效')
  })

  it('Given 负商品件数 When 解析 Then 保留原值而不是拒绝整份报表', () => {
    const trend = trendFixture({ orderCounts: [1], itemQuantities: [-3] })
    const report = parseOrderSnapshotReport(reportBody({ trend }))
    expect(report.trend.points[0]?.itemQuantity).toBe(-3)
    expect(report.trend.previous).toBeNull()
  })

  it('Given 环比商品件数为负 When 解析 Then 接受来源原始有符号数量', () => {
    const trend = trendFixture({
      orderCounts: [1],
      previous: { orderCount: 2, itemQuantity: -2, salesAmount: 5 },
    })
    const report = parseOrderSnapshotReport(reportBody({ trend }))
    expect(report.trend.previous?.itemQuantity).toBe(-2)
  })

  it('Given 金额计数与逐日金额矛盾 When 解析 Then 拒绝', () => {
    const known = trendFixture({
      orderCounts: [1],
      salesAmounts: [42],
      amountKnownOrderCount: 0,
      amountUnknownOrderCount: 1,
    })
    expect(() => parseOrderSnapshotReport(reportBody({ trend: known }))).toThrow('报表数据格式无效')
    const unknown = trendFixture({
      orderCounts: [1],
      salesAmounts: [null],
      amountKnownOrderCount: 1,
      amountUnknownOrderCount: 0,
    })
    expect(() => parseOrderSnapshotReport(reportBody({ trend: unknown }))).toThrow(
      '报表数据格式无效',
    )
  })
})
