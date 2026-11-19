import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest } from '../../src/account/session'
import {
  useErpHub,
  type ShopSyncStartResult,
  type ShopSyncStatus,
} from '../../src/composables/useErpHub'

const hubMock = vi.hoisted(() => {
  const handlers = new Map<string, (value: unknown) => void>()
  const connection = {
    state: 'Disconnected',
    on: vi.fn((event: string, callback: (value: unknown) => void) => {
      handlers.set(event, callback)
    }),
    onreconnecting: vi.fn<(callback: () => void) => void>(),
    onreconnected: vi.fn<(callback: () => void) => void>(),
    onclose: vi.fn(),
    start: vi.fn(async () => {
      connection.state = 'Connected'
    }),
    stop: vi.fn(async () => {
      connection.state = 'Disconnected'
    }),
    invoke: vi.fn(),
  }
  const builder = {
    withUrl: vi.fn(() => builder),
    withAutomaticReconnect: vi.fn(() => builder),
    configureLogging: vi.fn(() => builder),
    build: vi.fn(() => connection),
  }
  return { connection, builder, handlers }
})

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(function () {
    return hubMock.builder
  }),
  HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected' },
  LogLevel: { Warning: 'Warning' },
}))

vi.mock('../../src/account/session', () => ({
  apiRequest: vi.fn(),
  currentAccessToken: () => 'access-token',
}))

function status(overrides: Partial<ShopSyncStatus> = {}): ShopSyncStatus {
  return {
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
    ...overrides,
  }
}

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((fulfill) => {
    resolve = fulfill
  })
  return { promise, resolve }
}

describe('ERP Hub 状态同步', () => {
  beforeEach(() => {
    hubMock.handlers.clear()
    hubMock.connection.state = 'Disconnected'
    hubMock.connection.invoke.mockReset()
    vi.mocked(apiRequest).mockReset()
  })

  it('Given 较新状态推送先到 When 刷新响应后到 Then 不覆盖较新状态', async () => {
    const pending = deferred<Response>()
    vi.mocked(apiRequest).mockReturnValueOnce(pending.promise)
    const hub = useErpHub()

    const connecting = hub.connect()
    await vi.waitFor(() => expect(apiRequest).toHaveBeenCalledTimes(1))
    hubMock.handlers.get('ShopSyncUpdated')?.(status({ completedAt: 'newer', version: 2 }))
    pending.resolve(jsonResponse(status({ running: true, completedAt: null, version: 1 })))
    await connecting

    expect(hub.status.value).toEqual(status({ completedAt: 'newer', version: 2 }))
  })

  it('Given 启动命令响应晚于完成广播 When 返回旧运行状态 Then 不覆盖完成状态', async () => {
    vi.mocked(apiRequest).mockResolvedValueOnce(jsonResponse(status({ version: 1 })))
    const hub = useErpHub()
    await hub.connect()

    const pending = deferred<ShopSyncStartResult>()
    hubMock.connection.invoke.mockReturnValueOnce(pending.promise)
    const starting = hub.startShopSync()
    hubMock.handlers.get('ShopSyncUpdated')?.(
      status({ completedAt: 'done', total: 1, checked: 1, version: 2 }),
    )
    pending.resolve({ accepted: true, status: status({ running: true, version: 1 }), error: null })
    await starting

    expect(hub.status.value).toEqual(
      status({ completedAt: 'done', total: 1, checked: 1, version: 2 }),
    )
  })

  it('Given 期间没有更新 When 刷新响应返回 Then 应用该状态', async () => {
    vi.mocked(apiRequest).mockResolvedValueOnce(
      jsonResponse(status({ completedAt: 'initial', version: 1 })),
    )
    const hub = useErpHub()

    await hub.connect()

    expect(hub.status.value).toEqual(status({ completedAt: 'initial', version: 1 }))
  })
})

describe('ERP Hub 订单同步', () => {
  beforeEach(() => {
    hubMock.handlers.clear()
    hubMock.connection.state = 'Disconnected'
    hubMock.connection.invoke.mockReset()
    vi.mocked(apiRequest).mockReset()
  })

  const orderTask = {
    id: 'task-1',
    sid: 42,
    status: 'running',
    stage: 'pages',
    version: 5,
    epoch: 'server-1',
    total: 10,
    read: 4,
    processed: 3,
    added: 1,
    updated: 1,
    unchanged: 1,
    skipped: 0,
    skipReasons: {},
    error: null,
  }

  it('Given 完成事件快于启动响应 When 旧响应与重复广播到达 Then 保留最新进度并仅通知一次持久化', async () => {
    const onOrdersChanged = vi.fn()
    const hub = useErpHub({ shopSync: false, onOrdersChanged })
    await hub.connect()
    const send = hubMock.handlers.get('OrderSyncUpdated')
    expect(send).toBeDefined()

    const pending = deferred<unknown>()
    hubMock.connection.invoke.mockReturnValueOnce(pending.promise)
    const starting = hub.startOrderSync(42)
    const completed = { ...orderTask, status: 'partial', stage: 'done', version: 6 }
    send?.(completed)
    send?.(completed)
    pending.resolve({ accepted: true, status: orderTask, error: null })
    await starting

    expect(hub.orderSyncTasks.value[42]).toEqual(completed)
    expect(onOrdersChanged).toHaveBeenCalledTimes(1)
    expect(hubMock.connection.invoke).toHaveBeenCalledWith('StartOrderSync', 42)
  })

  it('Given 重连期间收到更新 When 同进程的旧快照到达 Then 保留新状态', async () => {
    const hub = useErpHub({ shopSync: false })
    await hub.connect()
    const completed = { ...orderTask, status: 'succeeded', stage: 'done', version: 6 }
    hubMock.handlers.get('OrderSyncUpdated')?.(completed)
    hubMock.handlers.get('OrderSyncSnapshot')?.({ statuses: [orderTask], epoch: orderTask.epoch })

    expect(hub.orderSyncTasks.value[42]).toEqual(completed)
  })

  it('Given 旧进程曾有任务运行 When 重连收到新进程空快照 Then 清除旧任务并重新读取本地订单', async () => {
    const onOrdersChanged = vi.fn()
    const hub = useErpHub({ shopSync: false, onOrdersChanged })
    await hub.connect()
    hubMock.handlers.get('OrderSyncUpdated')?.(orderTask)

    expect(hub.orderSyncTasks.value[42]).toEqual(orderTask)
    hubMock.handlers.get('OrderSyncSnapshot')?.({ statuses: [], epoch: 'server-2' })

    expect(hub.orderSyncTasks.value).toEqual({})
    expect(onOrdersChanged).toHaveBeenCalledTimes(1)
  })

  it('Given 重连后返回新的部分成功快照 When 应用快照 Then 只刷新一次订单列表', async () => {
    const onOrdersChanged = vi.fn()
    const hub = useErpHub({ shopSync: false, onOrdersChanged })
    await hub.connect()
    hubMock.handlers.get('OrderSyncUpdated')?.(orderTask)

    const completed = { ...orderTask, status: 'partial', stage: 'done', version: 6 }
    const other = { ...completed, id: 'task-2', sid: 43, version: 7 }
    hubMock.handlers.get('OrderSyncSnapshot')?.({
      statuses: [completed, other],
      epoch: orderTask.epoch,
    })

    expect(hub.orderSyncTasks.value[42]).toEqual(completed)
    expect(hub.orderSyncTasks.value[43]).toEqual(other)
    expect(onOrdersChanged).toHaveBeenCalledTimes(1)
  })
  it('Given 掉线期间完成的任务已被下一任务覆盖 When 自动重连 Then 仍重新读取本地订单', async () => {
    const onOrdersChanged = vi.fn()
    const hub = useErpHub({ shopSync: false, onOrdersChanged })
    await hub.connect()
    hubMock.handlers.get('OrderSyncUpdated')?.(orderTask)
    expect(onOrdersChanged).not.toHaveBeenCalled()

    hubMock.connection.onreconnecting.mock.lastCall?.[0]()
    expect(hub.state.value).toBe('reconnecting')
    const nextTask = { ...orderTask, id: 'task-2', version: 9 }
    hubMock.handlers.get('OrderSyncSnapshot')?.({ statuses: [nextTask], epoch: nextTask.epoch })
    hubMock.connection.onreconnected.mock.lastCall?.[0]()

    expect(hub.state.value).toBe('connected')
    expect(hub.orderSyncTasks.value[42]).toEqual(nextTask)
    expect(onOrdersChanged).toHaveBeenCalledTimes(1)
  })
})
