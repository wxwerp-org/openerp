export type ShopRow = {
  sid: string
  name: string
  platform: string
  region: string | null
  isTokenOk: boolean | null
  tokenError: string | null
  syncedAt: string | null
}

export type ShopDirectorySummary = {
  valid: number
  invalid: number
  unknown: number
  lastSyncedAt: string | null
  syncVersion: number
}

export type ShopDirectory = {
  items: ShopRow[]
  /** 员工投影没有汇总；只有管理员响应才有。 */
  summary: ShopDirectorySummary | null
}

export type ErpAvailability = {
  configured: boolean
  orderSyncEnabled: boolean
}

export type ErpConfigSummary = {
  configured: boolean
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string'
}

function isCount(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0
}

/** 响应包含当前账号可见的店铺状态，全局授权汇总仅管理员可读。 */
export function parseShopDirectory(value: unknown): ShopDirectory {
  if (!isRecord(value) || !Array.isArray(value.items)) {
    throw new Error('店铺目录数据格式无效，请刷新后重试。')
  }
  const summary = isRecord(value.summary) ? parseShopSummary(value.summary) : null
  if (isRecord(value.summary) && summary === null) {
    throw new Error('店铺目录数据格式无效，请刷新后重试。')
  }

  const items = value.items.map((item): ShopRow => {
    if (!isRecord(item) || typeof item.sid !== 'string' || typeof item.platform !== 'string') {
      throw new Error('店铺目录中包含格式无效的店铺。')
    }
    return {
      sid: item.sid,
      name: typeof item.name === 'string' ? item.name : `店铺 ${item.sid}`,
      platform: item.platform,
      region: isNullableString(item.region) ? item.region : null,
      isTokenOk: typeof item.isTokenOk === 'boolean' ? item.isTokenOk : null,
      tokenError: isNullableString(item.tokenError) ? item.tokenError : null,
      syncedAt: isNullableString(item.syncedAt) ? item.syncedAt : null,
    }
  })

  return { items, summary }
}

function parseShopSummary(value: Record<string, unknown>): ShopDirectorySummary | null {
  if (
    !isCount(value.valid) ||
    !isCount(value.invalid) ||
    !isCount(value.unknown) ||
    typeof value.syncVersion !== 'number'
  ) {
    return null
  }
  return {
    valid: value.valid,
    invalid: value.invalid,
    unknown: value.unknown,
    lastSyncedAt: isNullableString(value.lastSyncedAt) ? value.lastSyncedAt : null,
    syncVersion: value.syncVersion,
  }
}

export function parseErpAvailability(value: unknown): ErpAvailability {
  if (
    !isRecord(value) ||
    typeof value.configured !== 'boolean' ||
    typeof value.orderSyncEnabled !== 'boolean'
  ) {
    throw new Error('ERP 接入状态格式无效。')
  }
  return { configured: value.configured, orderSyncEnabled: value.orderSyncEnabled }
}

export function parseErpConfig(value: unknown): ErpConfigSummary {
  if (!isRecord(value) || typeof value.configured !== 'boolean') {
    throw new Error('ERP 配置格式无效。')
  }
  return { configured: value.configured }
}

/** 本地时区的 `YYYY-MM-DD HH:mm`；无值显示破折号。 */
export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  const pad = (input: number) => String(input).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

export function formatCount(value: number | null | undefined): string {
  return typeof value === 'number' ? value.toLocaleString('zh-CN') : '—'
}

/** 完整店铺键 → 真实店名；目录缺失时回退「店铺 <SID>」。 */
export function shopNameMap(directory: ShopDirectory | null): Map<string, string> {
  const map = new Map<string, string>()
  if (!directory) return map
  for (const shop of directory.items) {
    map.set(`${shop.platform}/${shop.sid}`, shop.name)
  }
  return map
}

/** 同步任务只带 SID，按 SID 建索引用于同步卡展示店名。 */
export function shopNameBySid(directory: ShopDirectory | null): Map<string, string> {
  const map = new Map<string, string>()
  if (!directory) return map
  for (const shop of directory.items) {
    map.set(shop.sid, shop.name)
  }
  return map
}

export const syncStageLabels: Record<string, string> = {
  queued: '排队中',
  shop: '读取店铺',
  pages: '拉取订单',
  details: '读取详情',
  saving: '保存订单',
  done: '完成',
}

export const syncStatusLabels: Record<string, string> = {
  running: '同步中',
  succeeded: '同步成功',
  partial: '部分成功',
  failed: '同步失败',
}

export function stageLabel(stage: string): string {
  return syncStageLabels[stage] ?? stage
}

export function skipReasonLabel(reason: string): string {
  const labels: Record<string, string> = {
    unsupported: '平台不支持',
    'missing-shop': '缺少店铺',
    'missing-number': '缺少订单号',
    'not-temuppop': '非 TemuPop 订单',
  }
  return labels[reason] ?? reason
}

/** 报表平台聚合，按订单数降序，同值按平台名升序。 */
export function platformTotals(
  groups: { shop: { platform: string }; orderCount: number }[],
): { platform: string; orderCount: number }[] {
  const totals = new Map<string, number>()
  for (const group of groups) {
    totals.set(group.shop.platform, (totals.get(group.shop.platform) ?? 0) + group.orderCount)
  }
  return [...totals.entries()]
    .map(([platform, orderCount]) => ({ platform, orderCount }))
    .sort(
      (left, right) =>
        right.orderCount - left.orderCount || left.platform.localeCompare(right.platform),
    )
}
