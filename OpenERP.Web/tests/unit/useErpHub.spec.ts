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
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
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
