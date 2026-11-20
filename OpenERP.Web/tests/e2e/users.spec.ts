import { randomBytes } from 'node:crypto'
import { expect, test } from '@playwright/test'
import { authenticatedHeaders, enterWorkspace } from './authentication'

test('Given 管理员创建并改名的员工 When 同步历史订单和店铺 Then 看到更新且没有管理或授权入口', async ({
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
    await orders.goto('/orders')
    await orders.getByLabel('用户名').fill('bob')
    await orders.getByLabel('密码', { exact: true }).fill(password)
    const login = orders.waitForResponse((response) => response.url().endsWith('/api/auth/login'))
    await orders.getByRole('button', { name: '登录', exact: true }).click()
    const { accessToken } = await (await login).json()
    const headers = { Authorization: `Bearer ${accessToken}` }
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

    await test.step('Given 已登录的普通用户 When 查看导航并直达管理路由 Then 无入口且被拒绝', async () => {
      await expect(orders.locator('[data-nav-page="shops"]')).toHaveCount(0)
      await expect(orders.locator('[data-nav-page="users"]')).toHaveCount(0)
      await expect(orders.locator('[data-nav-page="business"]')).toHaveCount(0)
      await expect(orders.locator('[data-group="settings"]')).toHaveCount(0)
      await expect(orders.locator('[data-group="shops"]')).toHaveCount(0)

      await orders.goto('/shops')
      await expect(orders).toHaveURL(/\/(\?denied=1)?$/)
      await expect(orders.getByTestId('toast')).toContainText('该页面仅管理员可访问')
      await expect(orders.getByRole('table', { name: '授权店铺列表' })).toHaveCount(0)
      await orders.screenshot({ path: testInfo.outputPath('employee-shop-denied.png') })

      // 员工仍可用最小投影选店（订单同步弹窗已在上一步验证），但不能读取授权/同步状态。
      const shops = await staff.request.get('/api/erp/shops?page=1&pageSize=20', { headers })
      expect(shops.status()).toBe(200)
      const shopBody = (await shops.json()) as {
        items: Record<string, unknown>[]
        summary?: unknown
      }
      expect(shopBody.summary).toBeUndefined()
      expect(Object.keys(shopBody.items[0] ?? {}).sort()).toEqual([
        'name',
        'platform',
        'region',
        'sid',
      ])
      expect((await staff.request.get('/api/erp/shops?status=valid', { headers })).status()).toBe(
        403,
      )
      expect((await staff.request.get('/api/erp/shop-sync', { headers })).status()).toBe(403)
    })

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
    await staff.close()
  }
})
