import { randomBytes } from 'node:crypto'
import { expect, type APIRequestContext } from '@playwright/test'

export const adminPassword = process.env.OPENERP_TEST_ADMIN_PASSWORD ?? ''

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
