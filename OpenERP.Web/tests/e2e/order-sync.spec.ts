import { expect, test } from '@playwright/test'
import { enterWorkspace } from './authentication'

test.beforeEach(async ({ page }) => {
  await enterWorkspace(page)
  await page.route('**/api/hubs/erp/**', (route) => route.abort())
})

test('Given 开启定时同步 When 视口高度不足 Then 页面可滚动访问表格与分页', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 600 })
  await page.route('**/api/erp/config', (route) =>
    route.fulfill({
      json: { configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: true },
    }),
  )
  await page.route('**/api/erp/order-sync-settings', (route) =>
    route.fulfill({
      json: { enabled: true, shopSids: [1], intervalMinutes: 30 },
    }),
  )
  await page.route('**/api/erp/order-sync-settings/shops', (route) =>
    route.fulfill({
      json: { items: [{ sid: '1', name: '测试店铺 1' }] },
    }),
  )
  await page.route('**/api/erp/shops?**', (route) =>
    route.fulfill({
      json: {
        items: [{ sid: '1', name: '测试店铺 1', platform: 'Temu', region: 'US' }],
        page: 1,
        pageSize: 20,
        totalCount: 1,
        pageCount: 1,
      },
    }),
  )
  await page.route('**/api/erp/shops/1/order-sync', (route) => route.fulfill({ status: 204 }))
  await page.goto('/orders/sync')

  const syncSwitch = page.getByRole('checkbox', { name: '启用定时同步' })
  await syncSwitch.focus()
  await expect(syncSwitch).toBeFocused()
  await expect(syncSwitch).toHaveCSS('outline-color', 'rgb(0, 91, 245)')
  await expect(syncSwitch).toHaveCSS('outline-style', 'solid')
  await expect(syncSwitch).toHaveCSS('outline-width', '2px')

  const syncCard = page.locator('.sync-settings-card')
  await expect(syncCard).toBeVisible()
  await expect(syncCard).toHaveCSS('border-top-width', '0px')
  await expect(syncCard).toHaveCSS('border-top-left-radius', '6px')
  const syncRows = syncCard.locator('.sync-settings-card__row')
  await expect(syncRows.first()).toHaveCSS('display', 'grid')
  await expect(syncRows.first()).toHaveCSS('grid-template-columns', '240px 912px')
  await expect(syncRows.last()).toHaveCSS('grid-template-columns', '240px 912px')
  await expect(syncCard.locator('.sync-settings-card__footer')).toHaveCSS('margin-left', '264px')

  await page.locator('.page-shops').evaluate((element) => element.scrollTo(0, element.scrollHeight))
  await expect(page.locator('.page-shops tbody tr').last()).toBeInViewport()
  await expect(page.locator('.table-pagination')).toBeInViewport()
})
