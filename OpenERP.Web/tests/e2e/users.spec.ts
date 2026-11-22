import { randomBytes } from 'node:crypto'
import { expect, test, type Page } from '@playwright/test'
import { authenticatedHeaders, enterWorkspace } from './authentication'

async function employeeLogin(page: Page, username: string, password: string) {
  await page.goto('/')
  await page.getByLabel('用户名').fill(username)
  await page.getByLabel('密码', { exact: true }).fill(password)
  const login = page.waitForResponse((response) => response.url().endsWith('/api/auth/login'))
  await page.getByRole('button', { name: '登录', exact: true }).click()
  const { accessToken } = await (await login).json()
  return { Authorization: `Bearer ${accessToken}` }
}

async function openAuthorization(page: Page, buttonName: string) {
  const url = 'https://v3.wxwerp.com/system/authorize'
  await page.context().route(url, (route) => route.fulfill({ body: '旺销网授权入口' }))
  const popup = page.waitForEvent('popup')
  await page.getByRole('button', { name: buttonName, exact: true }).click()
  const upstream = await popup
  await expect(upstream).toHaveURL(url)
  await upstream.close()
}

test('Given 零店铺员工代办授权 When 管理员同步并分配给另一员工 Then 只有负责人能查单、统计、续授权和同步', async ({
  page,
  browser,
  baseURL,
}, testInfo) => {
  const erpToken = process.env.OPENERP_TEST_BROWSER_ERP_TOKEN
  test.skip(!erpToken, '需要隔离宿主、真实账号和上游测试数据。')
  const password = randomBytes(8).toString('hex')
  await enterWorkspace(page)
  await page.goto('/users')
  await page.getByRole('button', { name: '添加用户', exact: true }).click()
  const create = page.getByRole('dialog', { name: '添加用户' })
  await create.getByRole('textbox', { name: '用户名', exact: true }).fill('alice')
  await create.getByRole('textbox', { name: '姓名', exact: true }).fill('订单验收员')
  await create.getByLabel(/^密码/).fill(password)
  await create.getByRole('button', { name: '保存' }).click()
  const original = page.getByRole('row').filter({ hasText: 'alice' })
  await original.getByRole('button', { name: /编辑/ }).click()
  const edit = page.getByRole('dialog', { name: '编辑用户' })
  await edit.getByRole('textbox', { name: '用户名', exact: true }).fill('bob')
  await edit.getByRole('button', { name: '保存' }).click()
  const renamed = page.getByRole('row').filter({ hasText: 'bob' })
  await expect(renamed).toContainText('订单验收员')
  await page.screenshot({ path: testInfo.outputPath('user-management.png') })

  const configured = await page.request.put('/api/erp/config', {
    headers: await authenticatedHeaders(page.request),
    data: { accessToken: erpToken },
  })
  expect(configured.status()).toBe(204)
  const colleague = await page.request.post('/api/users', {
    headers: await authenticatedHeaders(page.request),
    data: { username: 'agent105', name: '授权代办员', password },
  })
  expect(colleague.status()).toBe(201)
  const applicant = await browser.newContext({ baseURL })
  const staff = await browser.newContext({ baseURL })
  try {
    const orders = await staff.newPage()
    const restricted = [
      '/api/users',
      '/api/erp/config',
      '/api/erp/order-sync-settings',
      '/api/erp/order-sync-settings/shops',
    ]
    const restrictedRequests: string[] = []
    orders.on('request', (request) => {
      const path = new URL(request.url()).pathname
      if (restricted.includes(path)) restrictedRequests.push(path)
    })
    const authorizer = await applicant.newPage()
    const applicantHeaders = await employeeLogin(authorizer, 'agent105', password)
    await expect(authorizer.getByTestId('employee-empty-hint')).toBeVisible()
    await authorizer.getByRole('link', { name: '前往授权店铺' }).click()
    await expect(authorizer.getByText(/尚未分配店铺/).first()).toBeVisible()
    await openAuthorization(authorizer, '授权新店')
    await authorizer.screenshot({ path: testInfo.outputPath('employee-empty-shops.png') })

    await page.goto('/shops')
    await expect(page.getByText('本地暂无店铺数据', { exact: false })).toBeVisible()
    await page.getByRole('button', { name: '立即同步', exact: true }).click()
    await expect(page.getByRole('row').filter({ hasText: '验收店铺' })).toContainText('无效')
    const orderUrl = '/api/orders?shopSid=30'
    expect((await applicant.request.get(orderUrl, { headers: applicantHeaders })).status()).toBe(
      403,
    )
    const headers = await employeeLogin(orders, 'bob', password)
    expect((await staff.request.get(orderUrl, { headers })).status()).toBe(403)

    await page.goto('/users')
    await renamed.getByRole('button', { name: /分配店铺/ }).click()
    const assignment = page.getByRole('dialog', { name: '分配店铺' })
    await assignment.getByRole('checkbox', { name: /验收店铺/ }).check()
    await page.screenshot({ path: testInfo.outputPath('employee-shop-assignment.png') })
    await assignment.getByRole('button', { name: '保存' }).click()
    await expect(assignment).toBeHidden()

    await orders.goto('/shops')
    await expect(orders.getByRole('row').filter({ hasText: '验收店铺' })).toContainText('无效')
    await openAuthorization(orders, '续授权')
    await orders.screenshot({ path: testInfo.outputPath('employee-shops.png') })
    const reportUrl = '/api/reports/order-snapshot'
    const adminReport = await page.request.get(`${reportUrl}?shops=wxw-erp%2FTemuPop%2F30`, {
      headers: await authenticatedHeaders(page.request),
    })
    expect(adminReport.ok()).toBe(true)
    const subtotal = await adminReport.json()
    const employeeReport = await staff.request.get(reportUrl, { headers })
    expect(employeeReport.ok()).toBe(true)
    const report = await employeeReport.json()
    expect(report.summary).toEqual({ orderCount: 1, itemQuantity: 5 })
    expect(report.summary).toEqual(subtotal.summary)
    expect(report.groups).toEqual(subtotal.groups)
    expect((await applicant.request.get(orderUrl, { headers: applicantHeaders })).status()).toBe(
      403,
    )
    await orders.goto('/orders')
    await expect(orders.getByRole('row').filter({ hasText: 'TM-72801-A' })).toBeVisible()
    await expect(orders.getByRole('row').filter({ hasText: 'TM-90021' })).toHaveCount(0)
    await orders.getByRole('button', { name: '同步历史订单', exact: true }).first().click()
    const panel = orders.getByRole('dialog', { name: '同步历史订单' })
    await expect(panel.getByRole('heading', { name: '定时同步' })).toHaveCount(0)
    const shop = panel.getByRole('row').filter({ hasText: '验收店铺' })
    await shop.getByRole('button').click()
    await expect(shop).toContainText('同步完成')
    await expect(shop).toContainText(/新增\s*1/)
    await orders.screenshot({ path: testInfo.outputPath('employee-order-sync.png') })
    await orders.keyboard.press('Escape')
    await expect(orders.getByRole('row').filter({ hasText: 'TM-BROWSER-SYNC-30' })).toBeVisible()

    await expect(orders.locator('[data-nav-page="users"]')).toHaveCount(0)
    await expect(orders.locator('[data-nav-page="business"]')).toHaveCount(0)
    await expect(orders.locator('[data-group="settings"]')).toHaveCount(0)
    await orders.goto('/users')
    await expect(orders.getByTestId('toast')).toContainText('该页面仅管理员可访问')
    expect((await staff.request.get('/api/erp/shop-sync', { headers })).status()).toBe(403)
    expect(restrictedRequests).toEqual([])
    for (const path of restricted) {
      expect((await staff.request.get(path, { headers })).status(), path).toBe(403)
    }

    await renamed.getByRole('button', { name: /删除/ }).click()
    await page
      .getByRole('dialog', { name: '删除用户' })
      .getByRole('button', { name: '确认删除' })
      .click()
    await expect(renamed).toHaveCount(0)
    await orders.reload()
    await expect(orders.getByRole('heading', { name: '登录 OpenERP' })).toBeVisible()
    expect((await staff.request.get('/api/orders', { headers })).status()).toBe(401)
  } finally {
    await Promise.all([staff.close(), applicant.close()])
  }
})
