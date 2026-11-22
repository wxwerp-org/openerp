import type { OrderSyncTask } from '../composables/orderSyncTask'
import { MaxTrendDays } from '../reports/orderSnapshot'
import type { OrderSnapshotReport, ReportTrend, TrendPoint } from '../reports/orderSnapshot'
import {
  formatCompactMoney,
  formatCount,
  formatDateTime,
  formatMoney,
  formatShortDate,
  localDateString,
} from './workspaceData'

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

export type TrendTone = 'up' | 'down' | 'flat' | 'none'

export type TrendComparisonItem = {
  label: string
  text: string
  tone: TrendTone
}

export type TrendComparison = {
  sales: TrendComparisonItem
  orders: TrendComparisonItem
}

export type TrendViewState = 'empty' | 'degraded' | 'data'

/** 走势卡三态：无订单 → 空态；有订单但全无金额 → 金额权限降级态；否则有数据态。 */
export function resolveTrendState(trend: ReportTrend): TrendViewState {
  const orderCount = trend.points.reduce((total, point) => total + point.orderCount, 0)
  if (orderCount === 0) return 'empty'
  return trend.amountKnownOrderCount === 0 && trend.amountUnknownOrderCount > 0
    ? 'degraded'
    : 'data'
}

/** 窗口内已知金额之和；窗口有订单但全部金额未知时返回 null，不把未知当 0。 */
export function trendSalesTotal(trend: ReportTrend): number | null {
  // 无订单日的 salesAmount 是补零，不能据此把「全部金额未知」误判为已知 0。
  if (trend.amountKnownOrderCount === 0) return trendOrderTotal(trend) === 0 ? 0 : null
  return trend.points.reduce(
    (total, point) => (point.salesAmount === null ? total : total + point.salesAmount),
    0,
  )
}

export function trendOrderTotal(trend: ReportTrend): number {
  return trend.points.reduce((total, point) => total + point.orderCount, 0)
}

function formatPercent(
  current: number | null,
  previous: number | null,
): TrendComparisonItem['text'] {
  if (previous === null) return '无对比'
  if (current === null || previous === 0) return '—'
  const ratio = ((current - previous) / previous) * 100
  // 上一窗口基数极小时百分比会失真，钳制展示避免噪声数字挤占卡头。
  if (ratio > 999.9) return '>999.9%'
  if (ratio < -999.9) return '<-999.9%'
  return `${ratio >= 0 ? '+' : ''}${ratio.toFixed(1)}%`
}

function toneOf(current: number | null, previous: number | null): TrendTone {
  if (previous === null || current === null || previous === 0) return 'none'
  if (current > previous) return 'up'
  if (current < previous) return 'down'
  return 'flat'
}

/** 环比 = 本窗口与上一个等长窗口比较；无更早历史返回「无对比」。 */
export function buildTrendComparison(trend: ReportTrend): TrendComparison {
  const salesCurrent = trendSalesTotal(trend)
  const salesPrevious = trend.previous?.salesAmount ?? null
  const ordersCurrent = trendOrderTotal(trend)
  const ordersPrevious = trend.previous?.orderCount ?? null
  return {
    sales: {
      label: '销售额环比',
      text: formatPercent(salesCurrent, salesPrevious),
      tone: toneOf(salesCurrent, salesPrevious),
    },
    orders: {
      label: '订单数环比',
      text: formatPercent(ordersCurrent, ordersPrevious),
      tone: toneOf(ordersCurrent, ordersPrevious),
    },
  }
}

export type TrendChartAxisTick = { y: number; label: string }
export type TrendChartPoint = { index: number; x: number; ySales: number | null; yOrders: number }
export type TrendChartTick = { x: number; label: string; index: number }

export type TrendChartGeometry = {
  width: number
  height: number
  plot: { left: number; right: number; top: number; bottom: number }
  showSales: boolean
  xTicks: TrendChartTick[]
  sales: {
    ticks: TrendChartAxisTick[]
    lineSegments: string[]
    areaSegments: string[]
    /** 前后都无已知金额的孤立点，单点 path 不可见，需要额外标记。 */
    markers: { x: number; y: number }[]
    max: number
  }
  orders: {
    ticks: TrendChartAxisTick[]
    line: string
    /** 单日窗口的折线只有一个 moveto，SVG 不绘制，需要常驻圆点。 */
    markers: { x: number; y: number }[]
    max: number
  }
  points: TrendChartPoint[]
}

const AxisTickCount = 5

function axisTicks(
  max: number,
  top: number,
  bottom: number,
  label: (value: number) => string,
): TrendChartAxisTick[] {
  if (max <= 0) return [{ y: bottom, label: label(0) }]
  return Array.from({ length: AxisTickCount + 1 }, (_, index) => {
    const ratio = 1 - index / AxisTickCount
    return {
      y: top + (bottom - top) * (index / AxisTickCount),
      label: label(max * ratio),
    }
  })
}

/** 订单轴按整数步长取刻度，低量程时不会出现重复或小数标签。 */
function orderScale(maxOrderCount: number): { max: number; step: number } {
  const integerMax = Math.max(1, Math.ceil(maxOrderCount))
  const rough = Math.max(1, Math.ceil(integerMax / AxisTickCount))
  const exponent = 10 ** Math.floor(Math.log10(rough))
  const normalized = rough / exponent
  const step = Math.max(
    1,
    Math.round((normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * exponent),
  )
  return { max: step * AxisTickCount, step }
}

/**
 * 金额轴按「好看」步长取整；最小步长 0.01（后端金额精度为两位小数），
 * 避免低量程时多档刻度四舍五入成同一个标签。
 */
function salesScale(maxSales: number): { max: number; step: number } {
  const rough = Math.max(0.01, maxSales / AxisTickCount)
  const exponent = 10 ** Math.floor(Math.log10(rough))
  const normalized = rough / exponent
  const nice =
    normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 2.5 ? 2.5 : normalized <= 5 ? 5 : 10
  const step = Math.max(0.01, Math.round(nice * exponent * 100) / 100)
  return { max: step * AxisTickCount, step }
}

function salesLabel(max: number, step: number, currency: string | null): (value: number) => string {
  const digits = step < 1 ? 2 : step < 10 ? 1 : 0
  return (value) =>
    max >= 1000 ? formatCompactMoney(value, currency) : formatMoney(value, currency, digits)
}

function linePath(points: { x: number; y: number }[]): string {
  return points.map((point, index) => `${index === 0 ? 'M' : 'L'}${point.x} ${point.y}`).join(' ')
}

function salesPaths(
  points: TrendChartPoint[],
  bottom: number,
): { lineSegments: string[]; areaSegments: string[]; markers: { x: number; y: number }[] } {
  const lineSegments: string[] = []
  const areaSegments: string[] = []
  const markers: { x: number; y: number }[] = []
  let segment: { x: number; y: number }[] = []
  const flush = () => {
    if (segment.length === 0) return
    lineSegments.push(linePath(segment))
    if (segment.length === 1) markers.push(segment[0]!)
    const first = segment[0]!
    const last = segment[segment.length - 1]!
    areaSegments.push(`${linePath(segment)} L${last.x} ${bottom} L${first.x} ${bottom} Z`)
    segment = []
  }
  for (const point of points) {
    if (point.ySales === null) {
      flush()
      continue
    }
    segment.push({ x: point.x, y: point.ySales })
  }
  flush()
  return { lineSegments, areaSegments, markers }
}

function xTicks(points: TrendPoint[], xOf: (index: number) => number): TrendChartTick[] {
  if (points.length === 0) return []
  const longRange = points.length > 92
  const step = points.length <= 7 ? 1 : Math.ceil(points.length / 6)
  const indexes = new Set<number>()
  for (let index = 0; index < points.length; index += step) indexes.add(index)
  indexes.add(points.length - 1)
  let ticks = [...indexes]
    .sort((left, right) => left - right)
    .map((index) => ({
      index,
      x: xOf(index),
      // 超过一季度后 MM-DD 在跨年窗口里会重复，改用 YYYY-MM；季度内仍保留更细的日期。
      label: longRange ? points[index]!.date.slice(0, 7) : formatShortDate(points[index]!.date),
    }))
  // 长窗口固定步长可能在同一月份取到两档；同标签只保留更早的一档，避免轴标签重复。
  ticks = ticks.filter((tick, index) => index === 0 || tick.label !== ticks[index - 1]!.label)
  // 固定步长采样后再补末尾日期，可能和前一档只差几个像素；按最小可读间距贪心抽稀。
  const minGap = longRange ? 56 : 40
  const kept: TrendChartTick[] = []
  for (const tick of ticks) {
    if (kept.length === 0 || tick.x - kept[kept.length - 1]!.x >= minGap) kept.push(tick)
  }
  // 末尾日期必须保留：若它和上一档太近，就替换上一档，直到间距足够。
  const finalTick = ticks[ticks.length - 1]!
  if (kept[kept.length - 1] !== finalTick) {
    while (kept.length >= 1 && finalTick.x - kept[kept.length - 1]!.x < minGap) kept.pop()
    kept.push(finalTick)
  }
  return kept
}

/**
 * 双轴走势几何：销售额（左轴，可空、可断段）与订单数（右轴，连续）。
 * 宽度由容器测量后传入，纯函数以便单元测试；高度不足时把绘图区压到最小。
 */
export function buildTrendGeometry(
  trend: ReportTrend,
  layout: { width: number; height: number },
): TrendChartGeometry {
  const width = Math.max(240, Math.round(layout.width))
  const height = Math.max(120, Math.round(layout.height))
  const showSales = trend.amountKnownOrderCount > 0
  const left = showSales ? 62 : 16
  const right = width - 46
  const top = 12
  const bottom = Math.max(top + 40, height - 26)
  const points = trend.points
  const salesAxes = salesScale(
    points.reduce((max, point) => Math.max(max, point.salesAmount ?? 0), 0) * 1.05,
  )
  const salesMax = salesAxes.max
  const ordersScale = orderScale(
    points.reduce((max, point) => Math.max(max, point.orderCount), 0) * 1.05,
  )
  const ordersMax = ordersScale.max
  const xOf = (index: number) =>
    points.length <= 1 ? (left + right) / 2 : left + ((right - left) * index) / (points.length - 1)
  const ySales = (value: number | null): number | null => {
    if (!showSales || value === null) return null
    if (salesMax <= 0) return bottom
    return top + (bottom - top) * (1 - value / salesMax)
  }
  const yOrders = (value: number) =>
    top + (bottom - top) * (1 - Math.min(1, Math.max(0, value / ordersMax)))
  const chartPoints: TrendChartPoint[] = points.map((point, index) => ({
    index,
    x: xOf(index),
    ySales: ySales(point.salesAmount),
    yOrders: yOrders(point.orderCount),
  }))
  const paths = salesPaths(chartPoints, bottom)
  return {
    width,
    height,
    plot: { left, right, top, bottom },
    showSales,
    xTicks: xTicks(points, xOf),
    sales: {
      ticks: showSales
        ? axisTicks(salesMax, top, bottom, salesLabel(salesMax, salesAxes.step, trend.currency))
        : [],
      lineSegments: paths.lineSegments,
      areaSegments: paths.areaSegments,
      markers: paths.markers,
      max: salesMax,
    },
    orders: {
      ticks: axisTicks(ordersMax, top, bottom, (value) =>
        String(Math.round(value / Math.max(1, ordersScale.step)) * ordersScale.step),
      ),
      line: linePath(chartPoints.map((point) => ({ x: point.x, y: point.yOrders }))),
      markers:
        chartPoints.length === 1 ? [{ x: chartPoints[0]!.x, y: chartPoints[0]!.yOrders }] : [],
      max: ordersMax,
    },
    points: chartPoints,
  }
}

export type TrendPreset = 'last7' | 'last30' | 'quarter' | 'year' | 'custom'
export type TrendRange = { start: string; end: string }

export const TrendPresetLabels: Record<TrendPreset, string> = {
  last7: '近 7 天',
  last30: '近 30 天',
  quarter: '本季度',
  year: '本年',
  custom: '自定义区间',
}

type CalendarDate = { year: number; month: number; day: number }

/**
 * 自定义区间允许 0001..9999 的完整日历日期；不能用 `new Date(year, ...)`，
 * 因为它会把 0..99 年映射到 1900..1999 年。
 */
function parseCalendarDate(value: string): CalendarDate | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (!match) return null
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  if (year < 1 || month < 1 || month > 12 || day < 1) return null
  const probe = new Date(0)
  probe.setUTCHours(0, 0, 0, 0)
  probe.setUTCFullYear(year, month - 1, day)
  if (
    probe.getUTCFullYear() !== year ||
    probe.getUTCMonth() !== month - 1 ||
    probe.getUTCDate() !== day
  )
    return null
  return { year, month, day }
}

/** UTC 日序（1970-01-01 为 0），只用于日期差，不参与本地时间展示。 */
function calendarDayNumber(date: CalendarDate): number {
  const probe = new Date(0)
  probe.setUTCHours(0, 0, 0, 0)
  probe.setUTCFullYear(date.year, date.month - 1, date.day)
  return Math.round(probe.getTime() / 86_400_000)
}

function addDays(date: Date, days: number): Date {
  const result = new Date(date.getFullYear(), date.getMonth(), date.getDate())
  result.setDate(result.getDate() + days)
  return result
}

/** 含首尾的日历天数；用于校验后端 366 天上限与展示区间长度。 */
export function trendRangeDays(range: TrendRange): number {
  const start = parseCalendarDate(range.start)
  const end = parseCalendarDate(range.end)
  if (!start || !end) return 0
  return calendarDayNumber(end) - calendarDayNumber(start) + 1
}

/**
 * 预设区间按浏览器本地日期计算，结束日期固定为今天：
 * 近 7/30 天向前推固定天数，本季度/本年从自然季度、自然年的第一天开始（即「至今」）。
 */
export function resolveTrendRange(
  preset: TrendPreset,
  now = new Date(),
  custom: TrendRange | null = null,
): TrendRange {
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const end = localDateString(today)
  if (preset === 'custom')
    return custom ? { ...custom } : { start: localDateString(addDays(today, -6)), end }
  if (preset === 'last7') return { start: localDateString(addDays(today, -6)), end }
  if (preset === 'last30') return { start: localDateString(addDays(today, -29)), end }
  if (preset === 'quarter')
    return {
      start: localDateString(
        new Date(today.getFullYear(), Math.floor(today.getMonth() / 3) * 3, 1),
      ),
      end,
    }
  return { start: localDateString(new Date(today.getFullYear(), 0, 1)), end }
}

/** 脚注与无障碍摘要里的区间描述：短周期沿用原文，长周期补充实际起止日期。 */
export function trendRangeLabel(preset: TrendPreset, range: TrendRange): string {
  const label = TrendPresetLabels[preset]
  return preset === 'last7' || preset === 'last30'
    ? label
    : `${label}（${range.start} 至 ${range.end}）`
}

/** 自定义区间只允许今天及以前，避免把未来空日计入走势和环比窗口。 */
export function validateTrendRange(range: TrendRange, now = new Date()): string | null {
  const start = parseCalendarDate(range.start)
  const end = parseCalendarDate(range.end)
  if (!start || !end) return '请选择有效的开始与结束日期。'
  if (range.start > range.end) return '开始日期不能晚于结束日期。'
  if (trendRangeDays(range) > MaxTrendDays) return `统计区间最长支持 ${MaxTrendDays} 天。`
  if (range.end > localDateString(new Date(now.getFullYear(), now.getMonth(), now.getDate())))
    return '结束日期不能晚于今天。'
  return null
}
