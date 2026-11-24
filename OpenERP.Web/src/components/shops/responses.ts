import type { Shop, ShopPlatformOption, ShopSummary } from './types'

/** 授权店铺页读取的接口响应：格式校验、兜底值与错误文案。 */

export type ShopsResponse = {
  items: Shop[]
  page: number
  pageSize: number
  totalCount: number
  pageCount: number
  summary: ShopSummary
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object'
}

function nullableString(value: unknown): string | null {
  return typeof value === 'string' ? value : null
}

function nullableBoolean(value: unknown): boolean | null {
  return typeof value === 'boolean' ? value : null
}

function numberOr(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback
}

export function parseShopsResponse(value: unknown): ShopsResponse {
  if (!isRecord(value) || !Array.isArray(value.items)) throw new Error('本地店铺响应格式无效。')

  const items = value.items.map(parseShop)
  const summaryValue = isRecord(value.summary) ? value.summary : {}
  return {
    items,
    page: numberOr(value.page, 1),
    pageSize: numberOr(value.pageSize, 20),
    totalCount: numberOr(value.totalCount, items.length),
    pageCount: numberOr(value.pageCount, 0),
    summary: {
      valid: numberOr(summaryValue.valid, 0),
      invalid: numberOr(summaryValue.invalid, 0),
      unknown: numberOr(summaryValue.unknown, 0),
      lastSyncedAt: nullableString(summaryValue.lastSyncedAt),
      syncVersion: numberOr(summaryValue.syncVersion, 0),
    },
  }
}

function parseShop(value: unknown): Shop {
  if (!isRecord(value)) throw new Error('本地店铺响应格式无效。')

  const sid =
    typeof value.sid === 'string'
      ? value.sid
      : typeof value.sid === 'number'
        ? String(value.sid)
        : ''
  if (!sid || typeof value.name !== 'string' || typeof value.platform !== 'string')
    throw new Error('本地店铺响应格式无效。')

  return {
    sid,
    name: value.name,
    platform: value.platform,
    region: nullableString(value.region),
    memberId: nullableString(value.memberId),
    isTokenOk: nullableBoolean(value.isTokenOk),
    tokenError: nullableString(value.tokenError),
    syncedAt: nullableString(value.syncedAt),
  }
}

export function parsePlatforms(value: unknown): ShopPlatformOption[] {
  if (!isRecord(value) || !Array.isArray(value.items)) return []

  return value.items.flatMap((item) => {
    if (!isRecord(item) || typeof item.name !== 'string') return []
    return [
      {
        name: item.name,
        displayName: typeof item.displayName === 'string' ? item.displayName : item.name,
        icon: nullableString(item.icon),
        isProduction: item.isProduction !== false,
        shopCount: numberOr(item.shopCount, 0),
      },
    ]
  })
}

export function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && !(error instanceof TypeError) ? error.message : fallback
}

export async function problemDetail(response: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await response.clone().json()
    if (isRecord(body) && typeof body.detail === 'string' && body.detail) return body.detail
  } catch {
    // 非 ProblemDetails 响应使用兜底文案。
  }
  return fallback
}
