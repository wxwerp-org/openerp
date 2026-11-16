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

describe('真实会话路由', () => {
  it('Given 首次直达受保护页且会话仍在验证 When 会话确认无效 Then 始终隐藏业务界面并保留目标地址', async () => {
    let resolve: (response: Response) => void = () => { throw new Error('未发起会话请求') }
    vi.stubGlobal('fetch', () => new Promise<Response>((finish) => { resolve = finish }))
    const { app, router } = await mountAt('/users?from=notice#main')

    expect(app.find('.sidebar').exists()).toBe(false)
    resolve(new Response(null, { status: 401 }))
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/login')
    expect(router.currentRoute.value.query.returnTo).toBe('/users?from=notice#main')
    expect(app.get('h1').text()).toBe('登录 OpenERP')
    expect(app.find('.sidebar').exists()).toBe(false)
  })

  it('Given 会话服务暂时不可用 When 检查返回 503 后重试成功 Then 保留当前地址并进入工作台', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 503 }))
      .mockResolvedValueOnce(Response.json(admin))
    vi.stubGlobal('fetch', fetch)
    const { app, router } = await mountAt('/users')

    expect(router.currentRoute.value.path).toBe('/users')
    expect(app.find('.sidebar').exists()).toBe(false)
    await app.get('button').trigger('click')
    await flushPromises()

    expect(app.get('.account-button').text()).toContain('值班管理员')
    expect(router.currentRoute.value.path).toBe('/users')
  })

  it('Given 登录页带外站返回地址 When 凭证错误后用正确凭证重试 Then 保留登录反馈并安全进入首页', async () => {
    let csrfCount = 0
    const fetch = vi.fn((url: string, request?: RequestInit) => {
      if (url === '/api/auth/csrf') return Promise.resolve(Response.json({ requestToken: `csrf-${++csrfCount}` }))
      if (url === '/api/auth/me') return Promise.resolve(new Response(null, { status: 401 }))
      const credentials = JSON.parse(String(request?.body))
      if (credentials.password !== ' valid ') {
        return Promise.resolve(Response.json({ detail: '用户名或密码错误' }, { status: 401 }))
      }
      const token = new Headers(request?.headers).get('X-CSRF-TOKEN')
      return Promise.resolve(token ? Response.json(admin) : new Response(null, { status: 400 }))
    })
    vi.stubGlobal('fetch', fetch)
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
    expect(app.get('.account-button').text()).toContain('值班管理员')
    expect(csrfCount).toBeGreaterThan(1)
  })

  it('Given 已登录 When 会话检查暂时失败后业务接口返回 401 Then 先保留工作台再退出到登录页', async () => {
    let meStatus = 200
    vi.stubGlobal('fetch', (url: string) => {
      if (url !== '/api/auth/me') return Promise.resolve(new Response(null, { status: 401 }))
      return Promise.resolve(meStatus === 200 ? Response.json(admin) : new Response(null, { status: meStatus }))
    })
    const { app, router } = await mountAt('/')
    expect(app.get('.account-button').text()).toContain('值班管理员')

    meStatus = 503
    window.dispatchEvent(new Event('focus'))
    await flushPromises()
    expect(app.find('.sidebar').exists()).toBe(true)

    await router.push('/service-connection')
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/login')
    expect(app.find('.sidebar').exists()).toBe(false)
  })

  it('Given 会话即将到达固定期限 When 到达过期时间 Then 无须业务请求即回到登录页', async () => {
    const expiresAt = new Date(Date.now() + 30_000).toISOString()
    vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'] })
    vi.stubGlobal('fetch', () => Promise.resolve(Response.json({ ...admin, expiresAt })))
    const { app, router } = await mountAt('/')
    expect(app.find('.sidebar').exists()).toBe(true)

    await vi.advanceTimersByTimeAsync(30_000)
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/login')
    expect(app.find('.sidebar').exists()).toBe(false)
  })
})

async function openAccountDialog(app: VueWrapper, action: 'profile' | 'password'): Promise<void> {
  await app.get('.account-button').trigger('click')
  await app.get(`[data-account-action="${action}"]`).trigger('click')
}

describe('真实账号操作', () => {
  it('Given 从会话加载真实资料 When 取消编辑和保存失败后重试 Then 仅成功保存时关闭并刷新姓名', async () => {
    let saves = 0
    vi.stubGlobal('fetch', (url: string, request?: RequestInit) => {
      if (url === '/api/auth/csrf') return Promise.resolve(Response.json({ requestToken: 'account-token' }))
      if (url === '/api/auth/me') return Promise.resolve(Response.json(admin))
      const fields = JSON.parse(String(request?.body))
      const token = new Headers(request?.headers).get('X-CSRF-TOKEN')
      if (url !== '/api/account/profile' || request?.method !== 'PUT' || token !== 'account-token' || fields.name !== '仓库管理员') {
        return Promise.resolve(new Response(null, { status: 400 }))
      }
      saves++
      return Promise.resolve(saves === 1
        ? Response.json({ detail: '暂时无法保存' }, { status: 503 })
        : Response.json({ ...admin, ...fields }))
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
    expect(app.get('.account-button').text()).toContain(admin.name)

    await app.get('#saveProfile').trigger('click')
    await flushPromises()
    expect(app.get('#profileLayer').attributes('open')).toBeUndefined()
    expect(app.get('.account-button').text()).toContain('仓库管理员')
  })

  it('Given 资料弹窗 When 手机号超长或邮箱含空格 Then 标明首个无效字段且保留表单', async () => {
    let saves = 0
    vi.stubGlobal('fetch', (url: string) => {
      if (url === '/api/auth/csrf') return Promise.resolve(Response.json({ requestToken: 'account-token' }))
      if (url === '/api/auth/me') return Promise.resolve(Response.json(admin))
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
  ])('Given 另一页签更换共享 Cookie When $label Then 取得新会话的防伪令牌并保存', async ({ refocus }) => {
    let sessionNumber = 1
    let attempts = 0
    let savedName = admin.name
    vi.stubGlobal('fetch', (url: string, request?: RequestInit) => {
      const session = { ...admin, name: savedName, expiresAt: `2099-01-0${sessionNumber}T00:00:00Z` }
      const token = `session-${sessionNumber}`
      if (url === '/api/auth/csrf') return Promise.resolve(Response.json({ requestToken: token }))
      if (url === '/api/auth/me') return Promise.resolve(Response.json(session))
      attempts++
      if (new Headers(request?.headers).get('X-CSRF-TOKEN') !== token) {
        return Promise.resolve(Response.json({ detail: 'CSRF 校验失败' }, { status: 400 }))
      }
      savedName = JSON.parse(String(request?.body)).name
      return Promise.resolve(Response.json({ ...session, name: savedName }))
    })
    const { app } = await mountAt('/')
    await openAccountDialog(app, 'profile')
    await app.get('#profileName').setValue('第一会话')
    await app.get('#saveProfile').trigger('click')
    await flushPromises()
    expect(app.get('.account-button').text()).toContain('第一会话')

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
    expect(app.get('.account-button').text()).toContain(refocus ? '第二会话' : '第一会话')
    expect(attempts).toBe(2)
    if (!refocus) {
      await app.get('#saveProfile').trigger('click')
      await flushPromises()
    }

    expect(app.get('.account-button').text()).toContain('第二会话')
    expect(app.get('#profileLayer').attributes('open')).toBeUndefined()
  })

  it('Given 已登录 When 原密码错误后用正确原密码提交 Then 先保留弹窗再回到登录页', async () => {
    vi.stubGlobal('fetch', (url: string, request?: RequestInit) => {
      if (url === '/api/auth/csrf') return Promise.resolve(Response.json({ requestToken: 'password-token' }))
      if (url === '/api/auth/me') return Promise.resolve(Response.json(admin))
      const { oldPassword, newPassword } = JSON.parse(String(request?.body))
      const token = new Headers(request?.headers).get('X-CSRF-TOKEN')
      if (url === '/api/auth/change-password' && token === 'password-token' && oldPassword === ' previous ' && newPassword === ' newpwd ') {
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
    expect(app.find('.sidebar').exists()).toBe(false)
  })

  it('Given 已登录 When 退出请求失败后重试成功 Then 仅成功退出时离开工作台', async () => {
    let failed = true
    vi.stubGlobal('fetch', (url: string, request?: RequestInit) => {
      if (url === '/api/auth/me') return Promise.resolve(Response.json(admin))
      if (url === '/api/auth/csrf') return Promise.resolve(Response.json({ requestToken: 'logout-token' }))
      const token = new Headers(request?.headers).get('X-CSRF-TOKEN')
      if (url === '/api/auth/logout' && request?.method === 'POST' && token === 'logout-token') {
        return Promise.resolve(failed ? Response.json({ detail: '暂时无法退出' }, { status: 503 }) : new Response(null, { status: 204 }))
      }
      return Promise.resolve(new Response(null, { status: 400 }))
    })
    const { app, router } = await mountAt('/')

    await app.get('.account-button').trigger('click')
    await app.get('[data-account-action="logout"]').trigger('click')
    await flushPromises()
    expect(app.get('#toast').text()).toBe('暂时无法退出')
    expect(app.find('.sidebar').exists()).toBe(true)

    failed = false
    await app.get('.account-button').trigger('click')
    await app.get('[data-account-action="logout"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/login')
    expect(app.find('.sidebar').exists()).toBe(false)
  })
})

describe('临时密码首次登录', () => {
  it('Given 直达业务页后用临时密码登录 When 仅填写并确认新密码后重新登录 Then 返回原业务地址', async () => {
    let changed = false
    const passwordChanges: unknown[] = []
    vi.stubGlobal('fetch', (url: string, request?: RequestInit) => {
      if (url === '/api/auth/csrf') return Promise.resolve(Response.json({ requestToken: 'first-login-token' }))
      if (url === '/api/auth/me') return Promise.resolve(new Response(null, { status: 401 }))
      const fields = JSON.parse(String(request?.body))
      if (url === '/api/auth/login') {
        return Promise.resolve(Response.json({ ...admin, mustChangePassword: !changed }))
      }
      if (url === '/api/auth/change-password') passwordChanges.push(fields)
      if (url === '/api/auth/change-password' && !Object.hasOwn(fields, 'oldPassword') && fields.newPassword === ' personal ') {
        changed = true
        return Promise.resolve(new Response(null, { status: 204 }))
      }
      return Promise.resolve(new Response(null, { status: 400 }))
    })
    const { app, router } = await mountAt('/users?source=first-login#main')
    await app.get('#loginUsername').setValue('admin')
    await app.get('#loginPassword').setValue(' temporary ')
    await app.get('form').trigger('submit')
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/change-password')
    expect(app.find('.sidebar').exists()).toBe(false)
    expect(app.get('h1').text()).toBe('首次登录须修改密码')
    expect(router.currentRoute.value.query.returnTo).toBe('/users?source=first-login#main')
    expect(app.find('#oldPassword').exists()).toBe(false)
    expect(document.activeElement).toBe(app.get('#newPassword').element)

    await app.get('#newPassword').setValue(' personal ')
    await app.get('#confirmPassword').setValue('different')
    await app.get('form').trigger('submit')
    expect(app.get('#confirmPassword').attributes('aria-invalid')).toBe('true')
    expect(passwordChanges).toHaveLength(0)

    await app.get('#confirmPassword').setValue(' personal ')
    await app.get('form').trigger('submit')
    await flushPromises()
    expect(passwordChanges).toEqual([{ newPassword: ' personal ' }])
    expect(router.currentRoute.value.path).toBe('/login')
    expect(app.find('.sidebar').exists()).toBe(false)

    await app.get('#loginUsername').setValue('admin')
    await app.get('#loginPassword').setValue(' personal ')
    await app.get('form').trigger('submit')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/users?source=first-login#main')
    expect(app.get('.account-button').text()).toContain(admin.name)
  })

  it.each(['/users', '/change-password?returnTo=%2Fusers'])('Given 受限会话 When 加载 %s 并尝试切到业务页 Then 继续要求改密或退出', async (path) => {
    vi.stubGlobal('fetch', (url: string) => {
      if (url === '/api/auth/csrf') return Promise.resolve(Response.json({ requestToken: 'first-login-token' }))
      if (url === '/api/auth/logout') return Promise.resolve(new Response(null, { status: 204 }))
      return Promise.resolve(Response.json({ ...admin, mustChangePassword: true }))
    })
    const { app, router } = await mountAt(path)
    expect(router.currentRoute.value.path).toBe('/change-password')
    expect(app.find('.sidebar').exists()).toBe(false)

    await router.push('/orders')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/change-password')
    expect(app.find('.sidebar').exists()).toBe(false)

    const logout = app.findAll('button').find((button) => button.text() === '退出登录')
    expect(logout).toBeDefined()
    await logout!.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/login')
  })

  it('Given 正常登录 When 业务请求先返回普通 403 再返回强制改密 403 Then 只有后者打开改密页', async () => {
    let code = 'access_denied'
    vi.stubGlobal('fetch', (url: string) => Promise.resolve(url === '/api/auth/me'
      ? Response.json({ ...admin, mustChangePassword: false })
      : Response.json({ code }, { status: 403 })))
    const { app, router } = await mountAt('/service-connection')
    expect(router.currentRoute.value.path).toBe('/service-connection')
    expect(app.find('.sidebar').exists()).toBe(true)

    code = 'password_change_required'
    const retry = app.findAll('button').find((button) => button.text() === '重试')
    expect(retry).toBeDefined()
    await retry!.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/change-password')
    expect(app.find('.sidebar').exists()).toBe(false)
  })
})
