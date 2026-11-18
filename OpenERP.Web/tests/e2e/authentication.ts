import { expect, type APIRequestContext, type Page } from '@playwright/test'

export const adminPassword = process.env.OPENERP_TEST_ADMIN_PASSWORD ?? ''
const accessTokens = new WeakMap<APIRequestContext, string>()

type SessionResponse = {
  accessToken: string
  accessTokenExpiresAt: string
  account: {
    username: string
    name: string
    phone: string | null
    email: string | null
    lastLoginAt: string | null
    expiresAt: string
  }
}

export async function expectSessionResponse(response: {
  json(): Promise<unknown>
}): Promise<SessionResponse> {
  const session = (await response.json()) as SessionResponse
  expect(session).toMatchObject({
    accessToken: expect.any(String),
    accessTokenExpiresAt: expect.any(String),
    account: {
      username: expect.any(String),
      name: expect.any(String),
      expiresAt: expect.any(String),
    },
  })
  expect(typeof session.account.phone === 'string' || session.account.phone === null).toBe(true)
  expect(typeof session.account.email === 'string' || session.account.email === null).toBe(true)
  expect(
    typeof session.account.lastLoginAt === 'string' || session.account.lastLoginAt === null,
  ).toBe(true)
  expect(session.accessToken.length).toBeGreaterThan(0)
  expect(Number.isFinite(Date.parse(session.accessTokenExpiresAt))).toBe(true)
  expect(Number.isFinite(Date.parse(session.account.expiresAt))).toBe(true)

  if (session.account.lastLoginAt) {
    const accountLifetime =
      Date.parse(session.account.expiresAt) - Date.parse(session.account.lastLoginAt)
    expect(Math.abs(accountLifetime - 24 * 60 * 60 * 1000)).toBeLessThan(5_000)
  }

  return session
}

// 前端 CI 不启动后端：用页面级 mock 会话让工作台用例照常渲染。
export async function enterWorkspace(page: Page): Promise<void> {
  if (adminPassword) {
    await signInForWorkspace(page.request)
    return
  }

  const now = Date.now()
  const account = {
    username: 'admin',
    name: '验收管理员',
    phone: null,
    email: null,
    lastLoginAt: new Date(now).toISOString(),
    expiresAt: new Date(now + 24 * 60 * 60 * 1000).toISOString(),
  }
  await page.route('**/api/auth/status', (route) => route.fulfill({ json: { initialized: true } }))
  await page.route('**/api/auth/refresh', (route) =>
    route.fulfill({
      json: {
        accessToken: 'ci-access-token',
        accessTokenExpiresAt: new Date(now + 20 * 60 * 1000).toISOString(),
        account,
      },
    }),
  )
  await page.route('**/api/auth/csrf', (route) =>
    route.fulfill({ json: { requestToken: 'ci-token' } }),
  )
}

export async function csrfHeaders(request: APIRequestContext): Promise<Record<string, string>> {
  const response = await request.get('/api/auth/csrf')
  expect(response.ok()).toBe(true)
  return { 'X-CSRF-TOKEN': (await response.json()).requestToken }
}

export async function signInWithApi(request: APIRequestContext, password = adminPassword) {
  const response = await request.post('/api/auth/login', {
    headers: await csrfHeaders(request),
    data: { username: 'admin', password },
  })
  if (response.ok()) {
    const session = await expectSessionResponse(response)
    accessTokens.set(request, session.accessToken)
  }
  return response
}

export async function bearerHeaders(request: APIRequestContext): Promise<Record<string, string>> {
  const accessToken = accessTokens.get(request)
  expect(accessToken, '先通过 signInWithApi 获取 access token').toBeTruthy()
  return { Authorization: `Bearer ${accessToken}` }
}

export async function authenticatedHeaders(
  request: APIRequestContext,
): Promise<Record<string, string>> {
  return bearerHeaders(request)
}

export async function signInForWorkspace(request: APIRequestContext): Promise<void> {
  const login = await signInWithApi(request)
  expect(login.status()).toBe(200)
}
