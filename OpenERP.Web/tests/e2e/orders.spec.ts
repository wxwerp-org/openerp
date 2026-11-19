import { expect, test, type Page } from '@playwright/test'
import { enterWorkspace } from './authentication'

// 独立前端验收使用固定响应；Aspire 夹具已把同一笔样例保存到隔离 PostgreSQL。
async function prepareWorkspace(page: Page) {
  await enterWorkspace(page)
  if (process.env.OPENERP_TEST_LOCAL_ORDERS === '1') return

  const item = {
    id: 'browser-local-order',
    sid: 30,
    shopName: null,
    platform: 'TemuPop',
    platformOrders: [
      { number: 'TM-72801-A', status: '已发货', matched: false },
      { number: 'TM-72801-B', status: '待发货', matched: false },
    ],
    created: '2026-11-17T08:30:00',
    dispatched: null,
    lineCount: 2,
    itemQuantity: 5,
  }
  const options = {
    shops: [{ sid: 30, name: null }],
    platforms: ['TemuPop'],
    statuses: ['待发货', '已发货'],
  }
  await page.route(/\/api\/orders(?:\?|$)/, async (route) => {
    const params = new URL(route.request().url()).searchParams
    const number = params.get('number')
    let items = [item]
    if (number === '72801-A') {
      items =
        params.get('status') === '待发货'
          ? []
          : [
              {
                ...item,
                platformOrders: [
                  { ...item.platformOrders[0], matched: true },
                  item.platformOrders[1],
                ],
              },
            ]
    }
    await route.fulfill({ json: { items, options, total: items.length, page: 1, pageSize: 20 } })
  })
}

test('Given 同一主单有两个平台订单 When 按号码和状态查单 Then 保留整单数量且过滤不匹配的订单', async ({
  page,
}, testInfo) => {
  await prepareWorkspace(page)
  await page.goto('/orders')

  const row = page.getByRole('row').filter({ hasText: 'TM-72801-A' })
  await expect(row).toHaveCount(1)
  await expect(row).toContainText('TM-72801-B')
  await expect(row).toContainText('已发货')
  await expect(row).toContainText('待发货')
  await expect(row.getByRole('cell', { name: '5', exact: true })).toBeVisible()
  await page.getByLabel('平台订单号', { exact: true }).fill('72801-A')
  await page.getByRole('button', { name: '搜索', exact: true }).click()
  await expect(row.locator('mark')).toHaveText(/TM-72801-A.*已发货/)
  await expect(row).toContainText('TM-72801-B')
  await page.screenshot({ path: testInfo.outputPath('orders.png') })

  await page.getByRole('combobox', { name: /^订单状态/ }).selectOption({ label: '待发货' })
  await page.getByRole('button', { name: '搜索', exact: true }).click()
  await expect(page.getByText('未找到符合筛选条件的订单')).toBeVisible()
  await expect(row).toHaveCount(0)

  await page.getByRole('button', { name: '重置筛选', exact: true }).click()
  await expect(row).toBeVisible()
})
