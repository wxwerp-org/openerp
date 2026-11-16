import { randomBytes } from 'node:crypto'
import { expect, type APIRequestContext, type Page } from '@playwright/test'

export const adminPassword = process.env.OPENERP_TEST_ADMIN_PASSWORD ?? ''

// 前端 CI 不启动后端：用页面级 mock 会话让工作台用例照常渲染。
export async function enterWorkspace(page: Page): Promise<void> {
  if (adminPassword) {
    await signInForWorkspace(page.request)
    return
  }

  // expiresAt 必须是未来时间，否则前端会立即判定会话过期。
  const expiresAt = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString()
  await page.route('**/api/auth/me', (route) =>
    route.fulfill({
      json: {
        username: 'admin',
        name: '验收管理员',
        phone: null,
        email: null,
        lastLoginAt: null,
        expiresAt,
        mustChangePassword: false,
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
  return request.post('/api/auth/login', {
    headers: await csrfHeaders(request),
    data: { username: 'admin', password },
  })
}

export async function signInForWorkspace(request: APIRequestContext): Promise<void> {
  const login = await signInWithApi(request)
  expect(login.status()).toBe(200)
  if (!(await login.json()).mustChangePassword) return

  // 单独运行工作台用例时，也完成隔离测试账号的首次改密。
  const replacement = randomBytes(8).toString('hex')
  const changed = await request.post('/api/auth/change-password', {
    headers: await csrfHeaders(request),
    data: { newPassword: replacement },
  })
  expect(changed.status()).toBe(204)
  expect((await signInWithApi(request, replacement)).status()).toBe(200)
  const restored = await request.post('/api/auth/change-password', {
    headers: await csrfHeaders(request),
    data: { oldPassword: replacement, newPassword: adminPassword },
  })
  expect(restored.status()).toBe(204)
  expect((await signInWithApi(request)).status()).toBe(200)
}
