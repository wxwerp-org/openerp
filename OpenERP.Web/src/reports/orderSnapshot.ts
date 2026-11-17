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

export type OrderSnapshotReport = {
  generatedAtUtc: string
  summary: {
    orderCount: number
    itemQuantity: number
  }
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
  return typeof value.source === 'string' &&
    typeof value.platform === 'string' &&
    typeof value.shopId === 'string' &&
    typeof value.key === 'string' &&
    typeof value.label === 'string'
}

function isPlatformOrder(value: unknown): value is ReportPlatformOrder {
  if (!isRecord(value)) return false
  return typeof value.number === 'string' &&
    isNullableString(value.statusCode) &&
    isNullableString(value.statusText)
}

function isItem(value: unknown): value is ReportItem {
  if (!isRecord(value)) return false
  return typeof value.lineId === 'string' &&
    isNullableString(value.productId) &&
    isNullableString(value.skuId) &&
    isNullableString(value.skuCode) &&
    isFiniteNumber(value.quantity) &&
    (typeof value.disabled === 'boolean' || value.disabled === null) &&
    (typeof value.appended === 'boolean' || value.appended === null)
}

function isOrder(value: unknown): value is ReportOrder {
  if (!isRecord(value)) return false
  return typeof value.id === 'string' &&
    typeof value.number === 'string' &&
    isShop(value.shop) &&
    typeof value.status === 'string' &&
    Array.isArray(value.platformOrders) && value.platformOrders.every(isPlatformOrder) &&
    Array.isArray(value.items) && value.items.every(isItem) &&
    isFiniteNumber(value.itemQuantity)
}

function isGroup(value: unknown): value is OrderSnapshotReport['groups'][number] {
  if (!isRecord(value)) return false
  return isShop(value.shop) &&
    Number.isInteger(value.orderCount) &&
    isFiniteNumber(value.itemQuantity)
}

export function parseOrderSnapshotReport(value: unknown): OrderSnapshotReport {
  if (!isRecord(value) || !isRecord(value.summary) ||
    typeof value.generatedAtUtc !== 'string' || !value.generatedAtUtc.endsWith('Z') ||
    !Number.isFinite(Date.parse(value.generatedAtUtc)) ||
    !Number.isInteger(value.summary.orderCount) ||
    !isFiniteNumber(value.summary.itemQuantity) ||
    !Array.isArray(value.platforms) || !value.platforms.every((item) => typeof item === 'string') ||
    !Array.isArray(value.shops) || !value.shops.every(isShop) ||
    !Array.isArray(value.groups) || !value.groups.every(isGroup) ||
    !Array.isArray(value.orders) || !value.orders.every(isOrder) ||
    typeof value.page !== 'number' || !Number.isInteger(value.page) ||
    typeof value.pageSize !== 'number' || !Number.isInteger(value.pageSize) ||
    value.page < 1 || value.pageSize < 1 || value.pageSize > 100 ||
    typeof value.pageCount !== 'number' || !Number.isInteger(value.pageCount) || value.pageCount < 1) {
    throw new Error('报表数据格式无效，请刷新后重试。')
  }

  return value as unknown as OrderSnapshotReport
}

export function platformLabel(platform: string): string {
  return platform === 'TemuPop' ? 'Temu 半托管' : platform
}
