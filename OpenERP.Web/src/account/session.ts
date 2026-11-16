import { readonly, ref } from 'vue'

export type Account = {
  username: string
  name: string
  phone: string | null
  email: string | null
  lastLoginAt: string | null
  expiresAt: string
  mustChangePassword: boolean
}

const currentAccount = ref<Account | null>(null)
const state = ref<'pending' | 'unavailable' | 'anonymous' | 'authenticated'>('pending')
let revision = 0
let checking: Promise<void> | undefined
let csrfToken: Promise<string> | undefined

export const account = readonly(currentAccount)
export const sessionState = readonly(state)

function setAccount(value: Account | null): void {
  currentAccount.value = value
  state.value = value ? 'authenticated' : 'anonymous'
}

function changeSession(value: Account | null): void {
  revision++
  csrfToken = undefined
  setAccount(value)
}

async function responseError(response: Response, fallback: string): Promise<string> {
  const problem = await response.json().catch(() => null)
  return typeof problem?.detail === 'string' ? problem.detail : fallback
}

async function loadCsrfToken(): Promise<string> {
  const response = await fetch('/api/auth/csrf', { cache: 'no-store' })
  if (!response.ok) throw new Error('无法验证请求，请稍后重试。')
  const payload = await response.json()
  if (typeof payload.requestToken !== 'string' || !payload.requestToken) {
    throw new Error('无法验证请求，请稍后重试。')
  }
  return payload.requestToken
}

function getCsrfToken(): Promise<string> {
  if (csrfToken) return csrfToken
  const pending = loadCsrfToken().catch((error) => {
    if (csrfToken === pending) csrfToken = undefined
    throw error
  })
  csrfToken = pending
  return pending
}

export async function apiRequest(path: string, init: RequestInit = {}): Promise<Response> {
  const requestRevision = revision
  const method = init.method?.toUpperCase() ?? 'GET'
  const headers = new Headers(init.headers)
  const writing = !['GET', 'HEAD', 'OPTIONS', 'TRACE'].includes(method)
  const tokenRequest = writing ? getCsrfToken() : undefined
  if (tokenRequest) headers.set('X-CSRF-TOKEN', await tokenRequest)
  if (requestRevision !== revision) return new Response(null, { status: 401 })

  const response = await fetch(path, { ...init, headers })
  if (requestRevision !== revision) return response
  if (path !== '/api/auth/login' && response.status === 401) {
    changeSession(null)
  } else if (response.status === 403 && currentAccount.value && !currentAccount.value.mustChangePassword) {
    const problem = await response.clone().json().catch(() => null)
    if (requestRevision === revision && currentAccount.value && problem?.code === 'password_change_required') {
      setAccount({ ...currentAccount.value, mustChangePassword: true })
    }
  } else if (tokenRequest && response.status === 400 && csrfToken === tokenRequest) {
    // 防伪校验也返回 400，用户下次提交时重新取令牌。
    csrfToken = undefined
  }
  return response
}

export function checkSession(): Promise<void> {
  if (checking) return checking

  const snapshot = currentAccount.value
  const checkRevision = revision
  if (!snapshot) state.value = 'pending'
  checking = (async () => {
    try {
      const response = await apiRequest('/api/auth/me', { cache: 'no-store' })
      if (checkRevision !== revision || currentAccount.value !== snapshot) return
      if (!response.ok) throw new Error('无法验证登录状态。')
      const session: Account = await response.json()
      if (checkRevision !== revision || currentAccount.value !== snapshot) return
      if (snapshot && snapshot.expiresAt !== session.expiresAt) changeSession(session)
      else setAccount(session)
    } catch {
      if (checkRevision === revision && !snapshot) state.value = 'unavailable'
    } finally {
      checking = undefined
    }
  })()
  return checking
}

export function expireSession(): boolean {
  if (!currentAccount.value || Date.parse(currentAccount.value.expiresAt) > Date.now()) return false
  changeSession(null)
  return true
}

function jsonRequest(method: string, body: object): RequestInit {
  return {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  }
}

export async function login(username: string, password: string): Promise<void> {
  const response = await apiRequest('/api/auth/login', jsonRequest('POST', { username, password }))
  if (!response.ok) {
    const fallback = response.status === 401 ? '用户名或密码错误' : '登录失败，请稍后重试。'
    throw new Error(await responseError(response, fallback))
  }
  changeSession(await response.json())
  warmCsrfToken()
}

async function accountRequest(path: string, init: RequestInit, fallback: string): Promise<Response> {
  const response = await apiRequest(path, init)
  if (!response.ok) throw new Error(await responseError(response, fallback))
  return response
}

export async function saveProfile(fields: { name: string; phone: string; email: string }): Promise<void> {
  const requestRevision = revision
  const response = await accountRequest('/api/account/profile', jsonRequest('PUT', fields), '无法保存个人信息，请稍后重试。')
  const updated: Account = await response.json()
  if (requestRevision === revision) setAccount(updated)
}

async function endSession(path: string, init: RequestInit, fallback: string): Promise<void> {
  const requestRevision = revision
  await accountRequest(path, init, fallback)
  if (requestRevision !== revision) return
  changeSession(null)
  warmCsrfToken()
}

function warmCsrfToken(): void {
  // 网络恢复后，下次写请求会重新获取令牌。
  void getCsrfToken().catch(() => undefined)
}

export function logout(): Promise<void> {
  return endSession('/api/auth/logout', { method: 'POST' }, '退出登录失败，请稍后重试。')
}

export function changePassword(fields: { oldPassword?: string; newPassword: string }): Promise<void> {
  return endSession(
    '/api/auth/change-password',
    jsonRequest('POST', fields),
    '无法修改密码，请稍后重试。',
  )
}
