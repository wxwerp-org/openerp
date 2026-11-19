import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'
import { ref } from 'vue'
import { apiRequest, currentAccessToken } from '../account/session'
import { isOrderSyncTask, type OrderSyncTask } from './orderSyncTask'

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
  version: number
}

export type ShopSyncStartResult = {
  accepted: boolean
  status: ShopSyncStatus
  error: string | null
}

type OrderSyncStartResult = {
  accepted: boolean
  status: OrderSyncTask | null
  error: string | null
}

type ErpHubOptions = {
  shopSync?: boolean
  onOrdersChanged?: () => void
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
  version: 0,
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
    (candidate.error === null || typeof candidate.error === 'string') &&
    typeof candidate.version === 'number'
  )
}

export function useErpHub({ shopSync = true, onOrdersChanged }: ErpHubOptions = {}) {
  const state = ref<ErpHubState>('idle')
  const status = ref<ShopSyncStatus | null>(null)
  const orderSyncTasks = ref<Record<number, OrderSyncTask>>({})
  const retiredEpochs = new Set<string>()
  let epoch: string | undefined
  let connection: HubConnection | null = null
  let starting: Promise<void> | null = null

  function apply(next: ShopSyncStatus): void {
    // 服务端版本是状态的唯一权威：只丢弃更旧的快照，避免旧 HTTP/invoke 响应覆盖新广播。
    if (status.value && next.version < status.value.version) return
    status.value = next
  }

  function updateOrders(nextEpoch: string, updates: OrderSyncTask[]): void {
    if (retiredEpochs.has(nextEpoch)) return
    let ordersChanged = false
    if (epoch !== undefined && epoch !== nextEpoch) {
      retiredEpochs.add(epoch)
      ordersChanged = true
      orderSyncTasks.value = {}
    }
    epoch = nextEpoch

    for (const task of updates) {
      const previous = orderSyncTasks.value[task.sid]
      if (previous && task.version <= previous.version) continue
      orderSyncTasks.value[task.sid] = task
      if (task.status === 'succeeded' || task.status === 'partial') ordersChanged = true
    }

    if (ordersChanged) onOrdersChanged?.()
  }

  async function refreshStatus(): Promise<void> {
    try {
      const response = await apiRequest('/api/erp/shop-sync')
      if (!response.ok) return
      const body: unknown = await response.json()
      if (isShopSyncStatus(body)) apply(body)
    } catch {
      // 同步状态读取失败不影响授权页浏览本地数据。
    }
  }

  async function connect(): Promise<void> {
    if (starting) return starting
    if (connection && connection.state !== HubConnectionState.Disconnected) return

    const retrying = state.value === 'disconnected'
    starting = (async () => {
      try {
        const hub = new HubConnectionBuilder()
          .withUrl(absoluteHubUrl(), { accessTokenFactory: () => currentAccessToken() ?? '' })
          .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
          .configureLogging(LogLevel.Warning)
          .build()

        hub.on(syncUpdatedEvent, (next: unknown) => {
          if (shopSync && connection === hub && isShopSyncStatus(next)) apply(next)
        })
        hub.on('OrderSyncUpdated', (next: unknown) => {
          if (!shopSync && connection === hub && isOrderSyncTask(next)) {
            updateOrders(next.epoch, [next])
          }
        })
        hub.on('OrderSyncSnapshot', (payload: unknown) => {
          if (shopSync || connection !== hub || !payload || typeof payload !== 'object') return
          const { epoch, statuses } = payload as { epoch?: unknown; statuses?: unknown }
          if (typeof epoch !== 'string' || !Array.isArray(statuses)) return
          updateOrders(
            epoch,
            statuses.filter(
              (task): task is OrderSyncTask => isOrderSyncTask(task) && task.epoch === epoch,
            ),
          )
        })
        hub.onreconnecting(() => {
          if (connection === hub) state.value = 'reconnecting'
        })
        hub.onreconnected(() => {
          if (connection !== hub) return
          state.value = 'connected'
          if (shopSync) void refreshStatus()
          else onOrdersChanged?.()
        })
        hub.onclose(() => {
          if (connection === hub) state.value = 'disconnected'
        })

        connection = hub
        state.value = 'connecting'
        await hub.start()
        if (connection !== hub) return
        state.value = 'connected'
        if (shopSync) await refreshStatus()
        else if (retrying) onOrdersChanged?.()
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

    try {
      const result = await connection.invoke<ShopSyncStartResult>(startSyncMethod)
      if (result?.status && isShopSyncStatus(result.status)) apply(result.status)
      return result
    } catch {
      return {
        accepted: false,
        status: status.value ?? emptyStatus,
        error: '无法启动同步，请稍后重试。',
      }
    }
  }

  async function startOrderSync(sid: number): Promise<OrderSyncStartResult> {
    const hub = connection
    if (!hub || hub.state !== HubConnectionState.Connected) {
      return { accepted: false, status: null, error: '实时连接已断开，请重新连接后重试。' }
    }

    const startError = { accepted: false, status: null, error: '无法启动订单同步，请稍后重试。' }
    try {
      const result = await hub.invoke<OrderSyncStartResult>('StartOrderSync', sid)
      if (connection !== hub || typeof result?.accepted !== 'boolean') return startError

      if (isOrderSyncTask(result.status) && result.status.sid === sid) {
        updateOrders(result.status.epoch, [result.status])
      }
      return result
    } catch {
      return startError
    }
  }

  return {
    state,
    status,
    orderSyncTasks,
    connect,
    disconnect,
    refreshStatus,
    startShopSync,
    startOrderSync,
  }
}
