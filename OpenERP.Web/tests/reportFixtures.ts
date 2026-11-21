import type { ReportSales, ReportTrend, TrendPoint } from '../src/reports/orderSnapshot'

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

/** 生成以 end 结尾、按天升序的日期（UTC 口径，仅用于前端夹具）。 */
function datesEndingAt(end: string, days: number): string[] {
  const endDate = new Date(`${end}T00:00:00Z`)
  return Array.from({ length: days }, (_, index) => {
    const date = new Date(endDate)
    date.setUTCDate(endDate.getUTCDate() - (days - 1 - index))
    return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`
  })
}

export type TrendFixtureOptions = {
  days?: 7 | 30
  end?: string
  currency?: string | null
  orderCounts?: number[]
  itemQuantities?: number[]
  salesAmounts?: (number | null)[]
  previous?: { orderCount: number; itemQuantity: number; salesAmount: number | null } | null
  missingCreatedCount?: number
  amountKnownOrderCount?: number
  amountUnknownOrderCount?: number
}

export function trendFixture(options: TrendFixtureOptions = {}): ReportTrend {
  const days = options.days ?? 7
  const end = options.end ?? '2026-11-20'
  const dates = datesEndingAt(end, days)
  const points: TrendPoint[] = dates.map((date, index) => ({
    date,
    orderCount: options.orderCounts?.[index] ?? 0,
    itemQuantity: options.itemQuantities?.[index] ?? 0,
    salesAmount: options.salesAmounts?.[index] === undefined ? 0 : options.salesAmounts[index]!,
  }))
  const amountKnownOrderCount =
    options.amountKnownOrderCount ??
    points
      .filter((point) => point.orderCount > 0 && point.salesAmount !== null)
      .reduce((total, point) => total + point.orderCount, 0)
  const amountUnknownOrderCount =
    options.amountUnknownOrderCount ??
    points
      .filter((point) => point.orderCount > 0 && point.salesAmount === null)
      .reduce((total, point) => total + point.orderCount, 0)
  return {
    days,
    end,
    currency: options.currency === undefined ? 'USD' : options.currency,
    points,
    previous: options.previous === undefined ? null : options.previous,
    missingCreatedCount: options.missingCreatedCount ?? 0,
    amountKnownOrderCount,
    amountUnknownOrderCount,
  }
}

export function salesFixture(
  amount: number | null = 371.12,
  currency: string | null = 'USD',
  knownOrderCount = 42,
  unknownOrderCount = 0,
): ReportSales {
  return { amount, currency, knownOrderCount, unknownOrderCount }
}

/** 工作台有数据态的走势夹具：近 7 天有订单、金额已知，且存在上一等长窗口。 */
export function dataTrendFixture(): ReportTrend {
  return trendFixture({
    orderCounts: [1, 0, 2, 0, 1, 0, 3],
    itemQuantities: [2, 0, 5, 0, 2, 0, 6],
    salesAmounts: [12.5, 0, 20.25, 0, 5, 0, 9.25],
    previous: { orderCount: 5, itemQuantity: 8, salesAmount: 40 },
  })
}

/** 工作台空态的走势夹具：窗口内无订单且无更早历史。 */
export function emptyTrendFixture(): ReportTrend {
  return trendFixture()
}

/** 金额权限降级态：有订单但全部金额未知。 */
export function degradedTrendFixture(): ReportTrend {
  return trendFixture({
    orderCounts: [1, 2, 0, 1, 0, 0, 1],
    itemQuantities: [2, 3, 0, 1, 0, 0, 1],
    salesAmounts: [null, null, 0, null, 0, 0, null],
    amountKnownOrderCount: 0,
    amountUnknownOrderCount: 5,
    previous: { orderCount: 4, itemQuantity: 6, salesAmount: null },
  })
}
