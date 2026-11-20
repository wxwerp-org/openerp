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

    await test.step('Given 已登录的普通用户 When 从设置打开授权店铺与详情 Then 可读取状态且无授权入口', async () => {
      await orders.locator('[data-group="settings"] button[aria-expanded]').click()
      await orders.locator('[data-nav-page="shops"]').click()
      await expect(orders).toHaveURL(/\/shops$/)
      const table = orders.getByRole('table', { name: '授权店铺列表' })
      const row = table.getByRole('row').filter({ hasText: '验收店铺' })
      await expect(row.getByText('有效', { exact: true })).toBeVisible()
      await expect(row.getByRole('cell', { name: '尚未检查' })).toBeVisible()
      await expect(table.getByText('去 wxw 授权', { exact: true })).toHaveCount(0)
      await expect(orders.getByRole('button', { name: '立即同步' })).toBeEnabled()
      await orders.screenshot({ path: testInfo.outputPath('employee-shop-list.png') })

      await row.getByRole('button', { name: '详情' }).click()
      const details = orders.getByRole('dialog', { name: '验收店铺' })
      await expect(details.getByText('有效', { exact: true })).toBeVisible()
      await expect(details.getByText('去 wxw 授权', { exact: true })).toHaveCount(0)
      await orders.screenshot({ path: testInfo.outputPath('employee-shop-detail.png') })
      await details.getByRole('button', { name: '关闭', exact: true }).last().click()
      await expect(details).toBeHidden()
    })

    await test.step('When 点击立即同步 Then 授权状态和最后检查时间自动更新', async () => {
      const sync = orders.getByRole('button', { name: '立即同步' })
      await sync.click()
      const row = orders
        .getByRole('table', { name: '授权店铺列表' })
        .getByRole('row')
        .filter({ hasText: '验收店铺' })
      await expect(row.getByText('无效', { exact: true })).toBeVisible()
      await expect(row.getByRole('cell', { name: '尚未检查' })).toHaveCount(0)
      await expect(orders.getByText('刚刚', { exact: true })).toBeVisible()
      await expect(sync).toBeEnabled()
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
