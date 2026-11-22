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
  /** 被拒绝（例如非管理员）时服务端不返回状态。 */
  status: ShopSyncStatus | null
  error: string | null
}

type OrderSyncStartResult = {
  accepted: boolean
  status: OrderSyncTask | null
  error: string | null
}

type OrderSyncBatchStartResult = {
  accepted: boolean
  startedCount: number
  busyCount: number
  error: string | null
}

type ErpHubOptions = {
  /** 订阅目录同步状态与事件（仅管理员可用）。 */
  shopSync?: boolean
  /** 订阅订单同步任务与事件；默认与 shopSync 相反，二者可同时开启。 */
  orderSync?: boolean
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

export function useErpHub({ shopSync = true, orderSync, onOrdersChanged }: ErpHubOptions = {}) {
  const wantsShop = shopSync
  const wantsOrder = orderSync ?? !shopSync
  const state = ref<ErpHubState>('idle')
  const status = ref<ShopSyncStatus | null>(null)
  const orderSyncTasks = ref<Record<number, OrderSyncTask>>({})
  const retiredEpochs = new Set<string>()
  let epoch: string | undefined
  let orderVisibilityVersion = 0
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
    let tasks = orderSyncTasks.value
    if (epoch !== undefined && epoch !== nextEpoch) {
      retiredEpochs.add(epoch)
      ordersChanged = true
      tasks = {}
    }
    epoch = nextEpoch

    // 整体替换对象：调用方的浅层 watch 才能看到任务新增与状态变化。
    // 服务端快照与事件都按 version 合并，晚到的旧快照不会覆盖更新的状态。
    const merged = { ...tasks }
    let changed = tasks !== orderSyncTasks.value
    for (const task of updates) {
      const previous = merged[task.sid]
      if (previous && task.version <= previous.version) continue
      merged[task.sid] = task
      changed = true
      if (task.status === 'succeeded' || task.status === 'partial') ordersChanged = true
    }
    if (changed) orderSyncTasks.value = merged

    if (ordersChanged) onOrdersChanged?.()
  }

  function clearOrderTasks(): void {
    if (!wantsOrder) return
    orderVisibilityVersion += 1
    // 重连时服务端重新解析店铺归属。同进程内也可能有店铺不再可见。
    orderSyncTasks.value = {}
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
          if (wantsShop && connection === hub && isShopSyncStatus(next)) apply(next)
        })
        hub.on('OrderSyncUpdated', (next: unknown) => {
          if (wantsOrder && connection === hub && isOrderSyncTask(next)) {
            updateOrders(next.epoch, [next])
          }
        })
        hub.on('OrderSyncSnapshot', (payload: unknown) => {
          if (!wantsOrder || connection !== hub || !payload || typeof payload !== 'object') return
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
          if (connection !== hub) return
          clearOrderTasks()
          state.value = 'reconnecting'
        })
        hub.onreconnected(() => {
          if (connection !== hub) return
          state.value = 'connected'
          if (wantsShop) void refreshStatus()
          onOrdersChanged?.()
        })
        hub.onclose(() => {
          if (connection !== hub) return
          clearOrderTasks()
          state.value = 'disconnected'
        })

        connection = hub
        state.value = 'connecting'
        await hub.start()
        if (connection !== hub) {
          // 连接建立期间已被卸载/重连：停掉这条迟到的连接，避免泄漏。
          void hub.stop().catch(() => undefined)
          return
        }
        state.value = 'connected'
        if (wantsShop) await refreshStatus()
        if (wantsOrder && retrying) onOrdersChanged?.()
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
    clearOrderTasks()
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
    const visibilityVersion = orderVisibilityVersion
    try {
      const result = await hub.invoke<OrderSyncStartResult>('StartOrderSync', sid)
      if (
        connection !== hub ||
        visibilityVersion !== orderVisibilityVersion ||
        typeof result?.accepted !== 'boolean'
      )
        return startError

      if (isOrderSyncTask(result.status) && result.status.sid === sid) {
        updateOrders(result.status.epoch, [result.status])
      }
      return result
    } catch {
      return startError
    }
  }

  async function startAllOrderSync(): Promise<OrderSyncBatchStartResult> {
    const hub = connection
    const rejected = { accepted: false, startedCount: 0, busyCount: 0 }
    if (!hub || hub.state !== HubConnectionState.Connected) {
      return { ...rejected, error: '实时连接已断开，请重新连接后重试。' }
    }

    try {
      return await hub.invoke<OrderSyncBatchStartResult>('StartAllOrderSync')
    } catch {
      return { ...rejected, error: '未收到全部同步的启动结果，请查看各店状态后重试。' }
    }
  }

  /** 手动刷新重建连接：在线漏送终态时靠服务端快照恢复（本轮唯一恢复入口）。 */
  async function reconnect(): Promise<void> {
    await disconnect()
    await connect()
  }

  return {
    state,
    status,
    orderSyncTasks,
    connect,
    disconnect,
    reconnect,
    refreshStatus,
    startShopSync,
    startOrderSync,
    startAllOrderSync,
  }
}
