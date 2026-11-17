import { createServer, type IncomingMessage, type ServerResponse } from 'node:http'

const sid = 30
export const testShopName = 'E2E 订单同步验收店铺'
const pageSize = 20

type StubLine = {
  opid: string | null
  oguid: string | null
  skuId: string | null
  skuCode: string | null
  qty: number
  enabled: boolean | null
  append: boolean | null
}

type StubPlatformOrder = {
  oid: string
  sid: number
  statusCode: string
  statusText: string
}

type StubOrder = {
  erpId: string
  orderNumber: string
  statusCode: string
  platformOrders: StubPlatformOrder[]
  lines: StubLine[]
  times: { name: string; time: string }[]
}

const line = (
  opid: string | null,
  oguid: string | null,
  skuId: string | null,
  skuCode: string | null,
  qty: number,
  enabled: boolean | null,
  append: boolean | null,
): StubLine => ({ opid, oguid, skuId, skuCode, qty, enabled, append })

function makeLines(order: number): StubLine[] {
  if (order === 1) {
    const duplicate = line('PRODUCT-1', 'GUID-1', 'SKU-1', 'MERCHANT-1', 2, false, false)
    return [
      duplicate,
      { ...duplicate },
      line('PRODUCT-1', 'GUID-1B', 'SKU-1', 'MERCHANT-1', 3, false, true),
    ]
  }

  return [
    line(
      order % 4 === 0 ? null : `PRODUCT-${order}`,
      order % 5 === 0 ? null : `GUID-${order}`,
      `SKU-${(order % 6) + 1}`,
      order % 3 === 0 ? null : `MERCHANT-${order}`,
      (order % 5) + 1,
      order % 7 === 0 ? null : order % 2 === 0,
      order % 6 === 0 ? null : order % 3 === 0,
    ),
  ]
}

function makeTimes(order: number): { name: string; time: string }[] {
  const date = String(order).padStart(2, '0')
  const times = [{ name: '_创建', time: `2025-10-${date} 08:00` }]
  if (order % 2 === 0) times.push({ name: '_付款', time: `2025-10-${date} 09:15` })
  if (order % 3 === 0) times.push({ name: '_出库', time: `2025-10-${date} 13:40` })
  if (order % 7 === 0) times.push({ name: '_退款', time: `2025-10-${date} 16:20` })
  return times
}

const stubOrders: StubOrder[] = Array.from({ length: 21 }, (_, index) => {
  const order = index + 1
  const id = order.toString(16).padStart(24, '0')
  const platformOrders = order === 1 || order % 6 === 0
    ? [
        { oid: `PO-${String(order).padStart(4, '0')}-A`, sid, statusCode: '4', statusText: '已发货' },
        { oid: `PO-${String(order).padStart(4, '0')}-B`, sid, statusCode: '2', statusText: '待发货' },
      ]
    : [{
        oid: `PO-${String(order).padStart(4, '0')}-A`,
        sid,
        statusCode: order % 2 === 0 ? '4' : '2',
        statusText: order % 2 === 0 ? '已发货' : '待发货',
      }]

  return {
    erpId: id,
    orderNumber: `E2E-ORDER-${String(order).padStart(4, '0')}`,
    statusCode: order % 4 === 0 ? 'Completed' : 'Normal',
    platformOrders,
    lines: makeLines(order),
    times: makeTimes(order),
  }
})

type ExpectedOrder = {
  erpId: string
  orderNumber: string
  sid: number
  platform: string
  status: string
  createdRaw: string | null
  createdSource: string | null
  paidRaw: string | null
  paidSource: string | null
  dispatchedRaw: string | null
  dispatchedSource: string | null
  refundedRaw: string | null
  refundedSource: string | null
}

type ExpectedPlatformOrder = {
  erpId: string
  poNumber: string
  sid: number
  statusCode: string | null
  statusText: string | null
}

type ExpectedOrderLine = {
  erpId: string
  productId: string | null
  oguid: string | null
  skuId: string | null
  skuCode: string | null
  quantity: number
  disabled: boolean | null
  appended: boolean | null
}

function expectedTime(order: StubOrder, name: string): string | null {
  return order.times.find(time => time.name === name)?.time ?? null
}

/** 供隔离宿主的数据库快照逐列、多重集对账；商品行故意不导出 Ordinal。 */
export const expectedDatabase: {
  orders: ExpectedOrder[]
  platformOrders: ExpectedPlatformOrder[]
  orderLines: ExpectedOrderLine[]
} = {
  orders: stubOrders.map(order => ({
    erpId: order.erpId,
    orderNumber: order.orderNumber,
    sid,
    platform: 'TemuPop',
    status: order.statusCode,
    createdRaw: expectedTime(order, '_创建'),
    createdSource: expectedTime(order, '_创建') ? 'list' : null,
    paidRaw: expectedTime(order, '_付款'),
    paidSource: expectedTime(order, '_付款') ? 'list' : null,
    dispatchedRaw: expectedTime(order, '_出库'),
    dispatchedSource: expectedTime(order, '_出库') ? 'list' : null,
    refundedRaw: expectedTime(order, '_退款'),
    refundedSource: expectedTime(order, '_退款') ? 'list' : null,
  })),
  platformOrders: stubOrders.flatMap(order => order.platformOrders.map(po => ({
    erpId: order.erpId,
    poNumber: po.oid,
    sid: po.sid,
    statusCode: po.statusCode,
    statusText: po.statusText,
  }))),
  orderLines: stubOrders.flatMap(order => order.lines.map(product => ({
    erpId: order.erpId,
    productId: product.opid,
    oguid: product.oguid,
    skuId: product.skuId,
    skuCode: product.skuCode,
    quantity: product.qty,
    disabled: product.enabled,
    appended: product.append,
  }))),
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function sendJson(response: ServerResponse, status: number, body: unknown): void {
  response.writeHead(status, { 'content-type': 'application/json; charset=utf-8' })
  response.end(JSON.stringify(body))
}

async function readJson(request: IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = []
  let length = 0
  for await (const chunk of request) {
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk)
    length += buffer.length
    if (length > 1024 * 1024) throw new Error('Request body is too large.')
    chunks.push(buffer)
  }
  return JSON.parse(Buffer.concat(chunks).toString('utf8')) as unknown
}

function hasShopFilter(body: Record<string, unknown>): boolean {
  const filters = body.filters
  if (!isRecord(filters) || filters.status !== 'all' || filters.gid !== 'all') return false
  if (JSON.stringify(filters.platforms) !== '[40]') return false
  const shops = filters.shops
  if (!isRecord(shops) || !Array.isArray(shops.values) || shops.values.length !== 1) return false
  const first = shops.values[0]
  if (!isRecord(first) || !Array.isArray(first.items) || first.items.length !== 1) return false
  const item = first.items[0]
  return isRecord(item) && item.key === String(sid)
}

function invalidRequest(response: ServerResponse, message: string): void {
  sendJson(response, 400, { error: message })
}

async function handleRequest(
  request: IncomingMessage,
  response: ServerResponse,
  token: string,
): Promise<void> {
  if (request.method !== 'POST') {
    sendJson(response, 405, { error: 'Only POST is supported.' })
    return
  }
  if (request.headers.accesstoken !== token) {
    sendJson(response, 401, { error: 'Invalid test token.' })
    return
  }

  const pathname = new URL(request.url ?? '/', 'http://127.0.0.1').pathname
  const bodyValue = await readJson(request)
  if (!isRecord(bodyValue)) {
    invalidRequest(response, 'Expected a JSON object.')
    return
  }
  const body = bodyValue

  if (pathname === '/open-api/shop/get_shops') {
    if (body.page !== 1 || ![20, 100].includes(Number(body.page_size)) || body.platform !== 'TemuPop') {
      invalidRequest(response, 'Invalid shop pagination or platform filter.')
      return
    }
    sendJson(response, 200, {
      success: true,
      data_count: 1,
      page_count: 1,
      data: {
        items: [{ sid: String(sid), name: testShopName, platform: 'TemuPop', region: 'US' }],
      },
    })
    return
  }

  if (pathname === '/open-api/shop/get_status') {
    if (Number(body.sid) !== sid) {
      invalidRequest(response, 'Unexpected shop SID.')
      return
    }
    sendJson(response, 200, { success: true, data: { isTokenOk: true } })
    return
  }

  if (pathname === '/open-api/order/get_list_page_count') {
    if (!hasShopFilter(body) || body.page_size !== pageSize) {
      invalidRequest(response, 'Invalid order pagination or shop/platform filter.')
      return
    }
    sendJson(response, 200, {
      success: true,
      data: { data_count: stubOrders.length, page_count: Math.ceil(stubOrders.length / pageSize) },
    })
    return
  }

  if (pathname === '/open-api/order/get_list') {
    if (!hasShopFilter(body) || body.page_size !== pageSize ||
        ![1, 2].includes(Number(body.page)) || !isRecord(body.sort) ||
        body.sort.field !== 'w_order._id' || body.sort.order !== 'desc') {
      invalidRequest(response, 'Invalid order pagination, sorting, or shop/platform filter.')
      return
    }
    const currentPage = Number(body.page)
    const pageOrders = [...stubOrders].reverse().slice((currentPage - 1) * pageSize, currentPage * pageSize)
    sendJson(response, 200, {
      success: true,
      data: {
        orders: pageOrders.map(order => ({
          id: order.erpId,
          orderid: order.orderNumber,
          sub_orders: order.platformOrders.map(po => ({ id: po.oid, sid: po.sid, status: po.statusText })),
          times: order.times,
        })),
      },
    })
    return
  }

  if (pathname === '/open-api/order_detail/get_base') {
    if (body.ask !== false || body.showAllProduct !== true || typeof body.id !== 'string') {
      invalidRequest(response, 'Invalid detail request.')
      return
    }
    const order = stubOrders.find(item => item.erpId === body.id)
    if (!order) {
      invalidRequest(response, 'Unknown order ID.')
      return
    }
    sendJson(response, 200, {
      success: true,
      data: {
        orderId: order.orderNumber,
        shop: { sid, platform: 'TemuPop' },
        status: { key: order.statusCode },
        platformOrders: order.platformOrders.map(po => ({
          oid: po.oid,
          status: { key: po.statusCode, text: po.statusText },
        })),
        products: order.lines,
      },
    })
    return
  }

  sendJson(response, 404, { error: 'Unknown wxw test endpoint.' })
}

export async function startWxwStub(token: string): Promise<{ url: string; close: () => Promise<void> }> {
  if (!token.trim()) throw new Error('A non-empty wxw test token is required.')

  const server = createServer((request, response) => {
    void handleRequest(request, response, token).catch(() => {
      if (!response.headersSent) sendJson(response, 400, { error: 'Invalid wxw test request.' })
      else response.destroy()
    })
  })

  await new Promise<void>((resolve, reject) => {
    server.once('error', reject)
    server.listen(0, '127.0.0.1', () => {
      server.removeListener('error', reject)
      resolve()
    })
  })

  const address = server.address()
  if (!address || typeof address === 'string') {
    await new Promise<void>(resolve => server.close(() => resolve()))
    throw new Error('Could not determine the wxw test stub port.')
  }

  let closed = false
  return {
    url: `http://127.0.0.1:${address.port}`,
    close: async () => {
      if (closed) return
      closed = true
      await new Promise<void>((resolve, reject) => {
        server.close(error => error ? reject(error) : resolve())
      })
    },
  }
}
