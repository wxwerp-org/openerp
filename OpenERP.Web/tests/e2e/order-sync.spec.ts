import { expect, test, type Page } from '@playwright/test'
import { enterWorkspace } from './authentication'

async function prepareSync(page: Page) {
  await enterWorkspace(page)
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
        items: [{ sid: '1', name: '测试店铺 1', platform: 'TemuPop', region: 'US' }],
        page: 1,
        pageSize: 20,
        totalCount: 1,
        pageCount: 1,
      },
    }),
  )
  await page.goto('/orders')
  await page.getByRole('button', { name: '同步历史订单', exact: true }).first().press('Enter')
}

test('Given 开启定时同步 When 视口高度不足 Then 面板内可访问设置与店铺且关闭后回到查单', async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 600 })
  await page.route('**/api/hubs/erp/**', (route) => route.abort())
  await prepareSync(page)
  const panel = page.getByRole('dialog', { name: '同步历史订单' })
  const syncSwitch = panel.getByRole('checkbox', { name: '启用定时同步' })
  await syncSwitch.focus()
  await expect(syncSwitch).toBeFocused()
  await expect(syncSwitch).toHaveCSS('outline-color', 'rgb(0, 91, 245)')
  await expect(syncSwitch).toHaveCSS('outline-style', 'solid')
  await expect(syncSwitch).toHaveCSS('outline-width', '2px')
  const row = panel.getByRole('row').filter({ hasText: '测试店铺 1' })
  await row.scrollIntoViewIfNeeded()
  await expect(row).toBeInViewport()
  await expect(row.getByRole('button')).toBeDisabled()
  await page.screenshot({ path: testInfo.outputPath('order-sync-panel.png') })
  await page.keyboard.press('Escape')
  await expect(panel).toBeHidden()
  await expect(
    page.getByRole('button', { name: '同步历史订单', exact: true }).first(),
  ).toBeFocused()
})

test('Given ERP 凭证已被移除 When 在面板发起同步 Then Hub 返回原因且历史订单仍可查询', async ({
  page,
}) => {
  test.skip(
    process.env.OPENERP_TEST_LOCAL_ORDERS !== '1',
    '使用隔离宿主的真实 Hub 和本地样例订单。',
  )
  // 模拟已打开的面板仍显示旧配置，隔离服务实际没有配置上游凭证。
  await prepareSync(page)
  const panel = page.getByRole('dialog', { name: '同步历史订单' })
  const row = panel.getByRole('row').filter({ hasText: '测试店铺 1' })
  await row.getByRole('button').click()
  await expect(row.getByRole('alert')).toContainText('尚未配置私有 ERP AccessToken')
  await page.keyboard.press('Escape')
  await expect(page.getByRole('row').filter({ hasText: 'TM-72801-A' })).toBeVisible()
})
