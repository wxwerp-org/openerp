export type OrderSyncStatus = 'running' | 'succeeded' | 'partial' | 'failed'
export type OrderSyncStage = 'queued' | 'shop' | 'pages' | 'details' | 'saving' | 'done'
export type OrderSyncTask = {
  epoch: string
  version: number
  id: string
  sid: number
  status: OrderSyncStatus
  stage: OrderSyncStage
  total: number
  read: number
  processed: number
  added: number
  updated: number
  unchanged: number
  skipped: number
  skipReasons: Record<string, number>
  error: string | null
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string'
}

export function isOrderSyncTask(value: unknown): value is OrderSyncTask {
  const countKeys = [
    'total',
    'read',
    'processed',
    'added',
    'updated',
    'unchanged',
    'skipped'
  ] as const
  const statuses: OrderSyncStatus[] = ['running', 'succeeded', 'partial', 'failed']
  const stages: OrderSyncStage[] = ['queued', 'shop', 'pages', 'details', 'saving', 'done']

  if (
    !isRecord(value) ||
    typeof value.id !== 'string' ||
    typeof value.epoch !== 'string' ||
    !Number.isSafeInteger(value.version) ||
    !Number.isInteger(value.sid) ||
    !statuses.includes(value.status as OrderSyncStatus) ||
    !stages.includes(value.stage as OrderSyncStage) ||
    !isRecord(value.skipReasons) ||
    !isNullableString(value.error) ||
    countKeys.some((key) => !Number.isInteger(value[key]) || (value[key] as number) < 0) ||
    Object.values(value.skipReasons).some(
      (count) => typeof count !== 'number' || !Number.isInteger(count) || count < 0
    )
  ) {
    return false
  }

  return true
}
