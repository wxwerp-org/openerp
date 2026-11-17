import { expect, test } from '@playwright/test'
import { enterWorkspace } from './authentication'

// 浏览器截图使用固定接口样本，真实业务数据由后端验收覆盖。
const sampleShop = {
  source: 'wxw-erp',
  platform: 'TemuPop',
  shopId: '30',
  key: 'wxw-erp/TemuPop/30',
  label: '店铺 30',
}

const reportSample = {
  generatedAtUtc: '2026-11-17T01:02:03Z',
  summary: { orderCount: 1, itemQuantity: 5 },
  platforms: ['TemuPop'],
  shops: [sampleShop],
  groups: [{ shop: sampleShop, orderCount: 1, itemQuantity: 5 }],
  orders: [{
    id: 'sample-order-30',
    number: 'ERP-ORDER-30',
    shop: sampleShop,
    status: '已发货',
    platformOrders: [{ number: 'PO-ORDER-30', statusCode: 'SHIPPED', statusText: '已发货' }],
    items: [{
      lineId: 'line-30',
      productId: 'product-30',
      skuId: 'sku-30',
      skuCode: 'SKU-30',
      quantity: 5,
      disabled: true,
      appended: true,
    }],
    itemQuantity: 5,
  }],
  page: 1,
  pageSize: 20,
  pageCount: 1,
}

test.beforeEach(async ({ page }) => {
  await enterWorkspace(page)
})

test('Given 商品量报表有平台店铺和订单样本 When 从统计导航打开并展开订单 Then 展示筛选、快照口径与完整商品行', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 1100 })
  const requests: URL[] = []
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    requests.push(new URL(route.request().url()))
    await route.fulfill({ json: reportSample })
  })

  await page.goto('/')
  await page.locator('[data-group="stats"] .nav-group-head').click()
  await page.locator('[data-nav-page="order-report"]').click()
  await expect(page).toHaveURL(/\/reports\/orders$/)
  await expect(page.locator('.page-order-snapshot-report')).toBeVisible()
  await expect(page.locator('.page-tab.active .tab-label')).toHaveText('商品量报表')
  await expect(page.getByText('按本地来源订单计数；件数为当前商品行数量合计，包含禁用/追加行。')).toBeVisible()
  await expect(page.locator('[aria-label="订单总数"]')).toHaveText('1')
  await expect(page.locator('[aria-label="商品件数"]')).toHaveText('5')

  await page.getByLabel('平台筛选').selectOption('TemuPop')
  await page.locator('summary[aria-label="店铺筛选"]').click()
  await page.locator('input[value="wxw-erp/TemuPop/30"]').check()
  const filteredRequest = requests.at(-1)!
  expect(filteredRequest.searchParams.get('platform')).toBe('TemuPop')
  expect(filteredRequest.searchParams.getAll('shops')).toEqual(['wxw-erp/TemuPop/30'])
  await page.locator('summary[aria-label="店铺筛选"]').click()

  await page.getByRole('button', { name: '展开订单 ERP-ORDER-30 商品明细' }).click()
  const expanded = page.locator('.report-order-details')
  await expect(expanded.getByText('PO-ORDER-30')).toBeVisible()
  await expect(expanded.getByText('SKU-30')).toBeVisible()
  await expect(expanded.getByText('禁用')).toBeVisible()
  await expect(expanded.getByText('追加')).toBeVisible()
  await expect(page.getByText('本地订单件数 5')).toBeVisible()

  await page.screenshot({ path: testInfo.outputPath('order-report-mock-sample.png'), fullPage: true })
})
