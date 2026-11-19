import { expect, test, type Page } from '@playwright/test'
import { enterWorkspace } from './authentication'

const shopsTable = (page: Page) => page.getByRole('table', { name: '授权店铺列表' })
const shopRows = (page: Page) => shopsTable(page).locator('tbody tr')
const shopPagerCurrent = (page: Page) =>
  page.getByRole('navigation', { name: '授权店铺分页' }).locator('[aria-current="page"]')

test.beforeEach(async ({ page }) => {
  await enterWorkspace(page)
  await mockHub(page)
})

async function mockShops(page: Page, count = 1): Promise<void> {
  await page.route('**/api/erp/config', (route) =>
    route.fulfill({
      json: { configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: false },
    }),
  )
  await page.route('**/api/erp/platforms', (route) =>
    route.fulfill({
      json: {
        items: [
          {
            name: 'Temu',
            displayName: 'Temu半托管',
            icon: 'icon-temupop',
            isProduction: true,
            shopCount: count,
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
        total: count,
        checked: count,
        valid: count,
        invalid: 0,
        unknown: 0,
        error: null,
        version: 1,
      },
    }),
  )
  await page.route('**/api/erp/shops?**', (route) =>
    route.fulfill({
      json: {
        items: Array.from({ length: count }, (_, index) => ({
          sid: String(index + 1),
          name: `测试店铺 ${index + 1}`,
          platform: 'Temu',
          region: 'US',
          memberId: `member-${index + 1}`,
          isTokenOk: true,
          tokenError: null,
          syncedAt: new Date(Date.now() - 5 * 60_000).toISOString(),
        })),
        page: 1,
        pageSize: 20,
        totalCount: count,
        pageCount: count > 0 ? 1 : 0,
        summary: {
          valid: count,
          invalid: 0,
          unknown: 0,
          lastSyncedAt: new Date(Date.now() - 15 * 60_000).toISOString(),
          syncVersion: 1,
        },
      },
    }),
  )
}

async function mockPagedShops(page: Page, totalCount = 45): Promise<void> {
  await page.route('**/api/erp/config', (route) =>
    route.fulfill({
      json: { configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: false },
    }),
  )
  await page.route('**/api/erp/platforms', (route) =>
    route.fulfill({
      json: {
        items: [
          {
            name: 'Temu',
            displayName: 'Temu半托管',
            icon: 'icon-temupop',
            isProduction: true,
            shopCount: totalCount,
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
        valid: totalCount,
        invalid: 0,
        unknown: 0,
        error: null,
        version: 1,
      },
    }),
  )
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
          memberId: null,
          isTokenOk: true,
          tokenError: null,
          syncedAt: new Date(Date.now() - 5 * 60_000).toISOString(),
        })),
        page: currentPage,
        pageSize,
        totalCount,
        pageCount,
        summary: {
          valid: totalCount,
          invalid: 0,
          unknown: 0,
          lastSyncedAt: new Date(Date.now() - 15 * 60_000).toISOString(),
          syncVersion: 1,
        },
      },
    })
  })
}

async function mockHub(page: Page): Promise<void> {
  await page.route('**/api/hubs/erp/**', (route) => route.abort())
}

test('Given 原型工作台 When 打开首页并进入用户管理 Then 全部卡片和四条用户数据可见', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.goto('/')

  await expect(page.locator('.page-dashboard')).toBeVisible()
  await expect(page.locator('.rank-table tbody tr')).toHaveCount(5)
  await expect(page.locator('.trend-chart')).toBeVisible()
  await expect(page.locator('.donut-chart')).toBeVisible()

  await page.locator('[data-group="settings"] button[aria-expanded]').click()
  await page.locator('[data-nav-page="users"]').click()
  await expect(page).toHaveURL(/\/users$/)
  await expect(page.locator('#userTableBody tr')).toHaveCount(4)
  await expect(page.locator('[data-testid="page-tab"] [aria-current="page"]')).toHaveText(
    '用户管理',
  )
})

test('Given 多平台配置 When 选择店铺设置并打开账户弹窗 Then 配置内容和焦点行为与原型一致', async ({
  page,
}) => {
  await page.goto('/settings')
  await page.locator('[data-settings-group="multiplatform"]').click()
  await page.locator('[data-settings-item="店铺设置"]').click()

  await expect(page.locator('#settingsSecondTitle')).toHaveText('多平台')
  await expect(page.locator('#settingsContent')).toContainText('配置多平台模块下的店铺设置能力')

  await page.locator('[data-testid="account-button"]').click()
  await page.locator('[data-account-action="profile"]').click()
  await expect(page.locator('#profileLayer')).toBeVisible()
  await expect(page.locator('#profileName')).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(page.locator('#profileLayer')).toBeHidden()
  await expect(page.locator('[data-testid="account-button"]')).toBeFocused()

  await page.locator('[data-testid="account-button"]').click()
  await page.locator('[data-account-action="password"]').click()
  await expect(page.locator('#oldPassword')).toBeFocused()
  await page.locator('#oldPassword').fill('old-password')
  await page.locator('#newPassword').fill('new-password')
  await page.locator('#confirmPassword').fill('different')
  await page.locator('#savePassword').click()
  await expect(page.getByText('两次输入的密码不一致')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.locator('#passwordLayer')).toBeHidden()
  await expect(page.locator('[data-testid="account-button"]')).toBeFocused()
})

test('Given 产品和订单原型 When 打开表单与公告 Then 弹窗可见且桌面页面无横向溢出', async ({
  page,
}) => {
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
  const hasPageOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth > window.innerWidth,
  )
  expect(hasPageOverflow).toBe(false)
})

test('Given 桌面端服务页面 When 沿用 Dashboard 卡片语言 Then 页标题清晰且卡片字阶统一', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await mockShops(page)
  await page.goto('/shops')

  const shopsHeader = page.locator('[data-testid="shops-head"]')
  await expect(shopsHeader).toHaveCSS('display', 'flex')
  await expect(shopsHeader).toHaveCSS('height', '62px')
  await expect(shopsHeader).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)')
  await expect(shopsHeader).toHaveCSS('box-shadow', 'none')

  const shopsCard = page.locator('[data-testid="shops-card"]')
  await expect(shopsCard).toBeVisible()
  await expect(shopsCard).toHaveCSS('border-top-width', '1px')
  await expect(shopsCard).toHaveCSS('border-top-left-radius', '6px')
  await expect(shopsCard).toHaveCSS('border-bottom-left-radius', '6px')
  await expect(shopsTable(page).getByRole('columnheader').first()).toHaveCSS(
    'background-color',
    'rgb(242, 245, 250)',
  )

  const shopsTitle = page.getByRole('heading', { name: '授权店铺' })
  await expect(shopsTitle).toHaveCSS('font-size', '20px')
  await expect(shopsTitle).toHaveCSS('font-weight', '600')
  await expect(page.locator('[data-testid="upstream-tag"]')).toHaveCSS('height', '18px')
  await expect(page.getByRole('button', { name: '刷新本地数据' })).toBeVisible()
  await expect(page.locator('[data-testid="shop-summary-card"]')).toHaveCount(4)
  await expect(page.locator('[class^="el-"], [class*=" el-"]')).toHaveCount(0)

  await page.route('**/api/ping', (route) => route.fulfill({ json: { message: '连接正常' } }))
  await page.goto('/service-connection')

  const serviceHeader = page.locator('[data-testid="service-head"]')
  await expect(serviceHeader).toHaveCSS('display', 'block')
  await expect(serviceHeader).toHaveCSS('height', '62px')
  await expect(serviceHeader).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)')
  await expect(serviceHeader).toHaveCSS('box-shadow', 'none')

  const panel = page.locator('[data-testid="service-panel"]')
  await expect(panel).toBeVisible()
  await expect(panel).toHaveCSS('border-top-width', '0px')
  await expect(panel).toHaveCSS('border-top-left-radius', '6px')
  await expect(page.locator('[data-testid="service-label"]')).toHaveCSS('font-size', '14px')
  await expect(page.locator('[data-testid="service-label"]')).toHaveCSS('font-weight', '700')
  await expect(page.locator('[data-testid="service-panel-head"]')).toHaveCSS(
    'border-bottom-width',
    '0px',
  )
  await expect(page.locator('[data-testid="service-panel-head"]')).toHaveCSS('min-height', '54px')
  await expect(page.locator('[data-testid="service-panel-body"]')).toHaveCSS(
    'background-color',
    'rgb(255, 255, 255)',
  )
  await expect(page.locator('[data-testid="service-head"] h1')).toHaveCSS('font-size', '20px')
  await expect(page.locator('[data-testid="service-retry"]')).toHaveCSS('height', '30px')
  await expect(page.locator('[data-testid="service-tag"]')).toHaveCSS('height', '18px')
  await expect(page.locator('[class^="el-"], [class*=" el-"]')).toHaveCount(0)

  const hasPageOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth > window.innerWidth,
  )
  expect(hasPageOverflow).toBe(false)

  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.reload()
  await expect(page.locator('[data-testid="service-panel"]')).toBeVisible()
  const hasWideOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth > window.innerWidth,
  )
  expect(hasWideOverflow).toBe(false)

  await page.goto('/shops')
  await expect(shopsTable(page)).toBeVisible()
  const hasWideShopsOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth > window.innerWidth,
  )
  expect(hasWideShopsOverflow).toBe(false)
})

test('Given 独立服务路由 When 从设置侧栏导航 Then 授权店铺和服务连接均可达并已渲染', async ({
  page,
}) => {
  await mockShops(page)
  await page.route('**/api/ping', (route) => route.fulfill({ json: { message: '连接正常' } }))
  await page.goto('/')
  await page.locator('[data-group="settings"] button[aria-expanded]').click()

  await page.locator('[data-nav-page="shops"]').click()
  await expect(page).toHaveURL(/\/shops$/)
  await expect(shopsTable(page)).toBeVisible()
  await expect(page.locator('[data-nav-page="shops"]').first()).toHaveAttribute(
    'aria-current',
    'page',
  )

  await expect(page.locator('[data-nav-page="orders-sync"]')).toHaveCount(0)

  await page.locator('[data-nav-page="service-connection"]').click()
  await expect(page).toHaveURL(/\/service-connection$/)
  await expect(page.locator('[data-testid="service-panel"]')).toBeVisible()
  await expect(page.locator('[data-nav-page="service-connection"]').first()).toHaveAttribute(
    'aria-current',
    'page',
  )
})

test('Given 一页 20 家店铺 When 视口高度不足 Then 分页栏固定可见且表格体可滚动到最后一行', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 700 })
  await mockShops(page, 20)
  await page.goto('/shops')

  await expect(shopRows(page)).toHaveCount(20)
  const pagination = page.getByRole('navigation', { name: '授权店铺分页' })
  await expect(pagination).toBeInViewport()

  const scroll = page.getByRole('region', { name: '授权店铺表格' })
  const metrics = await scroll.evaluate((element) => ({
    scrollHeight: element.scrollHeight,
    clientHeight: element.clientHeight,
  }))
  expect(metrics.scrollHeight).toBeGreaterThan(metrics.clientHeight)

  await scroll.hover()
  await page.mouse.wheel(0, 4000)
  await expect(shopRows(page).last()).toBeInViewport()
  await expect(pagination).toBeInViewport()
})

test('Given 多页店铺 When 使用完整分页器 Then 页码、每页条数与跳页按新参数请求', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await mockPagedShops(page, 45)
  await page.goto('/shops')

  await expect(shopRows(page)).toHaveCount(20)
  await expect(shopPagerCurrent(page)).toHaveText('1')
  await expect(shopRows(page).first()).toContainText('分页店铺 1')

  await page.getByRole('button', { name: '下一页' }).click()
  await expect(shopPagerCurrent(page)).toHaveText('2')
  await expect(shopRows(page).first()).toContainText('分页店铺 21')

  await page.getByLabel('每页条数').selectOption('50')
  await expect(shopPagerCurrent(page)).toHaveText('1')
  await expect(shopRows(page)).toHaveCount(45)

  await page.getByLabel('每页条数').selectOption('20')
  await expect(shopRows(page)).toHaveCount(20)

  const jump = page.getByLabel('前往页码')
  await jump.fill('9')
  await jump.press('Enter')
  await expect(shopPagerCurrent(page)).toHaveText('3')
  await expect(shopRows(page).first()).toContainText('分页店铺 41')

  await jump.fill('0')
  await jump.press('Enter')
  await expect(shopPagerCurrent(page)).toHaveText('1')
  await expect(shopRows(page).first()).toContainText('分页店铺 1')

  await jump.fill('99')
  await jump.press('Enter')
  await expect(shopPagerCurrent(page)).toHaveText('3')
})

test('Given 店铺列表返回长错误详情 When 加载失败 Then 提示条完整换行且页面无横向溢出', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.route('**/api/erp/config', (route) =>
    route.fulfill({
      json: { configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: false },
    }),
  )
  const longDetail = 'problemdetail'.repeat(60)
  await page.route('**/api/erp/shops?**', (route) =>
    route.fulfill({
      status: 503,
      json: { title: 'ERP 服务不可用', detail: longDetail },
    }),
  )
  await page.goto('/shops')

  const notice = page.getByRole('alert').first()
  await expect(notice).toBeVisible()
  await expect(notice).toHaveAttribute('role', 'alert')
  await expect(notice).toContainText(longDetail)

  const metrics = await notice.evaluate((element) => ({
    scrollWidth: element.scrollWidth,
    clientWidth: element.clientWidth,
  }))
  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth)
  const hasPageOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth > window.innerWidth,
  )
  expect(hasPageOverflow).toBe(false)
})

test('Given 服务连接 When 检测成功或失败 Then 显示对应状态与既有文案', async ({ page }) => {
  let fail = false
  await page.route('**/api/ping', (route) =>
    fail
      ? route.fulfill({ status: 500, body: 'boom' })
      : route.fulfill({ json: { message: 'pong' } }),
  )

  await page.goto('/service-connection')
  await expect(page.locator('[data-testid="service-tag"]')).toHaveText('连接成功')
  await expect(page.locator('[data-testid="service-message"]')).toHaveText('后端响应：pong')

  fail = true
  await page.getByRole('button', { name: '重试' }).click()
  await expect(page.locator('[data-testid="service-tag"]')).toHaveText('连接失败')
  await expect(page.locator('[data-testid="service-message"][data-tone="error"]')).toContainText(
    '服务器返回 HTTP 500',
  )
  await expect(page.getByRole('button', { name: '重试' })).toBeEnabled()
})
