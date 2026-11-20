import { describe, expect, it } from 'vitest'
import type { OrderSyncTask } from '../../src/composables/orderSyncTask'
import type { OrderSnapshotReport } from '../../src/reports/orderSnapshot'
import {
  buildOnboardingSteps,
  groupSyncTasks,
  nextOnboardingLabel,
  rankTopShops,
  resolveDashboardState,
  type DashboardStateInput,
  type OnboardingInput,
} from '../../src/workspace/workspaceDerivation'
import { platformTotals } from '../../src/workspace/workspaceData'

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
