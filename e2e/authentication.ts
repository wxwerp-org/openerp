import { expect, type APIRequestContext, type Page } from '@playwright/test'

export const adminPassword = process.env.OPENERP_TEST_ADMIN_PASSWORD ?? ''

// E2E 总是连接隔离的真实 OpenERP 宿主。
export async function enterWorkspace(page: Page): Promise<void> {
  await signInForWorkspace(page.request)
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
}
