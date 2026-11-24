/** 订单同步面板读取的接口响应：类型、格式校验与错误文案。 */

export type Shop = {
  sid: string
  name: string
  platform: string
  region: string | null
}

export type ShopsResponse = {
  items: Shop[]
  page: number
  pageSize: number
  totalCount: number
  pageCount: number
}

export type SyncSettings = {
  enabled: boolean
  shopSids: number[]
  intervalMinutes: number
}

export type SelectableShop = {
  sid: string
  name: string
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string'
}

export function parseConfig(value: unknown): {
  configured: boolean
  orderSyncEnabled: boolean
} {
  if (
    !isRecord(value) ||
    typeof value.configured !== 'boolean' ||
    typeof value.orderSyncEnabled !== 'boolean'
  ) {
    throw new Error('服务器配置响应格式无效。')
  }

  return {
    configured: value.configured,
    orderSyncEnabled: value.orderSyncEnabled,
  }
}

export function parseSyncSettings(value: unknown): SyncSettings {
  if (
    !isRecord(value) ||
    typeof value.enabled !== 'boolean' ||
    !Array.isArray(value.shopSids) ||
    !Number.isInteger(value.intervalMinutes) ||
    (value.intervalMinutes as number) <= 0 ||
    value.shopSids.some((sid) => !Number.isInteger(sid) || (sid as number) <= 0)
  ) {
    throw new Error('定时同步设置响应格式无效。')
  }

  return {
    enabled: value.enabled,
    shopSids: value.shopSids as number[],
    intervalMinutes: value.intervalMinutes as number,
  }
}

export function parseSyncShopSelection(value: unknown): SelectableShop[] {
  if (!isRecord(value) || !Array.isArray(value.items)) {
    throw new Error('可选店铺响应格式无效。')
  }

  return value.items.map((item): SelectableShop => {
    if (
      !isRecord(item) ||
      typeof item.sid !== 'string' ||
      !/^[1-9]\d*$/.test(item.sid) ||
      typeof item.name !== 'string'
    ) {
      throw new Error('可选店铺响应中包含格式无效的店铺。')
    }

    return { sid: item.sid, name: item.name }
  })
}

export function parseShopsResponse(value: unknown): ShopsResponse {
  if (
    !isRecord(value) ||
    !Array.isArray(value.items) ||
    !Number.isInteger(value.page) ||
    !Number.isInteger(value.pageSize) ||
    !Number.isInteger(value.totalCount) ||
    !Number.isInteger(value.pageCount)
  ) {
    throw new Error('店铺列表响应格式无效。')
  }

  const items = value.items.map((item): Shop => {
    if (
      !isRecord(item) ||
      typeof item.sid !== 'string' ||
      typeof item.name !== 'string' ||
      typeof item.platform !== 'string' ||
      !isNullableString(item.region)
    ) {
      throw new Error('店铺列表中包含格式无效的店铺信息。')
    }

    return {
      sid: item.sid,
      name: item.name,
      platform: item.platform,
      region: item.region,
    }
  })

  return {
    items,
    page: value.page as number,
    pageSize: value.pageSize as number,
    totalCount: value.totalCount as number,
    pageCount: value.pageCount as number,
  }
}

export async function httpErrorMessage(response: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await response.json()
    if (isRecord(body)) {
      const title = typeof body.title === 'string' ? body.title.trim() : ''
      const detail = typeof body.detail === 'string' ? body.detail.trim() : ''
      if (title && detail) return `${title}：${detail}`
      if (title || detail) return title || detail
    }
  } catch {
    // 错误响应没有可读取的 ProblemDetails 时，使用 HTTP 状态码提示。
  }

  return `${fallback}（HTTP ${response.status}）`
}
