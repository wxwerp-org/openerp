import type { OrderSyncTask } from '../composables/orderSyncTask'
import type { OrderSnapshotReport } from '../reports/orderSnapshot'
import { formatCount, formatDateTime } from './workspaceData'

export type DashboardState = 'loading' | 'error' | 'empty' | 'syncing' | 'data'

export type DashboardStateInput = {
  hasSnapshot: boolean
  reportStatus: 'loading' | 'success' | 'error'
  /** 报表快照推导：失败刷新时仍用上次快照的订单数。 */
  orderCount: number
  runningOrderTasks: number
  administrator: boolean
  shopSyncRunning: boolean
  shopSyncStarting: boolean
}

/** 工作台三态 + 首屏错误态；刷新失败但保留快照时按快照推导。 */
export function resolveDashboardState(input: DashboardStateInput): DashboardState {
  if (!input.hasSnapshot) return input.reportStatus === 'error' ? 'error' : 'loading'
  if (input.orderCount > 0) return 'data'
  const syncing =
    input.runningOrderTasks > 0 ||
    (input.administrator && (input.shopSyncRunning || input.shopSyncStarting))
  return syncing ? 'syncing' : 'empty'
}

export type OnboardingStep = {
  id: 'configure' | 'authorize' | 'sync-directory' | 'sync-orders'
  title: string
  description: string
  icon: string
  done: boolean
  executable: boolean
}

export type OnboardingInput = {
  configured: boolean | null
  directoryItems: number
  directoryLoaded: boolean
  validTemuShopCount: number
  lastSyncedAt: string | null
  orderCount: number
  orderSyncEnabled: boolean
}

/** 四步初始化引导：第 1 步是唯一硬前置，第 2 步只能由目录结果确认。 */
export function buildOnboardingSteps(input: OnboardingInput): OnboardingStep[] {
  const configured = input.configured === true
  const hasValidTemuShop = input.validTemuShopCount > 0
  const directorySynced = input.lastSyncedAt != null
  const ordersSynced = input.orderCount > 0
  const tokenHasNoShops = input.directoryLoaded && input.directoryItems === 0

  return [
    {
      id: 'configure',
      title: '配置 ERP 接入',
      description: configured ? '私有 ERP AccessToken 已配置' : '填写私有 ERP AccessToken',
      icon: 'i-guide-account',
      done: configured,
      executable: !configured,
    },
    {
      id: 'authorize',
      title: '去 wxw 授权店铺',
      description: hasValidTemuShop
        ? `已授权 ${input.validTemuShopCount} 家 TemuPop 店铺`
        : '授权后请重新同步店铺目录',
      icon: 'i-guide-shield',
      done: hasValidTemuShop,
      executable: configured && !hasValidTemuShop,
    },
    {
      id: 'sync-directory',
      title: '同步店铺目录',
      description: tokenHasNoShops
        ? 'Token 无可见店铺'
        : directorySynced
          ? `最近同步 ${formatDateTime(input.lastSyncedAt)}`
          : '拉取 Token 可见店铺与授权状态',
      icon: 'i-guide-product',
      done: directorySynced,
      executable: configured && !directorySynced,
    },
    {
      id: 'sync-orders',
      title: '同步历史订单',
      description: ordersSynced
        ? `已同步 ${formatCount(input.orderCount)} 单`
        : !hasValidTemuShop
          ? '需先在上游授权 TemuPop 店铺'
          : !input.orderSyncEnabled
            ? '当前环境未开启订单同步'
            : '拉取一家 TemuPop 店铺的历史订单',
      icon: 'i-guide-supply',
      done: ordersSynced,
      executable: configured && hasValidTemuShop && input.orderSyncEnabled && !ordersSynced,
    },
  ]
}

export function nextOnboardingLabel(steps: OnboardingStep[]): string {
  return steps.find((step) => !step.done)?.title ?? '初始化完成'
}

export type RankedShop = {
  key: string
  name: string
  orderCount: number
  itemQuantity: number
}

/** Top5：件数降序、订单数降序、完整店铺键升序；店名缺失回退「店铺 <SID>」。 */
export function rankTopShops(
  groups: OrderSnapshotReport['groups'],
  shopNames: Map<string, string>,
  limit = 5,
): RankedShop[] {
  return [...groups]
    .sort(
      (left, right) =>
        right.itemQuantity - left.itemQuantity ||
        right.orderCount - left.orderCount ||
        left.shop.key.localeCompare(right.shop.key),
    )
    .slice(0, limit)
    .map((group) => ({
      key: group.shop.key,
      name:
        shopNames.get(`${group.shop.platform}/${group.shop.shopId}`) ??
        shopNames.get(group.shop.key) ??
        `店铺 ${group.shop.shopId}`,
      orderCount: group.orderCount,
      itemQuantity: group.itemQuantity,
    }))
}

/** 报表完整店铺键原样用于下钻，不做平台/店铺拆分假设。 */
export function shopDrillQuery(key: string): { shops: string } {
  return { shops: key }
}

export function platformDrillQuery(platform: string): { platform: string } {
  return { platform }
}

export type SyncTaskGroup = {
  running: OrderSyncTask[]
  queued: number
}

export function groupSyncTasks(tasks: OrderSyncTask[]): SyncTaskGroup {
  return {
    running: tasks.filter((task) => task.status === 'running' && task.stage !== 'queued'),
    queued: tasks.filter((task) => task.status === 'running' && task.stage === 'queued').length,
  }
}
