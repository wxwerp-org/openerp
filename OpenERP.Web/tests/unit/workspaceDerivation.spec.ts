import { describe, expect, it } from 'vitest'
import type { OrderSyncTask } from '../../src/composables/orderSyncTask'
import type { OrderSnapshotReport } from '../../src/reports/orderSnapshot'
import {
  buildOnboardingSteps,
  buildTrendComparison,
  buildTrendGeometry,
  groupSyncTasks,
  nextOnboardingLabel,
  rankTopShops,
  resolveDashboardState,
  resolveTrendRange,
  resolveTrendState,
  trendOrderTotal,
  trendRangeDays,
  trendRangeLabel,
  trendSalesTotal,
  validateTrendRange,
  type DashboardStateInput,
  type OnboardingInput,
} from '../../src/workspace/workspaceDerivation'
import { platformTotals } from '../../src/workspace/workspaceData'
import {
  dataTrendFixture,
  degradedTrendFixture,
  emptyTrendFixture,
  trendFixture,
} from '../reportFixtures'

function stateInput(overrides: Partial<DashboardStateInput> = {}): DashboardStateInput {
  return {
    hasSnapshot: true,
    reportStatus: 'success',
    orderCount: 0,
    runningOrderTasks: 0,
    administrator: true,
    shopSyncRunning: false,
    shopSyncStarting: false,
    ...overrides,
  }
}

function onboardingInput(overrides: Partial<OnboardingInput> = {}): OnboardingInput {
  return {
    configured: true,
    directoryItems: 2,
    directoryLoaded: true,
    validTemuShopCount: 1,
    lastSyncedAt: '2026-11-20T05:40:00Z',
    orderCount: 0,
    orderSyncEnabled: true,
    ...overrides,
  }
}

function task(overrides: Partial<OrderSyncTask> = {}): OrderSyncTask {
  return {
    epoch: 'epoch-1',
    version: 1,
    id: 'job-1',
    sid: 30,
    status: 'running',
    stage: 'details',
    total: 10,
    read: 3,
    processed: 3,
    added: 0,
    updated: 0,
    unchanged: 0,
    skipped: 0,
    skipReasons: {},
    error: null,
    ...overrides,
  }
}

function group(
  key: string,
  platform: string,
  shopId: string,
  orderCount: number,
  itemQuantity: number,
) {
  return {
    shop: { source: 'wxw-erp', platform, shopId, key, label: key },
    orderCount,
    itemQuantity,
  }
}

describe('工作台状态判定', () => {
  it('Given 首次报表加载中且无快照 When 判定 Then 显示骨架', () => {
    expect(resolveDashboardState(stateInput({ hasSnapshot: false, reportStatus: 'loading' }))).toBe(
      'loading',
    )
  })

  it('Given 首次报表失败且无快照 When 判定 Then 整页错误态', () => {
    expect(resolveDashboardState(stateInput({ hasSnapshot: false, reportStatus: 'error' }))).toBe(
      'error',
    )
  })

  it('Given 报表有订单 When 存在同步任务 Then 仍为有数据态', () => {
    expect(resolveDashboardState(stateInput({ orderCount: 12, runningOrderTasks: 2 }))).toBe('data')
  })

  it('Given 零订单 When 管理员目录同步中 Then 同步中态', () => {
    expect(resolveDashboardState(stateInput({ shopSyncRunning: true }))).toBe('syncing')
  })

  it('Given 零订单 When 员工且仅有目录同步（不可见） Then 空态', () => {
    expect(resolveDashboardState(stateInput({ administrator: false, shopSyncRunning: true }))).toBe(
      'empty',
    )
  })

  it('Given 零订单 When 目录同步启动请求中 Then 立即同步中态', () => {
    expect(resolveDashboardState(stateInput({ shopSyncStarting: true }))).toBe('syncing')
  })

  it('Given 零订单 When 有订单同步任务 Then 同步中态', () => {
    expect(resolveDashboardState(stateInput({ runningOrderTasks: 1 }))).toBe('syncing')
  })

  it('Given 刷新失败但保留快照 When 判定 Then 仍按快照推导有数据态', () => {
    expect(
      resolveDashboardState(
        stateInput({ reportStatus: 'error', orderCount: 3, hasSnapshot: true }),
      ),
    ).toBe('data')
    expect(resolveDashboardState(stateInput({ reportStatus: 'error', hasSnapshot: true }))).toBe(
      'empty',
    )
  })
})

describe('初始化引导步骤', () => {
  it('Given 已配置、目录已同步但有订单 When 生成步骤 Then 第四步为当前可执行步', () => {
    const steps = buildOnboardingSteps(onboardingInput())
    expect(steps.map((step) => step.done)).toEqual([true, true, true, false])
    expect(steps.map((step) => step.executable)).toEqual([false, false, false, true])
    expect(nextOnboardingLabel(steps)).toBe('同步历史订单')
  })

  it('Given 未配置 When 生成步骤 Then 只有第一步可执行', () => {
    const steps = buildOnboardingSteps(
      onboardingInput({
        configured: false,
        directoryLoaded: false,
        directoryItems: 0,
        validTemuShopCount: 0,
        lastSyncedAt: null,
      }),
    )
    expect(steps.map((step) => step.executable)).toEqual([true, false, false, false])
    expect(steps.map((step) => step.done)).toEqual([false, false, false, false])
  })

  it('Given Token 无可见店铺 When 生成步骤 Then 第三步提示且无法继续授权/订单', () => {
    const steps = buildOnboardingSteps(
      onboardingInput({ directoryItems: 0, validTemuShopCount: 0, lastSyncedAt: null }),
    )
    expect(steps[2]!.description).toBe('Token 无可见店铺')
    expect(steps[3]!.description).toBe('需先在上游授权 TemuPop 店铺')
    expect(steps[3]!.executable).toBe(false)
  })

  it('Given 有目录但没有 TemuPop 有效店铺 When 生成步骤 Then 第二步高亮且第四步置灰', () => {
    const steps = buildOnboardingSteps(onboardingInput({ validTemuShopCount: 0 }))
    expect(steps[1]!.done).toBe(false)
    expect(steps[1]!.executable).toBe(true)
    expect(steps[3]!.executable).toBe(false)
  })

  it('Given 环境未开启订单同步 When 生成步骤 Then 第四步置灰并说明原因', () => {
    const steps = buildOnboardingSteps(onboardingInput({ orderSyncEnabled: false }))
    expect(steps[3]!.executable).toBe(false)
    expect(steps[3]!.description).toBe('当前环境未开启订单同步')
  })

  it('Given 四步全部完成 When 生成步骤 Then 下一步显示初始化完成', () => {
    const steps = buildOnboardingSteps(onboardingInput({ orderCount: 5 }))
    expect(steps.every((step) => step.done)).toBe(true)
    expect(nextOnboardingLabel(steps)).toBe('初始化完成')
  })

  it('Given 换 Token 后未重新同步目录 When 目录仍为空 Then 提示重新同步且不清空订单', () => {
    const steps = buildOnboardingSteps(
      onboardingInput({
        directoryItems: 0,
        validTemuShopCount: 0,
        lastSyncedAt: null,
        orderCount: 3,
      }),
    )
    expect(steps[2]!.description).toBe('Token 无可见店铺')
    expect(steps[3]!.done).toBe(true)
  })
})

describe('工作台店铺排行与下钻', () => {
  const groups = [
    group('wxw-erp/TemuPop/30', 'TemuPop', '30', 4, 9),
    group('wxw-erp/TemuPop/31', 'TemuPop', '31', 6, 9),
    group('wxw-erp/TemuPop/32', 'TemuPop', '32', 2, 1),
    group('wxw-erp/TemuPop/33', 'TemuPop', '33', 1, 9),
  ]

  it('Given 件数并列 When 排行 Then 按订单数再按完整店铺键排序', () => {
    const names = new Map([['TemuPop/31', '三十一店']])
    const rows = rankTopShops(groups, names)

    expect(rows.map((row) => row.key)).toEqual([
      'wxw-erp/TemuPop/31',
      'wxw-erp/TemuPop/30',
      'wxw-erp/TemuPop/33',
      'wxw-erp/TemuPop/32',
    ])
    expect(rows[0]!.name).toBe('三十一店')
    expect(rows[1]!.name).toBe('店铺 30')
  })

  it('Given 超过五家店铺 When 排行 Then 只返回前五', () => {
    const many = Array.from({ length: 7 }, (_, index) =>
      group(`wxw-erp/TemuPop/${index}`, 'TemuPop', String(index), 1, index),
    )
    expect(rankTopShops(many, new Map())).toHaveLength(5)
  })

  it('Given 报表分组 When 聚合平台 Then 分母为订单总数', () => {
    const totals = platformTotals([
      group('a', 'TemuPop', '1', 3, 5),
      group('b', 'TemuPop', '2', 2, 1),
      group('c', 'Amazon', '3', 5, 2),
    ])
    expect(totals).toEqual([
      { platform: 'Amazon', orderCount: 5 },
      { platform: 'TemuPop', orderCount: 5 },
    ])
  })

  it('Given 同步任务列表 When 分组 Then 排队与进行中分开统计', () => {
    const grouped = groupSyncTasks([
      task({ sid: 1, stage: 'details' }),
      task({ sid: 2, stage: 'queued' }),
      task({ sid: 3, stage: 'queued' }),
      task({ sid: 4, status: 'succeeded', stage: 'done' }),
    ])
    expect(grouped.running.map((item) => item.sid)).toEqual([1])
    expect(grouped.queued).toBe(2)
  })
})

// 类型守卫：确保 OrderSnapshotReport['groups'] 与测试构造一致
const _typeCheck: OrderSnapshotReport['groups'] = []
void _typeCheck

describe('经营走势派生', () => {
  it('Given 窗口无订单 When 判定 Then 空态优先于金额降级态', () => {
    expect(resolveTrendState(emptyTrendFixture())).toBe('empty')
    expect(resolveTrendState(degradedTrendFixture())).toBe('degraded')
    expect(resolveTrendState(dataTrendFixture())).toBe('data')
  })

  it('Given 部分日期金额未知 When 汇总 Then 只加已知金额且不把未知当 0', () => {
    const trend = trendFixture({
      orderCounts: [2, 1, 0],
      salesAmounts: [10.5, null, 0],
    })
    expect(trendSalesTotal(trend)).toBe(10.5)
    expect(trendOrderTotal(trend)).toBe(3)
    expect(
      trendSalesTotal(
        trendFixture({
          orderCounts: [1],
          salesAmounts: [null, null, null, null, null, null, null],
        }),
      ),
    ).toBeNull()
  })

  it('Given 上一等长窗口 When 计算环比 Then 输出带符号百分比与方向', () => {
    const comparison = buildTrendComparison(dataTrendFixture())
    expect(comparison.sales.text).toBe('+17.5%')
    expect(comparison.sales.tone).toBe('up')
    expect(comparison.orders.text).toBe('+40.0%')
    expect(comparison.orders.tone).toBe('up')
  })

  it('Given 无更早历史或上一窗口为 0 When 计算环比 Then 显示无对比或破折号', () => {
    expect(buildTrendComparison(emptyTrendFixture()).sales.text).toBe('无对比')
    const zeroPrevious = buildTrendComparison(
      trendFixture({
        orderCounts: [1],
        salesAmounts: [10],
        previous: { orderCount: 0, itemQuantity: 0, salesAmount: 0 },
      }),
    )
    expect(zeroPrevious.sales.text).toBe('—')
    expect(zeroPrevious.orders.text).toBe('—')
    expect(zeroPrevious.sales.tone).toBe('none')
  })

  it('Given 上一窗口基数极小 When 计算环比 Then 钳制展示避免失真百分比', () => {
    const comparison = buildTrendComparison(
      trendFixture({
        orderCounts: [100],
        salesAmounts: [500],
        previous: { orderCount: 1, itemQuantity: 1, salesAmount: 0.01 },
      }),
    )
    expect(comparison.sales.text).toBe('>999.9%')
    expect(comparison.sales.tone).toBe('up')
  })

  it('Given 双轴走势数据 When 计算几何 Then 生成连续订单线与可断开的销售额线', () => {
    const geometry = buildTrendGeometry(dataTrendFixture(), { width: 800, height: 240 })
    expect(geometry.points).toHaveLength(7)
    expect(geometry.showSales).toBe(true)
    expect(geometry.sales.lineSegments).toHaveLength(1)
    expect(geometry.orders.line.startsWith('M')).toBe(true)
    expect(geometry.xTicks.map((tick) => tick.label)).toEqual([
      '11-14',
      '11-15',
      '11-16',
      '11-17',
      '11-18',
      '11-19',
      '11-20',
    ])
    expect(geometry.sales.ticks).toHaveLength(6)
    expect(geometry.orders.ticks).toHaveLength(6)
    for (const point of geometry.points) {
      expect(Number.isFinite(point.x)).toBe(true)
      expect(Number.isFinite(point.yOrders)).toBe(true)
    }
  })

  it('Given 金额未知日期 When 计算几何 Then 销售额线在缺口处分段且不画 0', () => {
    const trend = trendFixture({
      orderCounts: [1, 1, 1, 1],
      salesAmounts: [10, null, null, 12],
    })
    const geometry = buildTrendGeometry(trend, { width: 600, height: 200 })
    expect(geometry.sales.lineSegments).toHaveLength(2)
    expect(geometry.sales.areaSegments).toHaveLength(2)
    expect(geometry.points[1]?.ySales).toBeNull()
  })

  it('Given 金额全部未知 When 计算几何 Then 只保留订单数序列与右轴', () => {
    const geometry = buildTrendGeometry(degradedTrendFixture(), { width: 600, height: 200 })
    expect(geometry.showSales).toBe(false)
    expect(geometry.sales.lineSegments).toHaveLength(0)
    expect(geometry.sales.ticks).toHaveLength(0)
    expect(geometry.orders.line).not.toBe('')
  })

  it('Given 低量程数据 When 计算几何 Then 双轴刻度可读且孤立金额点有标记', () => {
    const trend = trendFixture({
      orderCounts: [1, 1, 1, 0, 0, 0, 0],
      salesAmounts: [null, 0.01, null, 0, 0, 0, 0],
    })
    const geometry = buildTrendGeometry(trend, { width: 720, height: 240 })
    const salesLabels = geometry.sales.ticks.map((tick) => tick.label)
    expect(new Set(salesLabels).size).toBe(salesLabels.length)
    const orderLabels = geometry.orders.ticks.map((tick) => tick.label)
    expect(new Set(orderLabels).size).toBeGreaterThan(1)
    for (const label of orderLabels) expect(label).toMatch(/^\d+$/)
    expect(geometry.sales.markers).toHaveLength(1)
    expect(geometry.points[0]?.ySales).toBeNull()
  })

  it('Given 窗口含无订单补零日且金额全部未知 When 汇总 Then 返回 null 而不是 0', () => {
    const trend = trendFixture({
      orderCounts: [1, 0, 1],
      salesAmounts: [null, 0, null],
      amountKnownOrderCount: 0,
      amountUnknownOrderCount: 2,
    })
    expect(trendSalesTotal(trend)).toBeNull()
  })

  it('Given 30 天窗口 When 计算几何 Then 轴标签抽稀但保留首尾', () => {
    const geometry = buildTrendGeometry(trendFixture({ days: 30, orderCounts: [1] }), {
      width: 900,
      height: 240,
    })
    expect(geometry.points).toHaveLength(30)
    const labels = geometry.xTicks.map((tick) => tick.label)
    expect(labels.length).toBeLessThanOrEqual(8)
    expect(labels.at(0)).toBe('10-22')
    expect(labels.at(-1)).toBe('11-20')
  })
})

describe('走势区间', () => {
  const now = new Date(2026, 10, 22, 15, 30)

  it('Given 近 7/30 天 When 解析区间 Then 从本地今天向前推固定天数', () => {
    expect(resolveTrendRange('last7', now)).toEqual({ start: '2026-11-16', end: '2026-11-22' })
    expect(resolveTrendRange('last30', now)).toEqual({ start: '2026-10-24', end: '2026-11-22' })
  })

  it('Given 本季度 When 解析区间 Then 从自然季度第一天到今天', () => {
    expect(resolveTrendRange('quarter', now)).toEqual({ start: '2026-10-01', end: '2026-11-22' })
    expect(resolveTrendRange('quarter', new Date(2026, 0, 5))).toEqual({
      start: '2026-01-01',
      end: '2026-01-05',
    })
    expect(resolveTrendRange('quarter', new Date(2026, 3, 1))).toEqual({
      start: '2026-04-01',
      end: '2026-04-01',
    })
  })

  it('Given 本年 When 解析区间 Then 从自然年第一天到今天', () => {
    expect(resolveTrendRange('year', now)).toEqual({ start: '2026-01-01', end: '2026-11-22' })
  })

  it('Given 自定义区间 When 解析 Then 原样返回，未应用时回退近 7 天', () => {
    expect(resolveTrendRange('custom', now, { start: '2025-06-12', end: '2025-06-30' })).toEqual({
      start: '2025-06-12',
      end: '2025-06-30',
    })
    expect(resolveTrendRange('custom', now, null)).toEqual({
      start: '2026-11-16',
      end: '2026-11-22',
    })
  })

  it('Given 含闰日与跨年区间 When 计算天数 Then 含首尾且按日历日', () => {
    expect(trendRangeDays({ start: '2024-02-28', end: '2024-03-01' })).toBe(3)
    expect(trendRangeDays({ start: '2024-02-29', end: '2024-02-29' })).toBe(1)
    expect(trendRangeDays({ start: '2025-11-20', end: '2026-11-20' })).toBe(366)
  })

  it('Given 不同周期 When 生成区间描述 Then 短周期沿用原文、长周期补充起止日期', () => {
    expect(trendRangeLabel('last7', { start: '2026-11-16', end: '2026-11-22' })).toBe('近 7 天')
    expect(trendRangeLabel('last30', { start: '2026-10-24', end: '2026-11-22' })).toBe('近 30 天')
    expect(trendRangeLabel('quarter', { start: '2026-10-01', end: '2026-11-22' })).toBe(
      '本季度（2026-10-01 至 2026-11-22）',
    )
    expect(trendRangeLabel('year', { start: '2026-01-01', end: '2026-11-22' })).toBe(
      '本年（2026-01-01 至 2026-11-22）',
    )
    expect(trendRangeLabel('custom', { start: '2025-06-12', end: '2025-06-30' })).toBe(
      '自定义区间（2025-06-12 至 2025-06-30）',
    )
  })

  it('Given 自定义区间 When 校验 Then 拒绝倒序、超过 366 天、未来结束日与非法日期', () => {
    expect(validateTrendRange({ start: '2026-11-01', end: '2026-11-22' }, now)).toBeNull()
    expect(validateTrendRange({ start: '2025-11-20', end: '2026-11-20' }, now)).toBeNull()
    expect(validateTrendRange({ start: '2026-11-22', end: '2026-11-01' }, now)).toBe(
      '开始日期不能晚于结束日期。',
    )
    expect(validateTrendRange({ start: '2025-11-19', end: '2026-11-20' }, now)).toBe(
      '统计区间最长支持 366 天。',
    )
    expect(validateTrendRange({ start: '2026-11-01', end: '2026-11-23' }, now)).toBe(
      '结束日期不能晚于今天。',
    )
    expect(validateTrendRange({ start: '2026-02-30', end: '2026-03-01' }, now)).toBe(
      '请选择有效的开始与结束日期。',
    )
  })

  it('Given 0001..0099 年日历日期 When 校验与计算 Then 不按 1900 年代偏移', () => {
    expect(validateTrendRange({ start: '0001-01-01', end: '0001-01-10' }, now)).toBeNull()
    expect(trendRangeDays({ start: '0001-01-01', end: '0001-01-10' })).toBe(10)
    expect(validateTrendRange({ start: '0099-12-31', end: '0100-01-01' }, now)).toBeNull()
    expect(trendRangeDays({ start: '0099-12-31', end: '0100-01-01' })).toBe(2)
  })
})

describe('走势单点与刻度', () => {
  it('Given 单日窗口 When 计算几何 Then 订单数与销售额都产生可见标记', () => {
    const geometry = buildTrendGeometry(
      trendFixture({ days: 1, orderCounts: [2], salesAmounts: [5] }),
      { width: 800, height: 240 },
    )
    expect(geometry.points).toHaveLength(1)
    expect(geometry.orders.markers).toHaveLength(1)
    expect(geometry.sales.markers).toHaveLength(1)
  })

  it('Given 单日且金额未知 When 计算几何 Then 订单数标记仍可见', () => {
    const geometry = buildTrendGeometry(
      trendFixture({
        days: 1,
        orderCounts: [2],
        salesAmounts: [null],
        amountKnownOrderCount: 0,
        amountUnknownOrderCount: 2,
      }),
      { width: 800, height: 240 },
    )
    expect(geometry.showSales).toBe(false)
    expect(geometry.orders.markers).toHaveLength(1)
  })

  it('Given 32 天窄图 When 计算 x 轴刻度 Then 末尾两档保持可读间距', () => {
    const geometry = buildTrendGeometry(trendFixture({ days: 32, orderCounts: [1] }), {
      width: 326,
      height: 240,
    })
    const xs = geometry.xTicks.map((tick) => tick.x)
    expect(xs.length).toBeGreaterThan(1)
    expect(xs[xs.length - 1]! - xs[xs.length - 2]!).toBeGreaterThanOrEqual(40)
    expect(geometry.xTicks.at(-1)?.index).toBe(31)
  })

  it('Given 366 天跨年窗口 When 计算 x 轴刻度 Then 标签带年份避免重复', () => {
    const geometry = buildTrendGeometry(trendFixture({ days: 366, end: '2025-06-12' }), {
      width: 900,
      height: 240,
    })
    for (const tick of geometry.xTicks) expect(tick.label).toMatch(/^\d{4}-\d{2}$/)
    expect(new Set(geometry.xTicks.map((tick) => tick.label)).size).toBe(geometry.xTicks.length)
  })

  it('Given 本年至今 163 天 When 计算 x 轴刻度 Then 同一月份标签不重复', () => {
    const geometry = buildTrendGeometry(trendFixture({ days: 163, end: '2025-06-12' }), {
      width: 900,
      height: 240,
    })
    const labels = geometry.xTicks.map((tick) => tick.label)
    expect(new Set(labels).size).toBe(labels.length)
    for (const label of labels) expect(label).toMatch(/^\d{4}-\d{2}$/)
  })

  it('Given 366 天窄图 When 计算 x 轴刻度 Then 长标签按宽度抽稀不重叠', () => {
    const geometry = buildTrendGeometry(trendFixture({ days: 366, end: '2025-06-12' }), {
      width: 326,
      height: 240,
    })
    const ticks = geometry.xTicks
    expect(ticks.length).toBeLessThanOrEqual(5)
    for (let index = 1; index < ticks.length; index += 1)
      expect(ticks[index]!.x - ticks[index - 1]!.x).toBeGreaterThanOrEqual(56)
  })
})
