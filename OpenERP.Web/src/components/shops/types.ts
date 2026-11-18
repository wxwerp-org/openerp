export type Shop = {
  sid: string
  name: string
  platform: string
  region: string | null
  memberId: string | null
  isTokenOk: boolean | null
  tokenError: string | null
  syncedAt: string | null
}

export type ShopSummary = {
  valid: number
  invalid: number
  unknown: number
  lastSyncedAt: string | null
}

export type ShopPlatformOption = {
  name: string
  displayName: string
  icon: string | null
  isProduction: boolean
  shopCount: number
}

export type ShopStatusFilter = 'valid' | 'invalid' | 'unknown'
