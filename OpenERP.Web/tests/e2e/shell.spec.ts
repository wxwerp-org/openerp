import { expect, test, type Page } from '@playwright/test'
import { enterWorkspace } from './authentication'

const tabs = (page: Page) => page.locator('[data-testid="page-tab"]')

test.beforeEach(async ({ page }) => {
  await enterWorkspace(page)
  await page.route('**/api/hubs/erp/**', (route) => route.abort())
})

test('Given 默认只打开工作台页签 When 导航到用户管理 Then 追加可关闭页签且移除已删导航条目', async ({
  page,
}) => {
  await page.goto('/')

  // 顶部页签默认只开「工作台」，其余按需追加。
  await expect(tabs(page)).toHaveCount(1)
  await expect(tabs(page).first()).toContainText('工作台')
  await expect(tabs(page).locator('[data-close-tab]')).toHaveCount(0)

  await page.locator('[data-testid="side-nav"] [data-nav-page="users"]').click()
  await expect(page).toHaveURL(/\/users$/)
  await expect(tabs(page)).toHaveCount(2)

  // 回到工作台让用户管理页签变为非活动页签：只有悬停或键盘聚焦时才显示关闭按钮。
  await page.locator('[data-testid="side-nav"] [data-nav-page="dashboard"]').click()
  await expect(page).toHaveURL(/\/$/)
  const usersTab = tabs(page).filter({ hasText: '用户管理' })
  const close = usersTab.locator('[data-close-tab="users"]')

  await expect(close).toBeHidden()

  await usersTab.hover()
  await expect(close).toBeVisible()

  await page.mouse.move(720, 400)
  await expect(close).toBeHidden()

  await usersTab.locator('a').focus()
  await expect(close).toBeVisible()

  await close.click()
  await expect(usersTab).toHaveCount(0)
  await expect(page).toHaveURL(/\/$/)
  await expect(tabs(page)).toHaveCount(1)

  // 已删页面不再出现在导航里；订单管理分组保留订单列表与商品量报表。
  const sidebar = page.locator('[data-testid="sidebar"]')
  await expect(sidebar.locator('[data-nav-page="orders"]')).toBeVisible()
  await expect(sidebar.locator('[data-nav-page="order-report"]')).toBeVisible()
  await expect(sidebar.getByText('新品开发')).toHaveCount(0)
  await expect(sidebar.getByText('产品管理')).toHaveCount(0)
  await expect(sidebar.getByText('服务连接')).toHaveCount(0)
})

test('Given 视口高度 700 When 展开或折叠导航 Then 短视口密度与旧版一致', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 700 })
  await page.goto('/')

  const sidebar = page.locator('[data-testid="sidebar"]')
  const groupHead = sidebar.locator('[data-group="orders"] button[aria-expanded]')

  await expect(groupHead).toHaveCSS('height', '36px')
  await expect(groupHead.locator('svg').first()).toHaveCSS('width', '16px')
  await expect(groupHead.locator('svg').last()).toHaveCSS('width', '16px')
  await expect(sidebar.locator('[data-group="orders"] [data-nav-page="orders"]')).toHaveCSS(
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
