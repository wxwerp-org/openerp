import { expect, test } from '@playwright/test'
import { enterWorkspace } from './authentication'

test.beforeEach(async ({ page }) => {
  await enterWorkspace(page)
  await page.route('**/api/hubs/erp/**', (route) => route.abort())
})

test('Given 多个已打开页签 When 悬停或键盘聚焦非活动页签 Then 关闭按钮可见且可关闭', async ({
  page,
}) => {
  await page.goto('/')
  const newdevTab = page.locator('[data-testid="page-tab"]').filter({ hasText: '新品开发' })
  const close = newdevTab.locator('[data-close-tab="newdev"]')

  await expect(close).toBeHidden()

  await newdevTab.hover()
  await expect(close).toBeVisible()

  await page.mouse.move(720, 400)
  await expect(close).toBeHidden()

  await newdevTab.locator('a').focus()
  await expect(close).toBeVisible()

  await close.click()
  await expect(newdevTab).toHaveCount(0)
})

test('Given 视口高度 700 When 展开或折叠导航 Then 短视口密度与旧版一致', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 700 })
  await page.goto('/')

  const sidebar = page.locator('[data-testid="sidebar"]')
  const groupHead = sidebar.locator('[data-group="sales"] button[aria-expanded]')

  await expect(groupHead).toHaveCSS('height', '36px')
  await expect(groupHead.locator('svg').first()).toHaveCSS('width', '16px')
  await expect(groupHead.locator('svg').last()).toHaveCSS('width', '16px')
  await expect(sidebar.locator('[data-group="product"] [data-nav-page="newdev"]')).toHaveCSS(
    'height',
    '28px',
  )

  await page.locator('[data-testid="collapse-nav"]').click()
  await expect(sidebar).toHaveAttribute('data-rail', 'true')

  const home = sidebar.locator('[data-testid="side-nav"] [data-nav-page="dashboard"]')
  await expect(home).toHaveCSS('height', '40px')
  await expect(home.locator('svg')).toHaveCSS('width', '16px')

  await groupHead.focus()
  const flyout = page.locator('[data-testid="nav-flyout"]')
  await expect(flyout).toBeVisible()
  await expect(flyout.locator('button').first()).toHaveCSS('height', '28px')
})
