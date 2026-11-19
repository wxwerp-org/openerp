import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { adminAccount as admin } from './adminAccount'
import { installNativeDialogShim } from './installNativeDialogShim'

beforeAll(installNativeDialogShim)

let wrapper: VueWrapper | undefined

beforeEach(() => vi.resetModules())
afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

async function mountAt(path: string) {
  history.replaceState(null, '', path)
  const { default: router } = await import('../../src/router')
  const { default: App } = await import('../../src/App.vue')
  wrapper = mount(App, { attachTo: document.body, global: { plugins: [router] } })
  await flushPromises()
  return { app: wrapper, router }
}

function mockInitializedFetch(
  fetch: (url: string, request?: RequestInit) => Promise<Response>,
): void {
  vi.stubGlobal('fetch', (url: string, request?: RequestInit) =>
    url === '/api/auth/status'
      ? Promise.resolve(Response.json({ initialized: true }))
      : fetch(url, request),
  )
}

function sessionResponse(value = admin, accessToken = 'unit-access'): Response {
  return Response.json({
    accessToken,
    accessTokenExpiresAt: new Date(Date.now() + 20 * 60 * 1000).toISOString(),
    account: value,
  })
}

describe('真实会话路由', () => {
  it('Given 首次直达受保护页且会话仍在验证 When 会话确认无效 Then 始终隐藏业务界面并保留目标地址', async () => {
    let resolve: (response: Response) => void = () => {
      throw new Error('未发起会话请求')
    }
    mockInitializedFetch((url: string) =>
      url === '/api/auth/csrf'
        ? Promise.resolve(Response.json({ requestToken: 'route-token' }))
        : new Promise<Response>((finish) => {
            resolve = finish
          }),
    )
    const { app, router } = await mountAt('/users?from=notice#main')

    expect(app.find('[data-testid="sidebar"]').exists()).toBe(false)
    resolve(new Response(null, { status: 401 }))
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/login')
    expect(router.currentRoute.value.query.returnTo).toBe('/users?from=notice#main')
    expect(app.get('h1').text()).toBe('登录 OpenERP')
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(false)
  })

  it('Given 会话服务暂时不可用 When 检查返回 503 后重试成功 Then 保留当前地址并进入工作台', async () => {
    let refreshAttempts = 0
    const fetch = vi.fn((url: string) => {
      if (url === '/api/auth/csrf')
        return Promise.resolve(Response.json({ requestToken: 'retry-token' }))
      if (url === '/api/auth/refresh') {
        refreshAttempts++
        return Promise.resolve(
          refreshAttempts === 1 ? new Response(null, { status: 503 }) : sessionResponse(),
        )
      }
      return Promise.resolve(new Response(null, { status: 404 }))
    })
    mockInitializedFetch(fetch)
    const { app, router } = await mountAt('/users')

    expect(router.currentRoute.value.path).toBe('/users')
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(false)
    await app.get('button').trigger('click')
    await flushPromises()

    expect(app.get('[data-testid="account-button"]').text()).toContain('值班管理员')
    expect(router.currentRoute.value.path).toBe('/users')
  })

  it('Given 登录页带外站返回地址 When 凭证错误后用正确凭证重试 Then 保留登录反馈并安全进入首页', async () => {
    let csrfCount = 0
    const fetch = vi.fn((url: string, request?: RequestInit) => {
      if (url === '/api/auth/csrf')
        return Promise.resolve(Response.json({ requestToken: `csrf-${++csrfCount}` }))
      if (url === '/api/auth/refresh') return Promise.resolve(new Response(null, { status: 401 }))
      const credentials = JSON.parse(String(request?.body))
      if (credentials.password !== ' valid ') {
        return Promise.resolve(Response.json({ detail: '用户名或密码错误' }, { status: 401 }))
      }
      const token = new Headers(request?.headers).get('X-CSRF-TOKEN')
      return Promise.resolve(token ? sessionResponse() : new Response(null, { status: 400 }))
    })
    mockInitializedFetch(fetch)
    const { app, router } = await mountAt('/login?returnTo=%2F%2Fexample.test')

    await app.get('input[autocomplete="username"]').setValue('admin')
    await app.get('input[autocomplete="current-password"]').setValue('wrong')
    await app.get('form').trigger('submit')
    await flushPromises()

    expect(app.get('[role="alert"]').text()).toBe('用户名或密码错误')
    expect(router.currentRoute.value.path).toBe('/login')

    await app.get('input[autocomplete="current-password"]').setValue(' valid ')
    await app.get('form').trigger('submit')
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/')
    expect(app.get('[data-testid="account-button"]').text()).toContain('值班管理员')
    expect(csrfCount).toBeGreaterThan(1)
  })

  it('Given 已登录 When 刷新暂时失败后业务接口令牌明确失效 Then 保留工作台直至刷新确认会话失效', async () => {
    let refreshStatus = 200
    mockInitializedFetch((url: string) => {
      if (url === '/api/auth/csrf')
        return Promise.resolve(Response.json({ requestToken: 'session-check-token' }))
      if (url === '/api/auth/refresh') {
        return Promise.resolve(
          refreshStatus === 200 ? sessionResponse() : new Response(null, { status: refreshStatus }),
        )
      }
      return Promise.resolve(new Response(null, { status: 401 }))
    })
    const { app, router } = await mountAt('/')
    expect(app.get('[data-testid="account-button"]').text()).toContain('值班管理员')

    refreshStatus = 503
    window.dispatchEvent(new Event('focus'))
    await flushPromises()
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(true)

    refreshStatus = 401
    await router.push('/service-connection')
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/login')
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(false)
  })

  it('Given 浏览器时钟越过固定期限 When 后端刷新确认 Cookie 已失效 Then 才回到登录页', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'] })
    let refreshCount = 0
    mockInitializedFetch((url: string) => {
      if (url === '/api/auth/csrf')
        return Promise.resolve(Response.json({ requestToken: 'expiry-token' }))
      if (url === '/api/auth/refresh') {
        refreshCount++
        return Promise.resolve(
          refreshCount === 1 ? sessionResponse() : new Response(null, { status: 401 }),
        )
      }
      return Promise.resolve(new Response(null, { status: 404 }))
    })
    const { app, router } = await mountAt('/')
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(true)

    vi.setSystemTime(Date.now() + 25 * 60 * 60 * 1000)
    await flushPromises()
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(true)

    window.dispatchEvent(new Event('focus'))
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/login')
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(false)
  })

  it.each(['focus', 'online', 'visibilitychange'] as const)(
    'Given 页面恢复活动且五分钟刷新点已过 When 收到 %s Then 重新验证并刷新访问令牌',
    async (eventName) => {
      vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'] })
      let refreshCount = 0
      mockInitializedFetch((url: string) => {
        if (url === '/api/auth/csrf')
          return Promise.resolve(Response.json({ requestToken: 'activity-token' }))
        if (url === '/api/auth/refresh') {
          refreshCount++
          return Promise.resolve(sessionResponse())
        }
        return Promise.resolve(new Response(null, { status: 404 }))
      })
      const { app } = await mountAt('/users')
      expect(refreshCount).toBe(1)

      const refreshDeadline = performance.now() + 6 * 60 * 1000
      vi.spyOn(performance, 'now').mockReturnValue(refreshDeadline)
      if (eventName === 'visibilitychange') {
        Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
        document.dispatchEvent(new Event(eventName))
      } else {
        window.dispatchEvent(new Event(eventName))
      }
      await flushPromises()

      expect(refreshCount).toBe(2)
      expect(app.get('[data-testid="account-button"]').text()).toContain(admin.name)
    },
  )
})

async function openAccountDialog(app: VueWrapper, action: 'profile' | 'password'): Promise<void> {
  await app.get('[data-testid="account-button"]').trigger('click')
  await app.get(`[data-account-action="${action}"]`).trigger('click')
}

describe('真实账号操作', () => {
  it('Given 从会话加载真实资料 When 取消编辑和保存失败后重试 Then 仅成功保存时关闭并刷新姓名', async () => {
    let saves = 0
    mockInitializedFetch((url: string, request?: RequestInit) => {
      if (url === '/api/auth/csrf')
        return Promise.resolve(Response.json({ requestToken: 'account-token' }))
      if (url === '/api/auth/refresh') return Promise.resolve(sessionResponse())
      const fields = JSON.parse(String(request?.body))
      const token = new Headers(request?.headers).get('Authorization')
      if (
        url !== '/api/account/profile' ||
        request?.method !== 'PUT' ||
        token !== 'Bearer unit-access' ||
        fields.name !== '仓库管理员'
      ) {
        return Promise.resolve(new Response(null, { status: 400 }))
      }
      saves++
      return Promise.resolve(
        saves === 1
          ? Response.json({ detail: '暂时无法保存' }, { status: 503 })
          : Response.json({ ...admin, ...fields }),
      )
    })
    const { app } = await mountAt('/')

    await openAccountDialog(app, 'profile')
    expect(app.get('#profileName').element).toHaveProperty('value', admin.name)
    expect(app.get('#profileUsername').element).toHaveProperty('readOnly', true)
    expect(app.get('time').attributes('datetime')).toBe(admin.lastLoginAt)
    expect(app.find('#profileDepartment').exists()).toBe(false)
    await app.get('#profileName').setValue('取消修改')
    await app.get('#cancelProfile').trigger('click')
    expect(saves).toBe(0)

    await openAccountDialog(app, 'profile')
    expect(app.get('#profileName').element).toHaveProperty('value', admin.name)
    await app.get('#profileName').setValue('仓库管理员')
    await app.get('#saveProfile').trigger('click')
    await flushPromises()
    expect(app.get('#profileLayer').attributes('open')).toBeDefined()
    expect(app.get('#profileLayer').text()).toContain('暂时无法保存')
    expect(app.get('[data-testid="account-button"]').text()).toContain(admin.name)

    await app.get('#saveProfile').trigger('click')
    await flushPromises()
    expect(app.get('#profileLayer').attributes('open')).toBeUndefined()
    expect(app.get('[data-testid="account-button"]').text()).toContain('仓库管理员')
  })

  it('Given 资料弹窗 When 手机号超长或邮箱含空格 Then 标明首个无效字段且保留表单', async () => {
    let saves = 0
    mockInitializedFetch((url: string) => {
      if (url === '/api/auth/csrf')
        return Promise.resolve(Response.json({ requestToken: 'account-token' }))
      if (url === '/api/auth/refresh') return Promise.resolve(sessionResponse())
      saves++
      return Promise.resolve(Response.json(admin))
    })
    const { app } = await mountAt('/')
    await openAccountDialog(app, 'profile')

    await app.get('#profilePhone').setValue('012345678901234567890')
    await app.get('#profileEmail').setValue('a b@example.com')
    await app.get('#profileLayer form').trigger('submit')
    await flushPromises()
    expect(app.get('#profilePhone').attributes('aria-invalid')).toBe('true')
    expect(document.activeElement?.id).toBe('profilePhone')
    expect(saves).toBe(0)

    await app.get('#profilePhone').setValue('')
    await app.get('#profileLayer form').trigger('submit')
    await flushPromises()
    expect(app.get('#profileEmail').attributes('aria-invalid')).toBe('true')
    expect(document.activeElement?.id).toBe('profileEmail')
    expect(saves).toBe(0)

    await app.get('#profileEmail').setValue('erp+team@example')
    await app.get('#profileLayer form').trigger('submit')
    await flushPromises()
    expect(app.get('#profileLayer').attributes('open')).toBeUndefined()
    expect(saves).toBe(1)
  })

  it.each([
    { label: '重新聚焦并校验会话', refocus: true },
    { label: '第一次写请求被拒绝后重试', refocus: false },
  ])('Given 另一页签更换刷新会话 When $label Then 取得新内存令牌并保存', async ({ refocus }) => {
    let sessionNumber = 1
    let attempts = 0
    let savedName = admin.name
    mockInitializedFetch((url: string, request?: RequestInit) => {
      const session = {
        ...admin,
        name: savedName,
        expiresAt: `2099-01-0${sessionNumber}T00:00:00Z`,
      }
      const token = `session-${sessionNumber}`
      if (url === '/api/auth/csrf') return Promise.resolve(Response.json({ requestToken: token }))
      if (url === '/api/auth/refresh')
        return Promise.resolve(sessionResponse(session, `access-${sessionNumber}`))
      if (url === '/api/account/profile') {
        attempts++
        if (
          new Headers(request?.headers).get('Authorization') !== `Bearer access-${sessionNumber}`
        ) {
          return Promise.resolve(new Response(null, { status: 401 }))
        }
        savedName = JSON.parse(String(request?.body)).name
        return Promise.resolve(Response.json({ ...session, name: savedName }))
      }
      return Promise.resolve(new Response(null, { status: 404 }))
    })
    const { app } = await mountAt('/')
    await openAccountDialog(app, 'profile')
    await app.get('#profileName').setValue('第一会话')
    await app.get('#saveProfile').trigger('click')
    await flushPromises()
    expect(app.get('[data-testid="account-button"]').text()).toContain('第一会话')

    sessionNumber = 2
    if (refocus) {
      window.dispatchEvent(new Event('focus'))
      await flushPromises()
    }

    await openAccountDialog(app, 'profile')
    await app.get('#profileName').setValue('第二会话')
    await app.get('#saveProfile').trigger('click')
    await flushPromises()
    expect(app.find('#profileLayer .account-error').exists()).toBe(!refocus)
    expect(app.get('[data-testid="account-button"]').text()).toContain(
      refocus ? '第二会话' : '第一会话',
    )
    expect(attempts).toBe(2)
    if (!refocus) {
      await app.get('#saveProfile').trigger('click')
      await flushPromises()
    }

    expect(app.get('[data-testid="account-button"]').text()).toContain('第二会话')
    expect(app.get('#profileLayer').attributes('open')).toBeUndefined()
  })

  it('Given 已登录 When 原密码错误后用正确原密码提交 Then 先保留弹窗再回到登录页', async () => {
    mockInitializedFetch((url: string, request?: RequestInit) => {
      if (url === '/api/auth/csrf')
        return Promise.resolve(Response.json({ requestToken: 'password-token' }))
      if (url === '/api/auth/refresh') return Promise.resolve(sessionResponse())
      const { oldPassword, newPassword } = JSON.parse(String(request?.body))
      const token = new Headers(request?.headers).get('Authorization')
      if (
        url === '/api/auth/change-password' &&
        token === 'Bearer unit-access' &&
        oldPassword === ' previous ' &&
        newPassword === ' newpwd '
      ) {
        return Promise.resolve(new Response(null, { status: 204 }))
      }
      return Promise.resolve(Response.json({ detail: '原密码错误' }, { status: 400 }))
    })
    const { app, router } = await mountAt('/')

    await openAccountDialog(app, 'password')
    await app.get('#oldPassword').setValue('wrong')
    await app.get('#newPassword').setValue(' newpwd ')
    await app.get('#confirmPassword').setValue(' newpwd ')
    await app.get('#savePassword').trigger('click')
    await flushPromises()
    expect(app.get('#passwordLayer').attributes('open')).toBeDefined()
    expect(app.get('#passwordLayer').text()).toContain('原密码错误')

    await app.get('#oldPassword').setValue(' previous ')
    await app.get('#savePassword').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/login')
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(false)
  })

  it('Given 已登录 When 退出请求失败后重试成功 Then 仅成功退出时离开工作台', async () => {
    let failed = true
    mockInitializedFetch((url: string, request?: RequestInit) => {
      if (url === '/api/auth/refresh') return Promise.resolve(sessionResponse())
      if (url === '/api/auth/csrf')
        return Promise.resolve(Response.json({ requestToken: 'logout-token' }))
      const token = new Headers(request?.headers).get('X-CSRF-TOKEN')
      if (
        url === '/api/auth/logout' &&
        request?.method === 'POST' &&
        request.body === undefined &&
        token === 'logout-token' &&
        !new Headers(request.headers).has('Authorization')
      ) {
        return Promise.resolve(
          failed
            ? Response.json({ detail: '暂时无法退出' }, { status: 503 })
            : new Response(null, { status: 204 }),
        )
      }
      return Promise.resolve(new Response(null, { status: 400 }))
    })
    const { app, router } = await mountAt('/')

    await app.get('[data-testid="account-button"]').trigger('click')
    await app.get('[data-account-action="logout"]').trigger('click')
    await flushPromises()
    expect(app.get('[data-testid="toast"]').text()).toBe('暂时无法退出')
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(true)

    failed = false
    await app.get('[data-testid="account-button"]').trigger('click')
    await app.get('[data-account-action="logout"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/login')
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(false)
  })
})

type InitializationService = {
  initialized?: boolean
  statusUnavailable?: boolean
  signedIn?: boolean
  submit?: () => Promise<Response>
}

function mockInitializationService(service: InitializationService) {
  const submissions: unknown[] = []
  const statusRequests: Array<RequestInit | undefined> = []
  vi.stubGlobal('fetch', async (url: string, request?: RequestInit) => {
    if (url === '/api/auth/status') {
      statusRequests.push(request)
      return service.statusUnavailable
        ? new Response(null, { status: 503 })
        : Response.json({ initialized: service.initialized ?? false })
    }
    if (url === '/api/auth/refresh')
      return service.signedIn ? sessionResponse() : new Response(null, { status: 401 })
    if (url === '/api/auth/csrf') return Response.json({ requestToken: 'initialization-token' })
    if (url === '/api/auth/initialize' && request?.method === 'POST') {
      if (new Headers(request.headers).get('X-CSRF-TOKEN') !== 'initialization-token') {
        return new Response(null, { status: 400 })
      }
      submissions.push(JSON.parse(String(request.body)))
      if (service.submit) return service.submit()
      service.initialized = true
      return new Response(null, { status: 204 })
    }
    if (url === '/api/auth/login' && service.initialized) return sessionResponse()
    return new Response(null, { status: 404 })
  })
  return { submissions, statusRequests }
}

async function fillInitialization(
  app: VueWrapper,
  password: string,
  confirmation = password,
): Promise<void> {
  await app.get('#newPassword').setValue(password)
  await app.get('#confirmPassword').setValue(confirmation)
}

function deferResponse() {
  let resolve: (response: Response) => void = () => {
    throw new Error('尚未设置响应')
  }
  const promise = new Promise<Response>((finish) => {
    resolve = finish
  })
  return { promise, resolve }
}

describe('首次设置管理员密码', () => {
  it('Given 未初始化时直达业务页 When 校验并设置密码 Then 仅提交一次并在重新登录后返回目标', async () => {
    const service: InitializationService = {}
    const { submissions, statusRequests } = mockInitializationService(service)
    const { app, router } = await mountAt('/users?source=initialization#main')
    expect(router.currentRoute.value.path).toBe('/initialize')
    expect(app.get('input[autocomplete="username"]').element).toMatchObject({
      value: 'admin',
      readOnly: true,
    })
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(false)
    expect(statusRequests[0]?.cache).toBe('no-store')

    await fillInitialization(app, 'short')
    await app.get('form').trigger('submit')
    expect(app.get('#newPassword').attributes('aria-invalid')).toBe('true')
    await fillInitialization(app, ' personal ', 'different')
    await app.get('form').trigger('submit')
    expect(app.get('#confirmPassword').attributes('aria-invalid')).toBe('true')
    expect(submissions).toEqual([])

    const response = deferResponse()
    service.submit = () => response.promise
    await fillInitialization(app, ' personal ')
    await app.get('form').trigger('submit')
    await app.get('form').trigger('submit')
    expect(app.get('button[type="submit"]').attributes('disabled')).toBeDefined()
    await flushPromises()
    expect(submissions).toEqual([{ newPassword: ' personal ' }])

    service.initialized = true
    response.resolve(new Response(null, { status: 204 }))
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/login')
    expect(app.get('[role="status"]').text()).toContain('初始化成功')
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(false)

    await app.get('#loginUsername').setValue('admin')
    await app.get('#loginPassword').setValue(' personal ')
    await app.get('form').trigger('submit')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/users?source=initialization#main')
    expect(app.get('[data-testid="account-button"]').text()).toContain(admin.name)
  })

  it('Given 初始化状态不可用 When 直接进入登录页并重试 Then 等状态确认后再显示初始化表单', async () => {
    const service: InitializationService = { statusUnavailable: true }
    mockInitializationService(service)
    const { app, router } = await mountAt('/login?returnTo=%2Fusers')
    expect(app.find('form').exists()).toBe(false)
    expect(app.get('[role="alert"]').text()).toContain('请稍后重试')
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(false)

    service.statusUnavailable = false
    await app.get('button').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/initialize')
    expect(router.currentRoute.value.query.returnTo).toBe('/users')
  })

  it.each(['focus', 'conflict', 'lost-response'] as const)(
    'Given 另一处已完成初始化 When 收到 %s 事件 Then 提示已初始化并安全进入登录页',
    async (reason) => {
      const service: InitializationService = {}
      const { submissions } = mockInitializationService(service)
      const { app, router } = await mountAt('/initialize?returnTo=%2F%2Fexample.test')
      expect(app.find('#newPassword').exists()).toBe(true)
      service.initialized = true
      if (reason === 'focus') {
        window.dispatchEvent(new Event('focus'))
      } else {
        service.submit =
          reason === 'conflict'
            ? async () => Response.json({ code: 'already_initialized' }, { status: 409 })
            : async () => {
                throw new TypeError('响应丢失')
              }
        await fillInitialization(app, 'personal')
        await app.get('form').trigger('submit')
      }
      await flushPromises()
      expect(router.currentRoute.value.path).toBe('/login')
      expect(router.currentRoute.value.query.returnTo).toBe('/')
      expect(app.get('[role="status"]').text()).toContain('已初始化')
      expect(app.get('[role="status"]').text()).not.toContain('初始化成功')
      expect(submissions).toHaveLength(reason === 'focus' ? 0 : 1)
      expect(app.find('[data-testid="sidebar"]').exists()).toBe(false)
    },
  )

  it('Given 初始化请求失败且服务仍未初始化 When 检查与手动重试 Then 保留表单且不自动补发写入', async () => {
    const service: InitializationService = {
      submit: async () => new Response(null, { status: 503 }),
    }
    const { submissions } = mockInitializationService(service)
    const { app, router } = await mountAt('/initialize')
    await fillInitialization(app, 'personal')
    await app.get('form').trigger('submit')
    await flushPromises()
    expect(app.get('[role="alert"]').text()).toContain('初始化失败')
    expect(app.get('#newPassword').element).toHaveProperty('value', 'personal')
    expect(submissions).toHaveLength(1)

    service.submit = undefined
    await app.get('form').trigger('submit')
    await flushPromises()
    expect(submissions).toHaveLength(2)
    expect(router.currentRoute.value.path).toBe('/login')
  })

  it('Given 初始化响应丢失且暂不能确认状态 When 点击重试 Then 只查询状态并显示实际结果', async () => {
    const service: InitializationService = {}
    service.submit = async () => {
      service.initialized = true
      service.statusUnavailable = true
      throw new TypeError('响应丢失')
    }
    const { submissions, statusRequests } = mockInitializationService(service)
    const { app, router } = await mountAt('/initialize')
    await fillInitialization(app, 'personal')
    await app.get('form').trigger('submit')
    await flushPromises()
    expect(app.find('form').exists()).toBe(false)
    expect(app.get('[role="alert"]').text()).toContain('请稍后重试')
    expect(statusRequests).toHaveLength(2)

    service.statusUnavailable = false
    await app.get('button').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/login')
    expect(app.get('[role="status"]').text()).toContain('已初始化')
    expect(submissions).toHaveLength(1)
    expect(statusRequests).toHaveLength(3)
  })

  it('Given 初始化的响应尚未返回 When 重新聚焦确认新的会话且旧响应才到达 Then 保留新会话和业务地址', async () => {
    const response = deferResponse()
    const service: InitializationService = { submit: () => response.promise }
    mockInitializationService(service)
    const { app, router } = await mountAt('/users')
    await fillInitialization(app, 'personal')
    await app.get('form').trigger('submit')
    await flushPromises()

    service.initialized = true
    service.signedIn = true
    window.dispatchEvent(new Event('focus'))
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/users')
    expect(app.get('[data-testid="account-button"]').text()).toContain(admin.name)

    response.resolve(new Response(null, { status: 204 }))
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/users')
    expect(app.get('[data-testid="account-button"]').text()).toContain(admin.name)
  })

  it('Given 其他页签在初始化 204 到达前已有会话 When 当前页签在登录页重新聚焦 Then 识别共享会话并进入原目标', async () => {
    const response = deferResponse()
    const service: InitializationService = { submit: () => response.promise }
    const { statusRequests } = mockInitializationService(service)
    const { app, router } = await mountAt('/users?source=initialization#main')
    await fillInitialization(app, 'personal')
    await app.get('form').trigger('submit')
    await flushPromises()

    service.initialized = true
    service.signedIn = true
    response.resolve(new Response(null, { status: 204 }))
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/login')
    expect(app.get('[role="status"]').text()).toContain('初始化成功')
    expect(app.find('[data-testid="sidebar"]').exists()).toBe(false)
    expect(statusRequests).toHaveLength(1)

    window.dispatchEvent(new Event('focus'))
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/users?source=initialization#main')
    expect(app.get('[data-testid="account-button"]').text()).toContain(admin.name)
  })

  it('Given 初始化请求等待响应 When 状态短暂失败并恢复显示表单 Then 继续等待同一次提交', async () => {
    const response = deferResponse()
    const service: InitializationService = { submit: () => response.promise }
    const { submissions } = mockInitializationService(service)
    const { app, router } = await mountAt('/initialize')
    await fillInitialization(app, 'personal')
    await app.get('form').trigger('submit')
    await flushPromises()

    service.statusUnavailable = true
    window.dispatchEvent(new Event('focus'))
    await flushPromises()
    expect(app.find('form').exists()).toBe(false)
    service.statusUnavailable = false
    await app.get('button').trigger('click')
    await flushPromises()
    expect(app.get('button[type="submit"]').attributes('disabled')).toBeDefined()
    expect(submissions).toHaveLength(1)

    service.initialized = true
    response.resolve(new Response(null, { status: 204 }))
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/login')
    expect(app.get('[role="status"]').text()).toContain('初始化成功')
  })

  it.each([
    { initialized: false, signedIn: false, destination: '/initialize' },
    { initialized: true, signedIn: false, destination: '/login' },
    { initialized: true, signedIn: true, destination: '/users' },
  ])(
    'Given 旧改密链接 When 当前会话确认 Then 进入 $destination',
    async ({ initialized, signedIn, destination }) => {
      mockInitializationService({ initialized, signedIn })
      const { router } = await mountAt('/change-password?returnTo=%2Fusers')
      expect(router.currentRoute.value.path).toBe(destination)
    },
  )
})
