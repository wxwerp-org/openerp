import { expect, test, type Page } from '@playwright/test'
import { adminPassword, signInForWorkspace } from './authentication'

// eslint-disable-next-line playwright/no-skipped-test -- 真实会话用例由隔离宿主提供初始密码。
test.skip(!adminPassword, '设置 OPENERP_TEST_ADMIN_PASSWORD 以使用真实管理员会话。')

test.beforeEach(async ({ page }) => {
  await signInForWorkspace(page.request)
})

async function mockShops(page: Page, count = 1): Promise<void> {
  await page.route('**/api/erp/config', (route) => route.fulfill({
    json: { configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: false },
  }))
  await page.route('**/api/erp/shops?**', (route) => route.fulfill({
    json: {
      items: Array.from({ length: count }, (_, index) => ({
        sid: String(index + 1),
        name: `测试店铺 ${index + 1}`,
        platform: 'Temu',
        region: 'US',
        isTokenOk: true,
        statusError: null,
      })),
      page: 1,
      pageSize: 20,
      totalCount: count,
      pageCount: 1,
    },
  }))
}

test('Given 原型工作台 When 打开首页并进入用户管理 Then 全部卡片和四条用户数据可见', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.goto('/')

  await expect(page.locator('.page-dashboard')).toBeVisible()
  await expect(page.locator('.rank-table tbody tr')).toHaveCount(5)
  await expect(page.locator('.trend-chart')).toBeVisible()
  await expect(page.locator('.donut-chart')).toBeVisible()

  await page.locator('[data-group="settings"] .nav-group-head').click()
  await page.locator('[data-nav-page="users"]').click()
  await expect(page).toHaveURL(/\/users$/)
  await expect(page.locator('#userTableBody tr')).toHaveCount(4)
  await expect(page.locator('.page-tab.active .tab-label')).toHaveText('用户管理')
})

test('Given 多平台配置 When 选择店铺设置并打开账户弹窗 Then 配置内容和焦点行为与原型一致', async ({ page }) => {
  await page.goto('/settings')
  await page.locator('[data-settings-group="multiplatform"]').click()
  await page.locator('[data-settings-item="店铺设置"]').click()

  await expect(page.locator('#settingsSecondTitle')).toHaveText('多平台')
  await expect(page.locator('#settingsContent')).toContainText('配置多平台模块下的店铺设置能力')

  await page.locator('.account-button').click()
  await page.locator('[data-account-action="profile"]').click()
  await expect(page.locator('#profileLayer')).toBeVisible()
  await expect(page.locator('#profileName')).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(page.locator('#profileLayer')).toBeHidden()
  await expect(page.locator('.account-button')).toBeFocused()

  await page.locator('.account-button').click()
  await page.locator('[data-account-action="password"]').click()
  await expect(page.locator('#oldPassword')).toBeFocused()
  await page.locator('#oldPassword').fill('old-password')
  await page.locator('#newPassword').fill('new-password')
  await page.locator('#confirmPassword').fill('different')
  await page.locator('#savePassword').click()
  await expect(page.getByText('两次输入的密码不一致')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.locator('#passwordLayer')).toBeHidden()
  await expect(page.locator('.account-button')).toBeFocused()
})

test('Given 产品和订单原型 When 打开表单与公告 Then 弹窗可见且桌面页面无横向溢出', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.goto('/products')
  await page.locator('#openProductForm').click()
  await expect(page.locator('#productFormLayer')).toBeVisible()
  await expect(page.locator('.form-nav-item')).toHaveCount(7)
  await page.locator('#saveProductForm').click()
  await expect(page.getByText('请输入产品名称')).toBeVisible()
  await page.locator('#closeProductForm').click()

  await page.goto('/orders')
  await page.locator('#openNotice').click()
  await expect(page.locator('#noticeLayer')).toBeVisible()
  await expect(page.locator('#noticeTitle')).toHaveText('【订单列表】优化公告')
  await page.locator('#closeNotice').click()

  await page.setViewportSize({ width: 1440, height: 900 })
  const hasPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)
  expect(hasPageOverflow).toBe(false)
})

test('Given 既有服务页面 When 应用共享视觉变量 Then 卡片边框色和圆角保持不变', async ({ page }) => {
  await mockShops(page)
  await page.goto('/shops')

  const shopsCard = page.locator('.shops-card')
  await expect(shopsCard).toBeVisible()
  await expect(shopsCard).toHaveCSS('border-top-color', 'rgb(231, 234, 240)')
  await expect(shopsCard).toHaveCSS('border-top-left-radius', '12px')

  await page.goto('/service-connection')
  const pingCard = page.locator('.ping-card')
  await expect(pingCard).toBeVisible()
  await expect(pingCard).toHaveCSS('border-top-color', 'rgb(231, 234, 240)')
  await expect(pingCard).toHaveCSS('border-top-left-radius', '14px')
})

test('Given 独立服务路由 When 从设置侧栏导航 Then 授权店铺和服务连接均可达', async ({ page }) => {
  await mockShops(page)
  await page.route('**/api/ping', (route) => route.fulfill({ json: { message: '连接正常' } }))
  await page.goto('/')
  await page.locator('[data-group="settings"] .nav-group-head').click()

  await page.locator('[data-nav-page="shops"]').click()
  await expect(page).toHaveURL(/\/shops$/)
  await expect(page.locator('.shops-card')).toBeVisible()
  await expect(page.locator('[data-nav-page="shops"]').first()).toHaveClass(/active/)

  await page.locator('[data-nav-page="service-connection"]').click()
  await expect(page).toHaveURL(/\/service-connection$/)
  await expect(page.locator('.ping-card')).toBeVisible()
  await expect(page.locator('[data-nav-page="service-connection"]').first()).toHaveClass(/active/)
})


test('Given 一页 20 家店铺 When 视口高度不足 Then 可通过纵向滚动访问分页栏', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 700 })
  await mockShops(page, 20)
  await page.goto('/shops')

  await expect(page.locator('.shops-table-wrap .el-table__body tr')).toHaveCount(20)
  const pagination = page.locator('.shops-pagination')
  await expect(pagination).not.toBeInViewport()
  await page.locator('.shops-page').hover()
  await page.mouse.wheel(0, 1400)
  await expect(pagination).toBeInViewport()
})
