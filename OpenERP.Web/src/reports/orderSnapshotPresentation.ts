import type { OrderSnapshotReport } from './orderSnapshot'
import { platformLabel } from './orderSnapshot'

export type SnapshotMetric = 'itemQuantity' | 'orderCount'

export type ShopRankingRow = {
  shop: OrderSnapshotReport['groups'][number]['shop']
  orderCount: number
  itemQuantity: number
  barPercentage: number
}

export type PlatformDistributionRow = {
  platform: string
  label: string
  orderCount: number
  percentage: number
  color: string
}

export function averageItemsPerOrder(summary: OrderSnapshotReport['summary']): number | null {
  return summary.orderCount > 0 ? summary.itemQuantity / summary.orderCount : null
}

const snapshotQuantityFormatter = new Intl.NumberFormat('zh-CN')

/** 数量展示统一使用 zh-CN 千分位；模块级单例避免重复实例化。 */
export function formatSnapshotQuantity(value: number): string {
  return snapshotQuantityFormatter.format(value)
}

/** 平均每单件数：非整数保留一位小数，整数与整数部分统一千分位。 */
export function formatAverageItems(value: number): string {
  const [integer, decimal] = value.toFixed(1).split('.')
  const whole = formatSnapshotQuantity(Number(integer))
  return decimal === '0' ? whole : `${whole}.${decimal}`
}

export function shopRanking(
  groups: OrderSnapshotReport['groups'],
  metric: SnapshotMetric,
  limit = 5,
): ShopRankingRow[] {
  const sorted = [...groups].sort((left, right) => {
    const diff = right[metric] - left[metric]
    return diff !== 0 ? diff : left.shop.key.localeCompare(right.shop.key)
  })

  const rows = sorted.slice(0, Math.max(0, limit)).map((group) => ({
    shop: group.shop,
    orderCount: group.orderCount,
    itemQuantity: group.itemQuantity,
    barPercentage: 0,
  }))

  const max = rows.reduce((value, row) => Math.max(value, row[metric]), 0)
  for (const row of rows) {
    row.barPercentage = max > 0 ? row[metric] / max : 0
  }

  return rows
}

const platformColors = ['#005BF5', '#65A6FF', '#A8CBFF', '#D5E5FF']

export function platformDistribution(
  groups: OrderSnapshotReport['groups'],
  totalOrders: number,
): PlatformDistributionRow[] {
  const orderCountByPlatform = new Map<string, number>()
  for (const group of groups) {
    orderCountByPlatform.set(
      group.shop.platform,
      (orderCountByPlatform.get(group.shop.platform) ?? 0) + group.orderCount,
    )
  }

  return [...orderCountByPlatform.entries()]
    .map(([platform, orderCount]) => ({ platform, orderCount }))
    .sort((left, right) => {
      const diff = right.orderCount - left.orderCount
      return diff !== 0 ? diff : left.platform.localeCompare(right.platform)
    })
    .map((entry, index) => ({
      platform: entry.platform,
      label: platformLabel(entry.platform),
      orderCount: entry.orderCount,
      percentage: totalOrders > 0 ? entry.orderCount / totalOrders : 0,
      color: platformColors[index % platformColors.length] as string,
    }))
}
