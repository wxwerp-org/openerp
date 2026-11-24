import { expect, test, type Page } from '@playwright/test'
import { enterWorkspace } from './authentication'

const shopRows = (page: Page) =>
  page.getByRole('table', { name: '授权店铺列表' }).locator('tbody tr')

type Shop = {
  sid: string
  name: string
  platform: string
  region: string | null
  memberId: string | null
  isTokenOk: boolean | null
  tokenError: string | null
  syncedAt: string | null
}

const shops: Shop[] = [
  {
    sid: '42',
    name: '测试店铺 42',
    platform: 'TemuPop',
    region: 'US',
    memberId: 'member-42',
    isTokenOk: true,
    tokenError: null,
    syncedAt: new Date(Date.now() - 5 * 60_000).toISOString(),
  },
  {
    sid: '6',
    name: 'AE 测试店铺 6',
    platform: 'AliExpress',
    region: 'GB',
    memberId: null,
    isTokenOk: false,
    tokenError: null,
    syncedAt: new Date(Date.now() - 5 * 60_000).toISOString(),
  },
  {
    sid: '9',
    name: '未知店铺 9',
    platform: 'TemuPop',
    region: null,
    memberId: null,
    isTokenOk: null,
    tokenError: '状态读取失败',
    syncedAt: null,
  },
]

test.beforeEach(async ({ page }) => {
  await enterWorkspace(page)
  await page.route('**/api/hubs/erp/**', (route) => route.abort())
})

async function mockDirectory(
  page: Page,
  items: Shop[] = shops,
  totalCount = items.length,
): Promise<void> {
  await page.route('**/api/erp/order-sync-availability', (route) =>
    route.fulfill({
      json: { configured: true, orderSyncEnabled: false },
    }),
  )
  await page.route('**/api/erp/platforms', (route) =>
    route.fulfill({
      json: {
        items: [
          {
            name: 'TemuPop',
            displayName: 'Temu半托管',
            icon: 'icon-temupop',
            isProduction: true,
            shopCount: 2,
          },
          {
            name: 'AliExpress',
            displayName: '速卖通',
            icon: 'icon-aliexpress',
            isProduction: true,
            shopCount: 1,
          },
        ],
      },
    }),
  )
  await page.route('**/api/erp/shop-sync', (route) =>
    route.fulfill({
      json: {
        running: false,
        startedAt: null,
        completedAt: new Date(Date.now() - 15 * 60_000).toISOString(),
        total: totalCount,
        checked: totalCount,
        valid: 1,
        invalid: 1,
        unknown: 1,
        error: null,
        version: 1,
      },
    }),
  )
  await page.route('**/api/erp/shops?**', (route) => {
    const url = new URL(route.request().url())
    const status = url.searchParams.get('status')
    const platform = url.searchParams.get('platform')
    const keyword = url.searchParams.get('keyword')
    const currentPage = Number(url.searchParams.get('page') ?? 1)
    const pageSize = Number(url.searchParams.get('pageSize') ?? 20)
    const filtered = items.filter(
      (shop) =>
        (!status ||
          (status === 'valid' && shop.isTokenOk === true) ||
          (status === 'invalid' && shop.isTokenOk === false) ||
          (status === 'unknown' && shop.isTokenOk === null)) &&
        (!platform || shop.platform === platform) &&
        (!keyword || shop.name.includes(keyword) || shop.sid === keyword),
    )
    const pageCount = filtered.length === 0 ? 0 : Math.ceil(filtered.length / pageSize)
    const start = (Math.min(currentPage, Math.max(pageCount, 1)) - 1) * pageSize
    return route.fulfill({
      json: {
        items: filtered.slice(start, start + pageSize),
        page: Math.min(currentPage, Math.max(pageCount, 1)),
        pageSize,
        totalCount: filtered.length,
        pageCount,
        summary: {
          valid: 1,
          invalid: 1,
          unknown: 1,
          lastSyncedAt: new Date(Date.now() - 15 * 60_000).toISOString(),
          syncVersion: 1,
        },
      },
    })
  })
}

test('Given 缺少 ERP Token When 从授权店铺的提示进入设置 Then 可填写 Token 且刷新后仍在接入页', async ({
  page,
}) => {
  await mockDirectory(page)
  await page.route('**/api/erp/order-sync-availability', (route) =>
    route.fulfill({ json: { configured: false, orderSyncEnabled: false } }),
  )
  await page.route('**/api/erp/config', (route) =>
    route.fulfill({
      json: { configured: false, baseUrl: 'https://erp.test', orderSyncEnabled: false },
    }),
  )
  await page.goto('/shops')
  const notice = page.getByRole('alert').filter({ hasText: '尚未配置私有 ERP AccessToken' })
  await expect(notice).toContainText('系统设置 → 基础 → ERP 接入')
  const link = notice.getByRole('link')
  await expect(link).toHaveAccessibleName('前往 ERP 接入')
  await link.click()
  const token = page.getByLabel('私有 ERP AccessToken', { exact: true })
  await expect(token).toBeEditable()
  await page.reload()
  await expect(token).toBeEditable()
  await page.goBack()
  await expect(page.getByRole('heading', { name: '授权店铺', exact: true })).toBeVisible()
})

test('Given 本地店铺目录 When 打开授权页 Then 汇总、表格与最后同步可见且没有订单同步入口', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await mockDirectory(page)
  await page.goto('/shops')

  await expect(page.locator('[data-testid="shop-summary-card"]')).toHaveCount(4)
  await expect(page.locator('[data-testid="shop-summary-card"]').nth(0)).toContainText('1')
  await expect(page.locator('[data-testid="shop-summary-card"]').nth(1)).toContainText('1')
  await expect(page.locator('[data-testid="shop-summary-card"]').nth(2)).toContainText('1')
  await expect(page.locator('[data-testid="shop-summary-card"][data-static="true"]')).toContainText(
    '分钟前',
  )
  await expect(shopRows(page)).toHaveCount(3)
  await expect(shopRows(page).nth(0)).toContainText('测试店铺 42')
  await expect(shopRows(page).nth(0)).toContainText('Temu半托管')
  await expect(shopRows(page).nth(1)).toContainText('无效')
  await expect(shopRows(page).nth(2)).toContainText('状态读取失败')
  await expect(page.locator('#page-shops')).not.toContainText('同步历史订单')
  await expect(page.locator('#page-shops')).not.toContainText('定时同步')
  await expect(page.getByRole('table', { name: '授权店铺列表' })).toBeInViewport()
  await expect(page.getByRole('navigation', { name: '授权店铺分页' })).toBeInViewport()

  const hasOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth > window.innerWidth,
  )
  expect(hasOverflow).toBe(false)
})

test('Given 汇总卡 When 点击无效与未知 Then 列表按状态筛选且汇总保持不变', async ({ page }) => {
  await mockDirectory(page)
  await page.goto('/shops')

  await page.locator('[aria-label^="无效"]').click()
  await expect(page.locator('[aria-label^="无效"]')).toHaveAttribute('aria-pressed', 'true')
  await expect(shopRows(page)).toHaveCount(1)
  await expect(shopRows(page)).toContainText('AE 测试店铺 6')
  await expect(page.locator('[data-testid="shop-summary-card"]').nth(0)).toContainText('1')

  await page.locator('[aria-label^="未知"]').click()
  await expect(page.locator('[aria-label^="无效"]')).toHaveAttribute('aria-pressed', 'false')
  await expect(page.locator('[aria-label^="未知"]')).toHaveAttribute('aria-pressed', 'true')
  await expect(shopRows(page)).toHaveCount(1)
  await expect(shopRows(page)).toContainText('未知店铺 9')
})

test('Given 平台与关键词筛选 When 查询 Then 结果与清除筛选可用', async ({ page }) => {
  await mockDirectory(page)
  await page.goto('/shops')

  await page.getByLabel('平台筛选').selectOption('AliExpress')
  await expect(shopRows(page)).toHaveCount(1)
  await expect(shopRows(page)).toContainText('AE 测试店铺 6')

  await page.getByRole('button', { name: '清除筛选' }).click()
  await expect(shopRows(page)).toHaveCount(3)

  await page.getByLabel('搜索店铺名称或 SID').fill('42')
  await page.getByRole('button', { name: '搜索', exact: true }).click()
  await expect(shopRows(page)).toHaveCount(1)
  await expect(shopRows(page)).toContainText('测试店铺 42')
})

test('Given 管理员 When 查看店铺列表和详情 Then 两处均可打开 wxw 授权', async ({
  page,
  context,
}) => {
  await mockDirectory(page)
  const authorizationUrl = 'https://v3.wxwerp.com/system/authorize'
  await context.route('https://v3.wxwerp.com/**', (route) => route.fulfill({ body: 'wxw' }))
  await page.goto('/shops')

  const row = shopRows(page).nth(0)
  const rowPopupPromise = page.waitForEvent('popup')
  await row.getByRole('button', { name: '去 wxw 授权' }).click()
  const rowPopup = await rowPopupPromise
  await expect(rowPopup).toHaveURL(authorizationUrl)
  await rowPopup.close()

  await row.getByRole('button', { name: '详情' }).click()
  await expect(page.locator('#shopDetailLayer')).toBeVisible()
  // 旧弹窗打开后聚焦「去 wxw 授权」；迁移保留该初始焦点。
  await expect(page.locator('#shopAuthorize')).toBeFocused()
  await expect(page.locator('#shopDetailLayer')).toContainText('member-42')
  await expect(page.locator('#shopDetailLayer')).toContainText('wxw ERP（OpenERP 只读）')
  await expect(page.locator('#shopDetailLayer')).toContainText(
    '授权范围、有效期、授权历史由 wxw ERP 管理',
  )
  await expect(page.locator('#shopDetailLayer')).not.toContainText('授权历史记录')

  const popupPromise = page.waitForEvent('popup')
  await page.locator('#shopAuthorize').click()
  const popup = await popupPromise
  await expect(popup).toHaveURL(authorizationUrl)
  await popup.close()

  await page.keyboard.press('Escape')
  await expect(page.locator('#shopDetailLayer')).toBeHidden()
})

test('Given 实时连接断开 When 打开 Then 本地列表可读且同步按钮禁用', async ({ page }) => {
  await mockDirectory(page)
  await page.goto('/shops')

  await expect(page.locator('[data-testid="hub-notice"]')).toContainText('实时连接已断开')
  await expect(shopRows(page)).toHaveCount(3)
  await expect(page.getByRole('button', { name: '立即同步' })).toBeDisabled()
})

test('Given 本地没有店铺 When 打开 Then 显示同步空态', async ({ page }) => {
  await mockDirectory(page, [], 0)
  await page.goto('/shops')

  await expect(page.locator('#page-shops')).toContainText('本地暂无店铺数据')
  await expect(page.getByRole('button', { name: '立即同步' })).toBeDisabled()
})
