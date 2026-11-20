import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const refreshLifetimeMs = 24 * 60 * 60 * 1000
const accessLifetimeMs = 20 * 60 * 1000
const fixedAccountExpiry = new Date(Date.now() + refreshLifetimeMs).toISOString()

function account(expiresAt = fixedAccountExpiry) {
  return {
    id: '019b2b91-0f43-7891-b8a7-87a15139c721',
    role: 'Administrator',
    username: 'admin',
    name: '值班管理员',
    phone: null,
    email: null,
    lastLoginAt: '2026-11-16T12:00:00Z',
    expiresAt,
  }
}

function credentials(accessToken = 'access-1') {
  return {
    accessToken,
    accessTokenExpiresAt: new Date(Date.now() + accessLifetimeMs).toISOString(),
    account: account(),
  }
}

function deferred<T>() {
  let resolve: (value: T) => void = () => {
    throw new Error('尚未设置响应')
  }
  const promise = new Promise<T>((finish) => {
    resolve = finish
  })
  return { promise, resolve }
}

function requestsFrom(fetch: ReturnType<typeof vi.fn>) {
  return fetch.mock.calls.map(([url, init]) => ({
    url: String(url),
    init: init as RequestInit | undefined,
  }))
}

describe('短期访问令牌与持久刷新会话', () => {
  beforeEach(() => {
    vi.resetModules()
    localStorage.clear()
    sessionStorage.clear()
  })

  afterEach(() => {
    vi.clearAllTimers()
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('Given 浏览器重开且服务端仍有刷新 Cookie When 先查初始化状态再刷新 Then 取得账号与内存访问令牌且不读 me', async () => {
    const fetch = vi.fn(async (url: string) => {
      if (url === '/api/auth/status') return Response.json({ initialized: true })
      if (url === '/api/auth/csrf') return Response.json({ requestToken: 'csrf-1' })
      if (url === '/api/auth/refresh') return Response.json(credentials())
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetch)
    const session = await import('../../src/account/session')

    await session.checkSession()

    const requests = requestsFrom(fetch)
    expect(requests.map(({ url }) => url)).toEqual([
      '/api/auth/status',
      '/api/auth/csrf',
      '/api/auth/refresh',
    ])
    const refresh = requests.find(({ url }) => url === '/api/auth/refresh')
    expect(refresh?.init?.method).toBe('POST')
    expect(refresh?.init?.body).toBeUndefined()
    expect(new Headers(refresh?.init?.headers).get('X-CSRF-TOKEN')).toBe('csrf-1')
    expect(new Headers(refresh?.init?.headers).has('Authorization')).toBe(false)
    expect(session.account.value).toEqual(account())
    expect(session.sessionState.value).toBe('authenticated')
    expect(requests.some(({ url }) => url === '/api/auth/me')).toBe(false)
    expect(Object.keys(localStorage)).toHaveLength(0)
    expect(Object.keys(sessionStorage)).toHaveLength(0)
  })

  it('Given 登录成功 When 调用受保护接口 Then 认证请求不带 Bearer 且业务请求使用内存令牌', async () => {
    const fetch = vi.fn(async (url: string) => {
      if (url === '/api/auth/csrf') return Response.json({ requestToken: 'csrf-login' })
      if (url === '/api/auth/login') return Response.json(credentials('login-access'))
      if (url === '/api/orders') return Response.json({ message: 'ok' })
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetch)
    const session = await import('../../src/account/session')

    await session.login('admin', ' secret ')
    const response = await session.apiRequest('/api/orders')

    expect(response.status).toBe(200)
    const requests = requestsFrom(fetch)
    const login = requests.find(({ url }) => url === '/api/auth/login')
    const csrf = requests.find(({ url }) => url === '/api/auth/csrf')
    const business = requests.find(({ url }) => url === '/api/orders')
    expect(JSON.parse(String(login?.init?.body))).toEqual({
      username: 'admin',
      password: ' secret ',
    })
    expect(new Headers(login?.init?.headers).get('X-CSRF-TOKEN')).toBe('csrf-login')
    expect(new Headers(login?.init?.headers).has('Authorization')).toBe(false)
    expect(new Headers(csrf?.init?.headers).has('Authorization')).toBe(false)
    expect(new Headers(business?.init?.headers).get('Authorization')).toBe('Bearer login-access')
    expect(Object.keys(localStorage)).toHaveLength(0)
    expect(Object.keys(sessionStorage)).toHaveLength(0)
  })

  it('Given 已初始化 When 设置密码 Then 使用 CSRF 且不带 Bearer；登录后的改密只带 Bearer', async () => {
    let initialized = false
    const fetch = vi.fn(async (url: string) => {
      if (url === '/api/auth/status') return Response.json({ initialized })
      if (url === '/api/auth/csrf') return Response.json({ requestToken: 'csrf-write' })
      if (url === '/api/auth/initialize') {
        initialized = true
        return new Response(null, { status: 204 })
      }
      if (url === '/api/auth/login') return Response.json(credentials('change-access'))
      if (url === '/api/auth/change-password') return new Response(null, { status: 204 })
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetch)
    const session = await import('../../src/account/session')
    await session.checkSession()
    await session.initialize(' initial ')
    await session.login('admin', ' initial ')
    await session.changePassword({ oldPassword: ' initial ', newPassword: ' next ' })

    const requests = requestsFrom(fetch)
    const initialize = requests.find(({ url }) => url === '/api/auth/initialize')
    const changePassword = requests.find(({ url }) => url === '/api/auth/change-password')
    expect(initialize?.init?.method).toBe('POST')
    expect(new Headers(initialize?.init?.headers).get('X-CSRF-TOKEN')).toBe('csrf-write')
    expect(new Headers(initialize?.init?.headers).has('Authorization')).toBe(false)
    expect(new Headers(changePassword?.init?.headers).get('Authorization')).toBe(
      'Bearer change-access',
    )
    expect(new Headers(changePassword?.init?.headers).has('X-CSRF-TOKEN')).toBe(false)
    expect(session.sessionState.value).toBe('anonymous')
  })

  it('Given 受保护读取因访问令牌过期返回 401 When 刷新成功 Then 只重试一次并使用新令牌', async () => {
    let readAttempts = 0
    let refreshAttempts = 0
    const fetch = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/auth/csrf') return Response.json({ requestToken: 'csrf-read' })
      if (url === '/api/auth/login') return Response.json(credentials('old-access'))
      if (url === '/api/auth/refresh') {
        refreshAttempts++
        return Response.json(credentials('new-access'))
      }
      if (url === '/api/orders') {
        readAttempts++
        return new Headers(init?.headers).get('Authorization') === 'Bearer new-access'
          ? Response.json({ items: ['order-1'] })
          : new Response(null, { status: 401 })
      }
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetch)
    const session = await import('../../src/account/session')
    await session.login('admin', 'secret')

    const response = await session.apiRequest('/api/orders')

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({ items: ['order-1'] })
    expect(readAttempts).toBe(2)
    expect(refreshAttempts).toBe(1)
    expect(session.sessionState.value).toBe('authenticated')
  })

  it('Given 多个安全读取同时返回 401 When 刷新共享会话 Then 同页只刷新一次且各读取最多重试一次', async () => {
    let refreshAttempts = 0
    const fetch = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/auth/csrf') return Response.json({ requestToken: 'csrf-flight' })
      if (url === '/api/auth/login') return Response.json(credentials('old-access'))
      if (url === '/api/auth/refresh') {
        refreshAttempts++
        await Promise.resolve()
        return Response.json(credentials('new-access'))
      }
      if (url.startsWith('/api/private/')) {
        return new Headers(init?.headers).get('Authorization') === 'Bearer new-access'
          ? Response.json({ ok: true })
          : new Response(null, { status: 401 })
      }
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetch)
    const session = await import('../../src/account/session')
    await session.login('admin', 'secret')

    const responses = await Promise.all([
      session.apiRequest('/api/private/one'),
      session.apiRequest('/api/private/two'),
    ])

    expect(responses.map(({ status }) => status)).toEqual([200, 200])
    expect(refreshAttempts).toBe(1)
    expect(requestsFrom(fetch).filter(({ url }) => url.startsWith('/api/private/'))).toHaveLength(4)
  })

  it('Given 业务写入返回 401 When 刷新成功 Then 保留会话但不重放写入', async () => {
    let writes = 0
    let refreshes = 0
    const fetch = vi.fn(async (url: string) => {
      if (url === '/api/auth/csrf') return Response.json({ requestToken: 'csrf-write-401' })
      if (url === '/api/auth/login') return Response.json(credentials('old-access'))
      if (url === '/api/auth/refresh') {
        refreshes++
        return Response.json(credentials('new-access'))
      }
      if (url === '/api/orders') {
        writes++
        return new Response(null, { status: 401 })
      }
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetch)
    const session = await import('../../src/account/session')
    await session.login('admin', 'secret')

    const response = await session.apiRequest('/api/orders', { method: 'POST', body: '{}' })

    expect(response.status).toBe(401)
    expect(writes).toBe(1)
    expect(refreshes).toBe(1)
    expect(session.sessionState.value).toBe('authenticated')
  })

  it('Given 刷新暂时返回 503 When 后续聚焦时服务恢复 Then 保留账号并重新取得访问令牌', async () => {
    let refreshAttempts = 0
    let temporaryFailure = true
    const fetch = vi.fn(async (url: string) => {
      if (url === '/api/auth/csrf') return Response.json({ requestToken: 'csrf-recovery' })
      if (url === '/api/auth/login') return Response.json(credentials('old-access'))
      if (url === '/api/auth/refresh') {
        refreshAttempts++
        return temporaryFailure
          ? new Response(null, { status: 503 })
          : Response.json(credentials('recovered-access'))
      }
      if (url === '/api/private') return new Response(null, { status: 401 })
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetch)
    const session = await import('../../src/account/session')
    await session.login('admin', 'secret')

    const first = await session.apiRequest('/api/private')
    expect(first.status).toBe(401)
    expect(session.account.value?.username).toBe('admin')
    expect(session.sessionState.value).toBe('authenticated')

    temporaryFailure = false
    await session.checkSession()

    const refreshedRead = await session.apiRequest('/api/private')
    expect(refreshedRead.status).toBe(401)
    expect(refreshAttempts).toBe(2)
    expect(session.sessionState.value).toBe('authenticated')
  })

  it('Given 刷新明确返回 401 When 受保护接口要求刷新 Then 清空当前页会话', async () => {
    const fetch = vi.fn(async (url: string) => {
      if (url === '/api/auth/csrf') return Response.json({ requestToken: 'csrf-expired' })
      if (url === '/api/auth/login') return Response.json(credentials('old-access'))
      if (url === '/api/auth/refresh') return new Response(null, { status: 401 })
      if (url === '/api/private') return new Response(null, { status: 401 })
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetch)
    const session = await import('../../src/account/session')
    await session.login('admin', 'secret')

    await session.apiRequest('/api/private')

    expect(session.account.value).toBeNull()
    expect(session.sessionState.value).toBe('anonymous')
  })

  it.each(['login-first', 'refresh-first'] as const)(
    'Given 匿名页签的旧刷新与登录并发 When %s 先完成 Then 有效登录最终保持在线',
    async (completion) => {
      const refreshStarted = deferred<void>()
      const loginStarted = deferred<void>()
      const refreshResponse = deferred<Response>()
      const loginResponse = deferred<Response>()
      const fetch = vi.fn(async (url: string) => {
        if (url === '/api/auth/status') return Response.json({ initialized: true })
        if (url === '/api/auth/csrf') return Response.json({ requestToken: 'csrf-anonymous-race' })
        if (url === '/api/auth/refresh') {
          refreshStarted.resolve()
          return refreshResponse.promise
        }
        if (url === '/api/auth/login') {
          loginStarted.resolve()
          return loginResponse.promise
        }
        return new Response(null, { status: 404 })
      })
      vi.stubGlobal('fetch', fetch)
      const session = await import('../../src/account/session')
      const checking = session.checkSession()
      await refreshStarted.promise
      const login = session.login('admin', 'correct-secret')
      await loginStarted.promise

      if (completion === 'login-first') {
        loginResponse.resolve(Response.json(credentials('login-access')))
        await login
        refreshResponse.resolve(new Response(null, { status: 401 }))
      } else {
        refreshResponse.resolve(new Response(null, { status: 401 }))
        await checking
        loginResponse.resolve(Response.json(credentials('login-access')))
        await login
      }
      await checking

      expect(session.sessionState.value).toBe('authenticated')
      expect(session.account.value?.username).toBe('admin')
    },
  )

  it.each(['logout', 'password-change', 'new-login'] as const)(
    'Given 刷新正在返回 When %s 先完成 Then 迟到刷新不能覆盖最新页面状态',
    async (completion) => {
      const refreshStarted = deferred<void>()
      const refreshResponse = deferred<Response>()
      let loginCount = 0
      const fetch = vi.fn(async (url: string, init?: RequestInit) => {
        if (url === '/api/auth/csrf') return Response.json({ requestToken: 'csrf-race' })
        if (url === '/api/auth/login') {
          loginCount++
          return Response.json(credentials(loginCount === 1 ? 'old-access' : 'new-login-access'))
        }
        if (url === '/api/auth/refresh') {
          refreshStarted.resolve()
          return refreshResponse.promise
        }
        if (url === '/api/private') return new Response(null, { status: 401 })
        if (url === '/api/auth/logout' || url === '/api/auth/change-password') {
          return new Response(null, { status: 204 })
        }
        if (url === '/api/identity') {
          return Response.json({ authorization: new Headers(init?.headers).get('Authorization') })
        }
        return new Response(null, { status: 404 })
      })
      vi.stubGlobal('fetch', fetch)
      const session = await import('../../src/account/session')
      await session.login('admin', 'secret')
      const pendingRead = session.apiRequest('/api/private')
      await refreshStarted.promise

      if (completion === 'logout') await session.logout()
      if (completion === 'password-change') {
        await session.changePassword({ oldPassword: 'secret', newPassword: 'next-secret' })
      }
      if (completion === 'new-login') await session.login('admin', 'next-secret')

      refreshResponse.resolve(Response.json(credentials('stale-access')))
      await pendingRead

      expect(requestsFrom(fetch).filter(({ url }) => url === '/api/private')).toHaveLength(1)
      let identityResponse: unknown = null
      if (completion === 'new-login') {
        const identity = await session.apiRequest('/api/identity')
        identityResponse = await identity.json()
      }
      expect(identityResponse).toEqual(
        completion === 'new-login' ? { authorization: 'Bearer new-login-access' } : null,
      )
      expect(session.account.value === null).toBe(completion !== 'new-login')
      expect(session.sessionState.value).toBe(
        completion === 'new-login' ? 'authenticated' : 'anonymous',
      )
    },
  )

  it('Given 刷新响应与资料保存并发 When 新资料先返回 Then 迟到刷新只更新令牌且保留最新资料', async () => {
    const refreshStarted = deferred<void>()
    const refreshResponse = deferred<Response>()
    const fetch = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/auth/csrf') return Response.json({ requestToken: 'csrf-profile-race' })
      if (url === '/api/auth/login') return Response.json(credentials('old-access'))
      if (url === '/api/auth/refresh') {
        refreshStarted.resolve()
        return refreshResponse.promise
      }
      if (url === '/api/private') return new Response(null, { status: 401 })
      if (url === '/api/account/profile') {
        return Response.json({ ...account(), name: JSON.parse(String(init?.body)).name })
      }
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetch)
    const session = await import('../../src/account/session')
    await session.login('admin', 'secret')
    const pendingRead = session.apiRequest('/api/private')
    await refreshStarted.promise

    await session.saveProfile({ name: '新资料姓名', phone: '', email: '' })
    refreshResponse.resolve(Response.json(credentials('refreshed-access')))
    await pendingRead

    expect(session.account.value?.name).toBe('新资料姓名')
    expect(session.sessionState.value).toBe('authenticated')
  })

  it('Given 访问令牌已过五分钟 When 页面定时器触发 Then 主动刷新并继续每五分钟安排一次', async () => {
    vi.useFakeTimers()
    const fetch = vi.fn(async (url: string) => {
      if (url === '/api/auth/csrf') return Response.json({ requestToken: 'csrf-timer' })
      if (url === '/api/auth/login') return Response.json(credentials('access-1'))
      if (url === '/api/auth/refresh') return Response.json(credentials('access-2'))
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetch)
    const session = await import('../../src/account/session')
    await session.login('admin', 'secret')

    await vi.advanceTimersByTimeAsync(5 * 60 * 1000)

    expect(requestsFrom(fetch).filter(({ url }) => url === '/api/auth/refresh')).toHaveLength(1)
    expect(session.sessionState.value).toBe('authenticated')
    await vi.advanceTimersByTimeAsync(5 * 60 * 1000)
    expect(requestsFrom(fetch).filter(({ url }) => url === '/api/auth/refresh')).toHaveLength(2)
  })

  it('Given 页面后台停留超过五分钟 When 发起受保护请求 Then 先补刷并只发送新令牌', async () => {
    vi.useFakeTimers()
    const fetch = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/auth/csrf') return Response.json({ requestToken: 'csrf-request' })
      if (url === '/api/auth/login') return Response.json(credentials('access-before-background'))
      if (url === '/api/auth/refresh') return Response.json(credentials('access-after-background'))
      if (url === '/api/orders') {
        return new Headers(init?.headers).get('Authorization') === 'Bearer access-after-background'
          ? Response.json({ message: 'ok' })
          : new Response(null, { status: 401 })
      }
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetch)
    const session = await import('../../src/account/session')
    await session.login('admin', 'secret')
    const refreshDeadline = performance.now() + 6 * 60 * 1000
    vi.spyOn(performance, 'now').mockReturnValue(refreshDeadline)

    const response = await session.apiRequest('/api/orders')

    expect(response.status).toBe(200)
    const requests = requestsFrom(fetch)
    expect(requests.filter(({ url }) => url === '/api/auth/refresh')).toHaveLength(1)
    expect(requests.filter(({ url }) => url === '/api/orders')).toHaveLength(1)
    expect(
      new Headers(requests.find(({ url }) => url === '/api/orders')?.init?.headers).get(
        'Authorization',
      ),
    ).toBe('Bearer access-after-background')
  })
})
