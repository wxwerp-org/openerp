export type ReportShop = {
  source: string
  platform: string
  shopId: string
  key: string
  label: string
}

export type ReportPlatformOrder = {
  number: string
  statusCode: string | null
  statusText: string | null
}

export type ReportItem = {
  lineId: string
  productId: string | null
  skuId: string | null
  skuCode: string | null
  quantity: number
  disabled: boolean | null
  appended: boolean | null
}

export type ReportOrder = {
  id: string
  number: string
  shop: ReportShop
  status: string
  platformOrders: ReportPlatformOrder[]
  items: ReportItem[]
  itemQuantity: number
}

/** 全量已同步订单的销售额汇总；金额未知时不补 0。 */
export type ReportSales = {
  amount: number | null
  currency: string | null
  knownOrderCount: number
  unknownOrderCount: number
}

/** 走势窗口内单日快照；缺日补零，未知金额保持 null。 */
export type TrendPoint = {
  date: string
  orderCount: number
  itemQuantity: number
  salesAmount: number | null
}

/** 与上一个等长窗口的汇总；无更早历史时为 null。 */
export type TrendPrevious = {
  orderCount: number
  itemQuantity: number
  salesAmount: number | null
}

export type ReportTrend = {
  days: number
  /** 窗口开始日期（含当天），与 points[0].date 一致。 */
  start: string
  end: string
  currency: string | null
  points: TrendPoint[]
  previous: TrendPrevious | null
  missingCreatedCount: number
  amountKnownOrderCount: number
  amountUnknownOrderCount: number
}

export type OrderSnapshotReport = {
  generatedAtUtc: string
  summary: {
    orderCount: number
    itemQuantity: number
  }
  sales: ReportSales
  trend: ReportTrend
  platforms: string[]
  shops: ReportShop[]
  groups: {
    shop: ReportShop
    orderCount: number
    itemQuantity: number
  }[]
  orders: ReportOrder[]
  page: number
  pageSize: number
  pageCount: number
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string'
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

function isShop(value: unknown): value is ReportShop {
  if (!isRecord(value)) return false
  return (
    typeof value.source === 'string' &&
    typeof value.platform === 'string' &&
    typeof value.shopId === 'string' &&
    typeof value.key === 'string' &&
    typeof value.label === 'string'
  )
}

function isPlatformOrder(value: unknown): value is ReportPlatformOrder {
  if (!isRecord(value)) return false
  return (
    typeof value.number === 'string' &&
    isNullableString(value.statusCode) &&
    isNullableString(value.statusText)
  )
}

function isItem(value: unknown): value is ReportItem {
  if (!isRecord(value)) return false
  return (
    typeof value.lineId === 'string' &&
    isNullableString(value.productId) &&
    isNullableString(value.skuId) &&
    isNullableString(value.skuCode) &&
    isFiniteNumber(value.quantity) &&
    (typeof value.disabled === 'boolean' || value.disabled === null) &&
    (typeof value.appended === 'boolean' || value.appended === null)
  )
}

function isOrder(value: unknown): value is ReportOrder {
  if (!isRecord(value)) return false
  return (
    typeof value.id === 'string' &&
    typeof value.number === 'string' &&
    isShop(value.shop) &&
    typeof value.status === 'string' &&
    Array.isArray(value.platformOrders) &&
    value.platformOrders.every(isPlatformOrder) &&
    Array.isArray(value.items) &&
    value.items.every(isItem) &&
    isFiniteNumber(value.itemQuantity)
  )
}

function isGroup(value: unknown): value is OrderSnapshotReport['groups'][number] {
  if (!isRecord(value)) return false
  return (
    isShop(value.shop) && Number.isInteger(value.orderCount) && isFiniteNumber(value.itemQuantity)
  )
}

function isCount(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0
}

/** 商品件数是来源行数量原值之和，来源允许负数行，因此与订单数区分校验。 */
function isSignedInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value)
}

function addDays(value: string, days: number): string {
  const date = new Date(`${value}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

function isNullableAmount(value: unknown): value is number | null {
  return value === null || isFiniteNumber(value)
}

function isDate(value: unknown): value is string {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const time = Date.parse(`${value}T00:00:00Z`)
  return Number.isFinite(time) && new Date(time).toISOString().startsWith(value)
}

function isSales(value: unknown): value is ReportSales {
  if (!isRecord(value)) return false
  return (
    isNullableAmount(value.amount) &&
    isNullableString(value.currency) &&
    isCount(value.knownOrderCount) &&
    isCount(value.unknownOrderCount)
  )
}

function isTrendPoint(value: unknown): value is TrendPoint {
  if (!isRecord(value)) return false
  return (
    isDate(value.date) &&
    isCount(value.orderCount) &&
    isSignedInteger(value.itemQuantity) &&
    isNullableAmount(value.salesAmount) &&
    (value.orderCount === 0 ? value.salesAmount === 0 : true)
  )
}

function isTrendPrevious(value: unknown): value is TrendPrevious {
  if (!isRecord(value)) return false
  // 环比商品量与当前窗口一致：来源商品行允许负数，不能按订单数规则要求非负。
  return (
    isCount(value.orderCount) &&
    isSignedInteger(value.itemQuantity) &&
    isNullableAmount(value.salesAmount)
  )
}

/** 工作台与报表共用的最长走势窗口，与后端校验保持一致。 */
export const MaxTrendDays = 366

/** 走势契约必须完整：窗口起止、逐日补零和环比汇总共同决定前端展示，不能容忍缺字段。 */
function isTrend(value: unknown): value is ReportTrend {
  if (!isRecord(value)) return false
  if (!(
    isCount(value.days) &&
    value.days >= 1 &&
    value.days <= MaxTrendDays &&
    isDate(value.start) &&
    isDate(value.end) &&
    isNullableString(value.currency) &&
    Array.isArray(value.points) &&
    value.points.length === value.days &&
    value.points.every(isTrendPoint) &&
    (value.previous === null || isTrendPrevious(value.previous)) &&
    isCount(value.missingCreatedCount) &&
    isCount(value.amountKnownOrderCount) &&
    isCount(value.amountUnknownOrderCount)
  ))
    return false

  const points = value.points as TrendPoint[]
  // 图表按数组顺序绘制，必须校验窗口从 start 连续、升序且恰好落在 end 上。
  if (points[0]?.date !== value.start) return false
  if (points[points.length - 1]?.date !== value.end) return false
  for (let index = 1; index < points.length; index += 1) {
    if (points[index]!.date !== addDays(points[index - 1]!.date, 1)) return false
  }
  // 金额计数与逐日订单数必须自洽，否则降级判断与环比会失真。
  const totalOrders = points.reduce((total, point) => total + point.orderCount, 0)
  if (value.amountKnownOrderCount + value.amountUnknownOrderCount !== totalOrders) return false
  // 有已知金额的订单必然落在某个「有订单且金额非 null」的日期上；全未知时不能出现这种日期。
  const hasKnownDay = points.some((point) => point.orderCount > 0 && point.salesAmount !== null)
  return value.amountKnownOrderCount === 0 ? !hasKnownDay : hasKnownDay
}

export function parseOrderSnapshotReport(value: unknown): OrderSnapshotReport {
  if (
    !isRecord(value) ||
    !isRecord(value.summary) ||
    typeof value.generatedAtUtc !== 'string' ||
    !value.generatedAtUtc.endsWith('Z') ||
    !Number.isFinite(Date.parse(value.generatedAtUtc)) ||
    !Number.isInteger(value.summary.orderCount) ||
    !isFiniteNumber(value.summary.itemQuantity) ||
    !isSales(value.sales) ||
    !isTrend(value.trend) ||
    !Array.isArray(value.platforms) ||
    !value.platforms.every((item) => typeof item === 'string') ||
    !Array.isArray(value.shops) ||
    !value.shops.every(isShop) ||
    !Array.isArray(value.groups) ||
    !value.groups.every(isGroup) ||
    !Array.isArray(value.orders) ||
    !value.orders.every(isOrder) ||
    typeof value.page !== 'number' ||
    !Number.isInteger(value.page) ||
    typeof value.pageSize !== 'number' ||
    !Number.isInteger(value.pageSize) ||
    value.page < 1 ||
    value.pageSize < 1 ||
    value.pageSize > 100 ||
    typeof value.pageCount !== 'number' ||
    !Number.isInteger(value.pageCount) ||
    value.pageCount < 1
  ) {
    throw new Error('报表数据格式无效，请刷新后重试。')
  }

  return value as unknown as OrderSnapshotReport
}

export function platformLabel(platform: string): string {
  return platform === 'TemuPop' ? 'Temu 半托管' : platform
}
