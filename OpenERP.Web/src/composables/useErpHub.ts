import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'
import { ref, type Ref } from 'vue'
import { apiRequest, currentAccessToken } from '../account/session'

export type ShopSyncStatus = {
  running: boolean
  startedAt: string | null
  completedAt: string | null
  total: number
  checked: number
  valid: number
  invalid: number
  unknown: number
  error: string | null
}

export type ShopSyncStartResult = {
  accepted: boolean
  status: ShopSyncStatus
  error: string | null
}

export type ErpHubState = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

const hubPath = '/api/hubs/erp'
const syncUpdatedEvent = 'ShopSyncUpdated'
const startSyncMethod = 'StartShopSync'

function absoluteHubUrl(): string {
  if (typeof window !== 'undefined' && window.location?.origin)
    return `${window.location.origin}${hubPath}`
  return hubPath
}

const emptyStatus: ShopSyncStatus = {
  running: false,
  startedAt: null,
  completedAt: null,
  total: 0,
  checked: 0,
  valid: 0,
  invalid: 0,
  unknown: 0,
  error: null,
}

function isShopSyncStatus(value: unknown): value is ShopSyncStatus {
  if (!value || typeof value !== 'object') return false
  const candidate = value as Partial<ShopSyncStatus>
  return (
    typeof candidate.running === 'boolean' &&
    [
      candidate.total,
      candidate.checked,
      candidate.valid,
      candidate.invalid,
      candidate.unknown,
    ].every((count) => typeof count === 'number') &&
    (candidate.startedAt === null || typeof candidate.startedAt === 'string') &&
    (candidate.completedAt === null || typeof candidate.completedAt === 'string') &&
    (candidate.error === null || typeof candidate.error === 'string')
  )
}

export function useErpHub(): {
  state: Ref<ErpHubState>
  status: Ref<ShopSyncStatus | null>
  connect: () => Promise<void>
  disconnect: () => Promise<void>
  startShopSync: () => Promise<ShopSyncStartResult>
  onStatus: (listener: (status: ShopSyncStatus) => void) => () => void
} {
  const state = ref<ErpHubState>('idle')
  const status = ref<ShopSyncStatus | null>(null)
  let connection: HubConnection | null = null
  let starting: Promise<void> | null = null
  let listeners: Array<(status: ShopSyncStatus) => void> = []
  let statusRevision = 0

  function apply(next: ShopSyncStatus): void {
    statusRevision += 1
    status.value = next
    for (const listener of listeners) listener(next)
  }

  function onStatus(listener: (status: ShopSyncStatus) => void): () => void {
    listeners.push(listener)
    return () => {
      listeners = listeners.filter((item) => item !== listener)
    }
  }

  async function refreshStatus(): Promise<void> {
    const revision = statusRevision
    try {
      const response = await apiRequest('/api/erp/shop-sync')
      if (!response.ok) return
      const body: unknown = await response.json()
      if (isShopSyncStatus(body) && revision === statusRevision) apply(body)
    } catch {
      // 同步状态读取失败不影响授权页浏览本地数据。
    }
  }

  async function connect(): Promise<void> {
    if (connection && connection.state !== HubConnectionState.Disconnected) return
    if (starting) return starting

    starting = (async () => {
      try {
        const hub = new HubConnectionBuilder()
          .withUrl(absoluteHubUrl(), { accessTokenFactory: () => currentAccessToken() ?? '' })
          .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
          .configureLogging(LogLevel.Warning)
          .build()

        hub.on(syncUpdatedEvent, (next: unknown) => {
          if (isShopSyncStatus(next)) apply(next)
        })
        hub.onreconnecting(() => {
          state.value = 'reconnecting'
        })
        hub.onreconnected(() => {
          state.value = 'connected'
          void refreshStatus()
        })
        hub.onclose(() => {
          state.value = 'disconnected'
        })

        connection = hub
        state.value = 'connecting'
        await hub.start()
        state.value = 'connected'
        await refreshStatus()
      } catch {
        state.value = 'disconnected'
      } finally {
        starting = null
      }
    })()

    return starting
  }

  async function disconnect(): Promise<void> {
    const hub = connection
    connection = null
    starting = null
    if (hub) {
      try {
        await hub.stop()
      } catch {
        // 连接已经断开时停止失败可忽略。
      }
    }
    state.value = 'disconnected'
  }

  async function startShopSync(): Promise<ShopSyncStartResult> {
    if (!connection || connection.state !== HubConnectionState.Connected) {
      return {
        accepted: false,
        status: status.value ?? emptyStatus,
        error: '实时连接已断开，请刷新页面后重试。',
      }
    }

    const revision = statusRevision
    try {
      const result = await connection.invoke<ShopSyncStartResult>(startSyncMethod)
      if (result?.status && isShopSyncStatus(result.status) && revision === statusRevision)
        apply(result.status)
      return result
    } catch {
      return {
        accepted: false,
        status: status.value ?? emptyStatus,
        error: '无法启动同步，请稍后重试。',
      }
    }
  }

  return { state, status, connect, disconnect, startShopSync, onStatus }
}
