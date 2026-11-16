import { expect, test, type Page } from '@playwright/test'
import { enterWorkspace } from './authentication'

test.beforeEach(async ({ page }) => {
  await enterWorkspace(page)
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

async function mockPagedShops(page: Page, totalCount = 45): Promise<void> {
  await page.route('**/api/erp/config', (route) => route.fulfill({
    json: { configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: false },
  }))
  await page.route('**/api/erp/shops?**', (route) => {
    const url = new URL(route.request().url())
    const currentPage = Number(url.searchParams.get('page'))
    const pageSize = Number(url.searchParams.get('pageSize'))
    const pageCount = Math.ceil(totalCount / pageSize)
    const start = (currentPage - 1) * pageSize
    const count = Math.max(0, Math.min(pageSize, totalCount - start))
    return route.fulfill({
      json: {
        items: Array.from({ length: count }, (_, index) => ({
          sid: String(start + index + 1),
          name: `分页店铺 ${start + index + 1}`,
          platform: 'Temu',
          region: 'US',
          isTokenOk: true,
          statusError: null,
        })),
        page: currentPage,
        pageSize,
        totalCount,
        pageCount,
      },
    })
  })
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
  await expect(page.locator('.notice-link')).toBeVisible()
  await page.locator('#closeNotice').click()

  await page.setViewportSize({ width: 1440, height: 900 })
  const hasPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)
  expect(hasPageOverflow).toBe(false)
})

test('Given 服务页面 When 应用共享设计体系 Then 使用 6px 卡片圆角、--line 边框与 18px 标签', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await mockShops(page)
  await page.goto('/shops')

  const shopsCard = page.locator('.page-shops .list-toolbar')
  await expect(shopsCard).toBeVisible()
  await expect(shopsCard).toHaveCSS('border-top-color', 'rgb(230, 232, 235)')
  await expect(shopsCard).toHaveCSS('border-top-left-radius', '6px')
  await expect(page.locator('.page-shops .table-box')).toHaveCSS('border-bottom-left-radius', '6px')

  const shopsTitle = page.locator('.page-shops h1')
  await expect(shopsTitle).toHaveCSS('font-size', '20px')
  await expect(shopsTitle).toHaveCSS('font-weight', '600')
  await expect(page.locator('.page-shops .tag').first()).toHaveCSS('height', '18px')
  await expect(page.getByRole('button', { name: '刷新' })).toBeVisible()
  await expect(page.locator('[class^="el-"], [class*=" el-"]')).toHaveCount(0)

  await page.route('**/api/ping', (route) => route.fulfill({ json: { message: '连接正常' } }))
  await page.goto('/service-connection')

  const panel = page.locator('.page-service .service-panel')
  await expect(panel).toBeVisible()
  await expect(panel).toHaveCSS('border-top-color', 'rgb(230, 232, 235)')
  await expect(panel).toHaveCSS('border-top-left-radius', '6px')
  await expect(page.locator('.page-service h1')).toHaveCSS('font-size', '20px')
  await expect(page.locator('.page-service .btn.primary')).toHaveCSS('height', '30px')
  await expect(page.locator('.page-service .tag')).toHaveCSS('height', '18px')
  await expect(page.locator('[class^="el-"], [class*=" el-"]')).toHaveCount(0)

  const hasPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)
  expect(hasPageOverflow).toBe(false)

  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.reload()
  await expect(page.locator('.page-service .service-panel')).toBeVisible()
  const hasWideOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)
  expect(hasWideOverflow).toBe(false)

  await page.goto('/shops')
  await expect(page.locator('.page-shops .grid-table')).toBeVisible()
  const hasWideShopsOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)
  expect(hasWideShopsOverflow).toBe(false)
})

test('Given 独立服务路由 When 从设置侧栏导航 Then 授权店铺和服务连接均可达并已渲染', async ({ page }) => {
  await mockShops(page)
  await page.route('**/api/ping', (route) => route.fulfill({ json: { message: '连接正常' } }))
  await page.goto('/')
  await page.locator('[data-group="settings"] .nav-group-head').click()

  await page.locator('[data-nav-page="shops"]').click()
  await expect(page).toHaveURL(/\/shops$/)
  await expect(page.locator('.page-shops .grid-table')).toBeVisible()
  await expect(page.locator('[data-nav-page="shops"]').first()).toHaveClass(/active/)

  await page.locator('[data-nav-page="service-connection"]').click()
  await expect(page).toHaveURL(/\/service-connection$/)
  await expect(page.locator('.page-service .service-panel')).toBeVisible()
  await expect(page.locator('[data-nav-page="service-connection"]').first()).toHaveClass(/active/)
})

test('Given 一页 20 家店铺 When 视口高度不足 Then 分页栏固定可见且表格体可滚动到最后一行', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 700 })
  await mockShops(page, 20)
  await page.goto('/shops')

  await expect(page.locator('.page-shops tbody tr')).toHaveCount(20)
  const pagination = page.locator('.table-pagination')
  await expect(pagination).toBeInViewport()

  const scroll = page.locator('.table-scroll')
  const metrics = await scroll.evaluate((element) => ({
    scrollHeight: element.scrollHeight,
    clientHeight: element.clientHeight,
  }))
  expect(metrics.scrollHeight).toBeGreaterThan(metrics.clientHeight)

  await scroll.hover()
  await page.mouse.wheel(0, 4000)
  await expect(page.locator('.page-shops tbody tr').last()).toBeInViewport()
  await expect(pagination).toBeInViewport()
})

test('Given 开启定时同步 When 视口高度不足 Then 页面可滚动访问表格与分页', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 600 })
  await page.route('**/api/erp/config', (route) => route.fulfill({
    json: { configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: true },
  }))
  await page.route('**/api/erp/order-sync-settings', (route) => route.fulfill({
    json: { enabled: true, shopSids: [1], intervalMinutes: 30 },
  }))
  await page.route('**/api/erp/order-sync-settings/shops', (route) => route.fulfill({
    json: { items: [{ sid: '1', name: '测试店铺 1' }] },
  }))
  await page.route('**/api/erp/shops?**', (route) => route.fulfill({
    json: {
      items: [{ sid: '1', name: '测试店铺 1', platform: 'Temu', region: 'US', isTokenOk: true, statusError: null }],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      pageCount: 1,
    },
  }))
  await page.route('**/api/erp/shops/1/order-sync', (route) => route.fulfill({ status: 204 }))
  await page.goto('/shops')

  await expect(page.locator('.sync-settings-card')).toBeVisible()
  await page.locator('.page-shops').evaluate((element) => element.scrollTo(0, element.scrollHeight))
  await expect(page.locator('.page-shops tbody tr').last()).toBeInViewport()
  await expect(page.locator('.table-pagination')).toBeInViewport()
})

test('Given 多页店铺 When 使用完整分页器 Then 页码、每页条数与跳页按新参数请求', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await mockPagedShops(page, 45)
  await page.goto('/shops')

  await expect(page.locator('.page-shops tbody tr')).toHaveCount(20)
  await expect(page.locator('.page-num')).toHaveText('1')
  await expect(page.locator('.page-shops tbody tr').first()).toContainText('分页店铺 1')

  await page.getByRole('button', { name: '下一页' }).click()
  await expect(page.locator('.page-num')).toHaveText('2')
  await expect(page.locator('.page-shops tbody tr').first()).toContainText('分页店铺 21')

  await page.locator('.page-size select').selectOption('50')
  await expect(page.locator('.page-num')).toHaveText('1')
  await expect(page.locator('.page-shops tbody tr')).toHaveCount(45)

  await page.locator('.page-size select').selectOption('20')
  await expect(page.locator('.page-shops tbody tr')).toHaveCount(20)

  const jump = page.locator('.page-jump input')
  await jump.fill('9')
  await jump.press('Enter')
  await expect(page.locator('.page-num')).toHaveText('3')
  await expect(page.locator('.page-shops tbody tr').first()).toContainText('分页店铺 41')

  await jump.fill('0')
  await jump.press('Enter')
  await expect(page.locator('.page-num')).toHaveText('1')
  await expect(page.locator('.page-shops tbody tr').first()).toContainText('分页店铺 1')

  await jump.fill('99')
  await jump.press('Enter')
  await expect(page.locator('.page-num')).toHaveText('3')
})

test('Given 店铺列表返回长错误详情 When 加载失败 Then 提示条完整换行且页面无横向溢出', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.route('**/api/erp/config', (route) => route.fulfill({
    json: { configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: false },
  }))
  const longDetail = 'problemdetail'.repeat(60)
  await page.route('**/api/erp/shops?**', (route) => route.fulfill({
    status: 503,
    json: { title: 'ERP 服务不可用', detail: longDetail },
  }))
  await page.goto('/shops')

  const notice = page.locator('.page-shops .notice.red')
  await expect(notice).toBeVisible()
  await expect(notice).toHaveAttribute('role', 'alert')
  await expect(notice).toContainText(longDetail)

  const metrics = await notice.evaluate((element) => ({
    scrollWidth: element.scrollWidth,
    clientWidth: element.clientWidth,
  }))
  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth)
  const hasPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)
  expect(hasPageOverflow).toBe(false)
})

test('Given 服务连接 When 检测成功或失败 Then 显示对应状态与既有文案', async ({ page }) => {
  let fail = false
  await page.route('**/api/ping', (route) => fail
    ? route.fulfill({ status: 500, body: 'boom' })
    : route.fulfill({ json: { message: 'pong' } }))

  await page.goto('/service-connection')
  await expect(page.locator('.page-service .tag')).toHaveText('连接成功')
  await expect(page.locator('.page-service .service-message')).toHaveText('后端响应：pong')

  fail = true
  await page.getByRole('button', { name: '重试' }).click()
  await expect(page.locator('.page-service .tag')).toHaveText('连接失败')
  await expect(page.locator('.page-service .service-message.is-error')).toContainText('服务器返回 HTTP 500')
  await expect(page.getByRole('button', { name: '重试' })).toBeEnabled()
})
