import { readonly, ref } from 'vue'

export type Account = {
  username: string
  name: string
  phone: string | null
  email: string | null
  lastLoginAt: string | null
  expiresAt: string
}

export type SessionState = 'pending' | 'unavailable' | 'uninitialized' | 'anonymous' | 'authenticated'

const currentAccount = ref<Account | null>(null)
const state = ref<SessionState>('pending')
const initializedHere = ref(false)
const initializing = ref(false)
let revision = 0
let checking: { revision: number; promise: Promise<void> } | undefined
let csrfToken: Promise<string> | undefined

export const account = readonly(currentAccount)
export const sessionState = readonly(state)
export const initializationCompleted = readonly(initializedHere)
export const initializationPending = readonly(initializing)

function setAccount(value: Account | null, emptyState: 'anonymous' | 'uninitialized' = 'anonymous'): void {
  currentAccount.value = value
  state.value = value ? 'authenticated' : emptyState
}

function changeSession(value: Account | null, emptyState: 'anonymous' | 'uninitialized' = 'anonymous'): void {
  revision++
  csrfToken = undefined
  initializedHere.value = false
  setAccount(value, emptyState)
}

function setUninitialized(): void {
  if (state.value === 'anonymous' || state.value === 'authenticated') changeSession(null, 'uninitialized')
  else setAccount(null, 'uninitialized')
}

async function initializationRequired(response: Response): Promise<boolean> {
  if (response.status !== 409) return false
  const problem = await response.clone().json().catch(() => null)
  return problem?.code === 'initialization_required'
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
  } else if (tokenRequest && response.status === 400 && csrfToken === tokenRequest) {
    // 防伪校验也返回 400，用户下次提交时重新取令牌。
    csrfToken = undefined
  } else if (response.status === 409 && await initializationRequired(response) && requestRevision === revision) {
    changeSession(null, 'uninitialized')
  }
  return response
}

export function checkSession(): Promise<void> {
  if (checking?.revision === revision) return checking.promise

  const snapshot = currentAccount.value
  const checkRevision = revision
  if (state.value === 'unavailable') state.value = 'pending'
  const current = () => checking?.promise === check && checkRevision === revision && currentAccount.value === snapshot
  const check = Promise.resolve().then(async () => {
    try {
      const status = await fetch('/api/auth/status', { cache: 'no-store' })
      if (!status.ok) throw new Error('无法验证登录状态。')
      const { initialized } = await status.json()
      if (!current()) return
      if (typeof initialized !== 'boolean') throw new Error('无法验证登录状态。')
      if (!initialized) {
        setUninitialized()
        return
      }

      const response = await fetch('/api/auth/me', { cache: 'no-store' })
      if (!current()) return
      if (response.status === 401) {
        if (snapshot) changeSession(null)
        else setAccount(null)
        return
      }
      if (await initializationRequired(response)) {
        if (current()) setUninitialized()
        return
      }
      if (!response.ok) throw new Error('无法验证登录状态。')
      const session: Account = await response.json()
      if (!current()) return
      if (snapshot?.expiresAt !== session.expiresAt) changeSession(session)
      else setAccount(session)
    } catch {
      if (current() && !snapshot) state.value = 'unavailable'
    } finally {
      if (checking?.promise === check) checking = undefined
    }
  })
  checking = { revision: checkRevision, promise: check }
  return check
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
  const requestRevision = revision
  const response = await apiRequest('/api/auth/login', jsonRequest('POST', { username, password }))
  if (!response.ok) {
    const fallback = response.status === 401 ? '用户名或密码错误' : '登录失败，请稍后重试。'
    throw new Error(await responseError(response, fallback))
  }
  const session: Account = await response.json()
  if (requestRevision !== revision) return
  changeSession(session)
  warmCsrfToken()
}

export async function initialize(newPassword: string): Promise<void> {
  if (state.value !== 'uninitialized' || initializing.value) return
  initializing.value = true
  try {
    await submitInitialization(newPassword)
  } finally {
    initializing.value = false
  }
}

async function submitInitialization(newPassword: string): Promise<void> {
  const requestRevision = revision
  const fallback = '初始化失败，请稍后重试。'
  let error = fallback
  try {
    const response = await apiRequest('/api/auth/initialize', jsonRequest('POST', { newPassword }))
    if (requestRevision !== revision) return
    if (response.status === 204) {
      changeSession(null)
      initializedHere.value = true
      return
    }
    error = await responseError(response, fallback)
  } catch (cause) {
    if (cause instanceof Error && !(cause instanceof TypeError)) error = cause.message
  }

  if (requestRevision !== revision) return
  // 必须查询写入尝试之后的状态。写入前已发出的检查可能仍会返回未初始化。
  checking = undefined
  await checkSession()
  if (requestRevision === revision && state.value === 'uninitialized') throw new Error(error)
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

export function changePassword(fields: { oldPassword: string; newPassword: string }): Promise<void> {
  return endSession(
    '/api/auth/change-password',
    jsonRequest('POST', fields),
    '无法修改密码，请稍后重试。',
  )
}
