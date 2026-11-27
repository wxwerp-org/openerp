/** 订单接口（OpenERP.Api `/api/orders`） */
import request from '@/utils/http'

/** 订单中的一个商品行；规格 specs 为平台原始的 SKU 属性文本 */
export type OrderLine = {
  productId: string | null
  skuCode: string | null
  name: string | null
  imageUrl: string | null
  specs: string | null
  quantity: number
  unitPrice: number | null
  currency: string | null
  logistics: string | null
  details: OrderDetail[]
}

/** 平台无关的「名称 + 取值」补充信息，各平台统一按此输出 */
export type OrderDetail = {
  label: string
  value: string
  /** 平台无关的分组：buyer 买家 / payment 金额与支付 / logistics 物流 / status 状态 / other 其他 */
  group: string
}

export type Order = {
  id: string
  sid: number
  shopName: string | null
  platform: string
  platformOrders: { number: string; status: string; matched: boolean }[]
  created: string | null
  dispatched: string | null
  lineCount: number
  itemQuantity: number
  lines: OrderLine[]
  status: string | null
  buyer: string | null
  totalAmount: number | null
  totalCurrency: string | null
  logistics: string | null
  paid: string | null
  details: OrderDetail[]
}

export type OrderPage = {
  items: Order[]
  total: number
  page: number
  pageSize: number
  options: {
    shops: { sid: number; name: string | null }[]
    platforms: string[]
    statuses: string[]
  }
}

export type OrderQuery = {
  page: number
  pageSize: number
  shopSid?: string
  platform?: string
  status?: string
  number?: string
  createdFrom?: string
  createdTo?: string
}

/** 单个订单的完整信息（与列表项同一结构） */
export function fetchOrder(id: string, signal?: AbortSignal) {
  return request.get<Order>({
    url: '/api/orders/detail',
    params: { id },
    signal,
    fallbackMessage: '无法加载订单详情，请稍后重试。'
  })
}

/** 批量删除本地订单（软删除）；返回实际删除的数量 */
export function deleteOrders(ids: string[]) {
  return request.post<{ deleted: number }>({
    url: '/api/orders/delete',
    data: { ids },
    fallbackMessage: '无法删除订单，请稍后重试。'
  })
}

export type OrderLabels = {
  documents: { platformOrderNumber: string; fileName: string; contentType: string; content: string }[]
  failures: { platformOrderNumber: string; reason: string }[]
}

/** 按所选本地订单取得物流面单（后端按平台抽象，文档内容为 Base64） */
export function printOrderLabels(ids: string[]) {
  return request.post<OrderLabels>({
    url: '/api/orders/print-labels',
    data: { ids },
    fallbackMessage: '无法获取面单，请稍后重试。'
  })
}

export type ReportAmount ={ currency: string; orders: number; total: number }

/** 某个维度取值下的订单数、件数和按币种的金额 */
export type ReportBucket = {
  key: string
  label: string
  orders: number
  itemQuantity: number
  amounts: ReportAmount[]
}

export type ReportProduct = {
  productId: string | null
  skuCode: string | null
  name: string | null
  imageUrl: string | null
  quantity: number
  orders: number
}

export type OrderReport = {
  summary: {
    orders: number
    itemQuantity: number
    dispatched: number
    paid: number
    amounts: ReportAmount[]
  }
  daily: ReportBucket[]
  shops: ReportBucket[]
  platforms: ReportBucket[]
  statuses: ReportBucket[]
  logistics: ReportBucket[]
  products: ReportProduct[]
  options: { shops: { sid: number; name: string | null }[]; platforms: string[] }
}

export type OrderReportQuery = {
  shopSid?: string
  platform?: string
  createdFrom?: string
  createdTo?: string
}

/** 订单报表：按创建日期、店铺、平台筛选后的统计 */
export function fetchOrderReport(params: OrderReportQuery, signal?: AbortSignal) {
  return request.get<OrderReport>({
    url: '/api/orders/report',
    params,
    signal,
    fallbackMessage: '无法加载订单报表，请稍后重试。'
  })
}

export function fetchOrders(params: OrderQuery, signal?: AbortSignal) {
  return request.get<OrderPage>({
    url: '/api/orders',
    params,
    signal,
    fallbackMessage: '无法加载订单，请稍后重试。'
  })
}
