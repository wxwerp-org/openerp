import { readonly, ref } from 'vue'

export type AccountRole = 'Administrator' | 'User'

export type Account = {
  id: string
  role: AccountRole
  username: string
  name: string
  phone: string | null
  email: string | null
  lastLoginAt: string | null
  expiresAt: string
}

export type SessionState =
  'pending' | 'unavailable' | 'uninitialized' | 'anonymous' | 'authenticated'

type SessionResponse = {
  accessToken: string
  accessTokenExpiresAt: string
  account: Account
}

type RefreshResult = 'refreshed' | 'invalid' | 'failed' | 'stale'

const refreshInterval = 5 * 60 * 1000
const csrfProtectedPaths = new Set([
  '/api/auth/initialize',
  '/api/auth/login',
  '/api/auth/refresh',
  '/api/auth/logout',
])
const bearerExcludedPaths = new Set([
  '/api/auth/status',
  '/api/auth/csrf',
  '/api/auth/initialize',
  '/api/auth/login',
  '/api/auth/refresh',
  '/api/auth/logout',
])

const currentAccount = ref<Account | null>(null)
const state = ref<SessionState>('pending')
const initializedHere = ref(false)
const initializing = ref(false)
let accessToken: string | null = null
let revision = 0
let checking: { revision: number; promise: Promise<void> } | undefined
let refreshing: { revision: number; promise: Promise<RefreshResult> } | undefined
let csrfToken: Promise<string> | undefined
let refreshTimer: number | undefined
let refreshAt = 0
let refreshFailed = false

export const account = readonly(currentAccount)
export const sessionState = readonly(state)
export const initializationCompleted = readonly(initializedHere)
export const initializationPending = readonly(initializing)

function setAccount(
  value: Account | null,
  emptyState: 'anonymous' | 'uninitialized' = 'anonymous',
): void {
  currentAccount.value = value
  state.value = value ? 'authenticated' : emptyState
}

function clearRefreshSchedule(): void {
  if (refreshTimer !== undefined) window.clearTimeout(refreshTimer)
  refreshTimer = undefined
  refreshAt = 0
}

function scheduleRefresh(delay = refreshInterval): void {
  clearRefreshSchedule()
  if (!currentAccount.value || !accessToken) return
  refreshAt = performance.now() + delay
  refreshTimer = window.setTimeout(() => {
    refreshTimer = undefined
    void refreshAccessToken(revision)
  }, delay)
}

function changeSession(
  value: Account | null,
  emptyState: 'anonymous' | 'uninitialized' = 'anonymous',
  session?: SessionResponse,
): void {
  revision++
  csrfToken = undefined
  initializedHere.value = false
  accessToken = session?.accessToken ?? null
  refreshFailed = false
  setAccount(value, emptyState)
  if (session) scheduleRefresh()
  else clearRefreshSchedule()
}

function setUninitialized(): void {
  if (state.value === 'anonymous' || state.value === 'authenticated' || accessToken) {
    changeSession(null, 'uninitialized')
  } else {
    setAccount(null, 'uninitialized')
  }
}

async function initializationRequired(response: Response): Promise<boolean> {
  if (response.status !== 409) return false
  const problem = await response
    .clone()
    .json()
    .catch(() => null)
  return problem?.code === 'initialization_required'
}

async function responseError(response: Response, fallback: string): Promise<string> {
  const problem = await response.json().catch(() => null)
  return typeof problem?.detail === 'string' ? problem.detail : fallback
}

function isAccount(value: unknown): value is Account {
  if (!value || typeof value !== 'object') return false
  const candidate = value as Partial<Account>
  return (
    typeof candidate.id === 'string' &&
    (candidate.role === 'Administrator' || candidate.role === 'User') &&
    typeof candidate.username === 'string' &&
    typeof candidate.name === 'string' &&
    (typeof candidate.phone === 'string' || candidate.phone === null) &&
    (typeof candidate.email === 'string' || candidate.email === null) &&
    (typeof candidate.lastLoginAt === 'string' || candidate.lastLoginAt === null) &&
    typeof candidate.expiresAt === 'string' &&
    Number.isFinite(Date.parse(candidate.expiresAt))
  )
}

async function readSessionResponse(response: Response): Promise<SessionResponse> {
  const payload = await response.json()
  if (
    !payload ||
    typeof payload.accessToken !== 'string' ||
    !payload.accessToken ||
    typeof payload.accessTokenExpiresAt !== 'string' ||
    !Number.isFinite(Date.parse(payload.accessTokenExpiresAt)) ||
    !isAccount(payload.account)
  ) {
    throw new Error('无法验证登录状态。')
  }
  return payload as SessionResponse
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

function markRefreshFailed(requestRevision: number): void {
  if (requestRevision !== revision) return
  refreshFailed = true
  if (currentAccount.value && accessToken) scheduleRefresh()
}

async function performRefresh(requestRevision: number): Promise<RefreshResult> {
  const accountAtRequest = currentAccount.value
  let tokenRequest: Promise<string> | undefined
  try {
    tokenRequest = getCsrfToken()
    const requestToken = await tokenRequest
    if (requestRevision !== revision) return 'stale'

    const response = await fetch('/api/auth/refresh', {
      method: 'POST',
      headers: { 'X-CSRF-TOKEN': requestToken },
      cache: 'no-store',
    })
    if (requestRevision !== revision) return 'stale'
    if (response.status === 401) {
      if (currentAccount.value || accessToken) changeSession(null)
      else setAccount(null)
      return 'invalid'
    }
    if (!response.ok) {
      if (response.status === 400 && csrfToken === tokenRequest) csrfToken = undefined
      markRefreshFailed(requestRevision)
      return 'failed'
    }

    const session = await readSessionResponse(response)
    if (requestRevision !== revision) return 'stale'
    accessToken = session.accessToken
    refreshFailed = false
    if (currentAccount.value === accountAtRequest) setAccount(session.account)
    scheduleRefresh()
    return 'refreshed'
  } catch {
    if (requestRevision === revision && tokenRequest && csrfToken === tokenRequest)
      csrfToken = undefined
    markRefreshFailed(requestRevision)
    return 'failed'
  }
}

function refreshAccessToken(requestRevision = revision): Promise<RefreshResult> {
  if (refreshing?.revision === requestRevision) return refreshing.promise
  const promise = performRefresh(requestRevision)
  refreshing = { revision: requestRevision, promise }
  void promise
    .finally(() => {
      if (refreshing?.promise === promise) refreshing = undefined
    })
    .catch(() => undefined)
  return promise
}

function refreshIfDue(requestRevision: number): Promise<RefreshResult> | 'not-needed' {
  if (!currentAccount.value || !accessToken) return 'not-needed'
  if (!refreshFailed && refreshAt > performance.now()) return 'not-needed'
  return refreshAccessToken(requestRevision)
}

function usesBearer(path: string): boolean {
  return path.startsWith('/api/') && !bearerExcludedPaths.has(path)
}

/** 当前 access token，仅供 SignalR accessTokenFactory 使用。 */
export function currentAccessToken(): string | null {
  return accessToken
}

function safeToRetry(method: string): boolean {
  return method === 'GET' || method === 'HEAD'
}

export async function apiRequest(path: string, init: RequestInit = {}): Promise<Response> {
  const requestRevision = revision
  const method = init.method?.toUpperCase() ?? 'GET'
  const protectedRequest = usesBearer(path)
  const requiresCsrf = csrfProtectedPaths.has(path)
  const headers = new Headers(init.headers)
  let refreshAttempted = false

  if (protectedRequest) {
    const refreshRequest = refreshIfDue(requestRevision)
    const refresh = refreshRequest === 'not-needed' ? refreshRequest : await refreshRequest
    refreshAttempted = refresh !== 'not-needed'
    if (refresh === 'invalid') return new Response(null, { status: 401 })
    if (requestRevision !== revision) return new Response(null, { status: 401 })
  }

  const tokenRequest = requiresCsrf ? getCsrfToken() : undefined
  if (tokenRequest) headers.set('X-CSRF-TOKEN', await tokenRequest)
  if (requestRevision !== revision) return new Response(null, { status: 401 })

  if (protectedRequest && accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
  else headers.delete('Authorization')
  const sentAccessToken = protectedRequest ? accessToken : null

  const response = await fetch(path, { ...init, headers })
  if (requestRevision !== revision) return response
  if (tokenRequest && response.status === 400 && csrfToken === tokenRequest) csrfToken = undefined
  if (
    response.status === 409 &&
    (await initializationRequired(response)) &&
    requestRevision === revision
  ) {
    changeSession(null, 'uninitialized')
    return response
  }

  if (
    response.status !== 401 ||
    !protectedRequest ||
    refreshAttempted ||
    requestRevision !== revision
  )
    return response

  const refresh =
    accessToken && accessToken !== sentAccessToken
      ? 'refreshed'
      : await refreshAccessToken(requestRevision)
  if (refresh !== 'refreshed' || requestRevision !== revision || !safeToRetry(method))
    return response

  const retryHeaders = new Headers(init.headers)
  if (accessToken) retryHeaders.set('Authorization', `Bearer ${accessToken}`)
  const retry = await fetch(path, { ...init, headers: retryHeaders })
  return retry
}

export function checkSession(): Promise<void> {
  if (checking?.revision === revision) return checking.promise

  const snapshot = currentAccount.value
  const checkRevision = revision
  if (state.value === 'unavailable') state.value = 'pending'
  const current = () => checking?.promise === check && checkRevision === revision
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

      const result = await refreshAccessToken(checkRevision)
      if (!current()) return
      if (result === 'refreshed' && !snapshot) {
        initializedHere.value = false
        revision++
      }
      if (result === 'failed' && !currentAccount.value) state.value = 'unavailable'
      if (result === 'stale' && !snapshot && checkRevision === revision) state.value = 'unavailable'
    } catch {
      if (current() && !snapshot) state.value = 'unavailable'
    } finally {
      if (checking?.promise === check) checking = undefined
    }
  })
  checking = { revision: checkRevision, promise: check }
  return check
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
  let session: SessionResponse
  try {
    session = await readSessionResponse(response)
  } catch {
    throw new Error('登录失败，请稍后重试。')
  }
  if (requestRevision !== revision) return
  changeSession(session.account, 'anonymous', session)
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

async function accountRequest(
  path: string,
  init: RequestInit,
  fallback: string,
): Promise<Response> {
  const response = await apiRequest(path, init)
  if (!response.ok) throw new Error(await responseError(response, fallback))
  return response
}

export async function saveProfile(fields: {
  name: string
  phone: string
  email: string
}): Promise<void> {
  const requestRevision = revision
  const response = await accountRequest(
    '/api/account/profile',
    jsonRequest('PUT', fields),
    '无法保存个人信息，请稍后重试。',
  )
  const updated = await response.json()
  if (!isAccount(updated)) throw new Error('无法保存个人信息，请稍后重试。')
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
  // 网络恢复后，下次防伪写请求会重新获取令牌。
  void getCsrfToken().catch(() => undefined)
}

export function logout(): Promise<void> {
  return endSession('/api/auth/logout', { method: 'POST' }, '退出登录失败，请稍后重试。')
}

export function changePassword(fields: {
  oldPassword: string
  newPassword: string
}): Promise<void> {
  return endSession(
    '/api/auth/change-password',
    jsonRequest('POST', fields),
    '无法修改密码，请稍后重试。',
  )
}
