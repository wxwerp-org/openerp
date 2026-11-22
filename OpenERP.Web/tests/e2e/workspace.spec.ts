import { expect, test, type Page } from '@playwright/test'
import { enterWorkspace, type WorkspaceRole } from './authentication'

const shopsTable = (page: Page) => page.getByRole('table', { name: '授权店铺列表' })
const shopRows = (page: Page) => shopsTable(page).locator('tbody tr')
const shopPagerCurrent = (page: Page) =>
  page.getByRole('navigation', { name: '授权店铺分页' }).locator('[aria-current="page"]')
const hasPageOverflow = (page: Page) =>
  page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)

/** 登录会话 + Hub 断连：工作台与列表页在无后端时按离线态渲染。 */
async function prepare(page: Page, role: WorkspaceRole = 'Administrator'): Promise<void> {
  await enterWorkspace(page, role)
  await page.route('**/api/hubs/erp/**', (route) => route.abort())
}

// ---------- 工作台 mock ----------

const reportShops = [
  {
    source: 'wxw-erp',
    platform: 'TemuPop',
    shopId: '30',
    key: 'wxw-erp/TemuPop/30',
    label: '店铺 30',
  },
  {
    source: 'wxw-erp',
    platform: 'AliExpress',
    shopId: '31',
    key: 'wxw-erp/AliExpress/31',
    label: '店铺 31',
  },
]

function workspaceReport() {
  return {
    generatedAtUtc: '2026-11-20T02:03:04Z',
    summary: { orderCount: 6, itemQuantity: 15 },
    platforms: ['TemuPop', 'AliExpress'],
    shops: reportShops,
    groups: [
      { shop: reportShops[0], orderCount: 4, itemQuantity: 12 },
      { shop: reportShops[1], orderCount: 2, itemQuantity: 3 },
    ],
    orders: [],
    page: 1,
    pageSize: 20,
    pageCount: 1,
  }
}

function emptyReport() {
  return {
    ...workspaceReport(),
    summary: { orderCount: 0, itemQuantity: 0 },
    groups: [],
  }
}

type DirectoryShopMock = {
  sid: string
  name: string
  platform: string
  region: string | null
  memberId: string | null
  isTokenOk: boolean | null
  tokenError: string | null
  syncedAt: string | null
}

const directoryItems: DirectoryShopMock[] = [
  {
    sid: '30',
    name: 'Temu 测试店铺 30',
    platform: 'TemuPop',
    region: 'US',
    memberId: 'member-30',
    isTokenOk: true,
    tokenError: null,
    syncedAt: '2026-11-20T01:50:00.000Z',
  },
  {
    sid: '31',
    name: 'AE 测试店铺 31',
    platform: 'AliExpress',
    region: 'GB',
    memberId: 'member-31',
    isTokenOk: true,
    tokenError: null,
    syncedAt: '2026-11-20T01:50:00.000Z',
  },
]

/** 工作台真实数据态所需的报表、目录、配置与可用性接口。 */
async function mockWorkspaceApis(
  page: Page,
  options: { report?: unknown; directoryItems?: unknown[]; employee?: boolean } = {},
): Promise<void> {
  const report = options.report ?? workspaceReport()
  const items = options.directoryItems ?? directoryItems
  // 员工响应含所属店铺的状态，管理员额外获得成员 ID 与授权汇总。
  const minimal = options.employee === true
  await page.route('**/api/reports/order-snapshot?**', (route) => route.fulfill({ json: report }))
  await page.route('**/api/about', (route) =>
    route.fulfill({
      json: { version: '1.0.0', commit: '3abfbcc', buildTime: '2026-11-20T00:00:00Z' },
    }),
  )
  await page.route('**/api/erp/order-sync-availability', (route) =>
    route.fulfill({ json: { configured: true, orderSyncEnabled: true } }),
  )
  await page.route('**/api/erp/config', (route) =>
    route.fulfill({
      json: { configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: true },
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
            shopCount: items.length,
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
        completedAt: '2026-11-20T01:50:00.000Z',
        total: items.length,
        checked: items.length,
        valid: items.length,
        invalid: 0,
        unknown: 0,
        error: null,
        version: 2,
      },
    }),
  )
  await page.route('**/api/erp/shops?**', (route) => {
    const url = new URL(route.request().url())
    const platform = url.searchParams.get('platform')
    const status = url.searchParams.get('status')
    const currentPage = Number(url.searchParams.get('page') ?? 1)
    const pageSize = Number(url.searchParams.get('pageSize') ?? 20)
    const filtered = (items as DirectoryShopMock[]).filter(
      (shop) =>
        (!platform || shop.platform === platform) &&
        (!status ||
          (status === 'valid' && shop.isTokenOk === true) ||
          (status === 'invalid' && shop.isTokenOk === false) ||
          (status === 'unknown' && shop.isTokenOk === null)),
    )
    const pageCount = filtered.length === 0 ? 0 : Math.ceil(filtered.length / pageSize)
    const start = (Math.min(currentPage, Math.max(pageCount, 1)) - 1) * pageSize
    const pageItems = filtered.slice(start, start + pageSize).map((shop) =>
      minimal
        ? {
            sid: shop.sid,
            name: shop.name,
            platform: shop.platform,
            region: shop.region,
            isTokenOk: shop.isTokenOk,
            tokenError: shop.tokenError,
            syncedAt: shop.syncedAt,
          }
        : shop,
    )
    return route.fulfill({
      json: {
        items: pageItems,
        page: Math.min(currentPage, Math.max(pageCount, 1)),
        pageSize,
        totalCount: filtered.length,
        pageCount,
        // 员工最小投影没有授权汇总。
        ...(minimal
          ? {}
          : {
              summary: {
                valid: items.length,
                invalid: 0,
                unknown: 0,
                lastSyncedAt: items.length > 0 ? '2026-11-20T01:50:00.000Z' : null,
                syncVersion: 2,
              },
            }),
      },
    })
  })
}

// ---------- 授权店铺 mock ----------

async function mockShops(page: Page, count = 1): Promise<void> {
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

// ---------- 订单列表 mock ----------

async function mockOrders(page: Page): Promise<void> {
  await page.route('**/api/erp/order-sync-availability', (route) =>
    route.fulfill({ json: { configured: true, orderSyncEnabled: true } }),
  )
  await page.route(/\/api\/orders(?:\?|$)/, (route) =>
    route.fulfill({
      json: {
        items: [
          {
            id: 'browser-local-order',
            sid: 30,
            shopName: null,
            platform: 'TemuPop',
            platformOrders: [{ number: 'TM-72801-A', status: '已发货', matched: false }],
            created: '2026-11-17T08:30:00',
            dispatched: null,
            lineCount: 1,
            itemQuantity: 5,
          },
        ],
        options: {
          shops: [{ sid: 30, name: null }],
          platforms: ['TemuPop'],
          statuses: ['已发货'],
        },
        total: 1,
        page: 1,
        pageSize: 20,
      },
    }),
  )
}

// ---------- 工作台 ----------

test('Given 工作台有数据 When 打开 Then 状态条数字卡图表卡与状态卡展示真实口径且下钻带 URL 筛选', async ({
  page,
}) => {
  await prepare(page)
  await mockWorkspaceApis(page)
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/')

  await expect(page.locator('[data-testid="page-dashboard"]')).toBeVisible()

  // 数据状态条：查询时间与目录同步摘要均来自真实接口。
  const statusBar = page.locator('[data-testid="workspace-status-bar"]')
  await expect(statusBar).toContainText('查询时间')
  await expect(page.locator('[data-testid="report-generated-at"]')).not.toHaveText('—')
  await expect(page.locator('[data-testid="directory-synced-at"]')).not.toHaveText('—')
  await expect(page.locator('[data-testid="workspace-refresh"]')).toBeVisible()

  // 数字卡：订单数 / 商品件数 / 有订单店铺 / 目录最近同步时间。
  await expect(page.locator('[data-testid="metric-orders"]')).toContainText('6')
  await expect(page.locator('[data-testid="metric-items"]')).toContainText('15')
  await expect(page.locator('[data-testid="metric-shops"]')).toContainText('2')
  await expect(page.locator('[data-testid="metric-last-sync"]')).toBeVisible()
  await expect(page.locator('[data-testid="metric-platforms"]')).toHaveCount(0)

  // 图卡：平台分布与店铺商品量 Top5，店名按目录映射。
  const platformCard = page.locator('[data-testid="workspace-platform-card"]')
  await expect(platformCard.locator('[data-platform="TemuPop"]')).toContainText('4')
  await expect(platformCard.locator('[data-platform="AliExpress"]')).toContainText('2')
  const rankCard = page.locator('[data-testid="workspace-shop-rank-card"]')
  await expect(rankCard.locator('[data-shop-key]')).toHaveCount(2)
  const firstRank = rankCard.locator('[data-shop-key]').first()
  await expect(firstRank).toHaveAttribute('data-shop-key', 'wxw-erp/TemuPop/30')
  await expect(firstRank).toContainText('Temu 测试店铺 30')

  // 状态卡：管理员 3 张，且概览区不出现空态引导。
  await expect(
    page.locator(
      '[data-testid="workspace-health-card"], [data-testid="workspace-sync-card"], [data-testid="workspace-system-card"]',
    ),
  ).toHaveCount(3)
  await expect(page.locator('[data-testid="workspace-health-card"]')).toContainText('有效')
  await expect(page.locator('[data-testid="workspace-system-card"]')).toContainText('已配置')
  await expect(page.locator('[data-testid="workspace-onboarding"]')).toHaveCount(0)
  expect(await hasPageOverflow(page)).toBe(false)

  // 平台下钻：URL 使用平台标识。
  await platformCard.locator('[data-platform="TemuPop"]').click()
  await expect(page).toHaveURL(/\/reports\/orders\?platform=TemuPop$/)
  await page.goBack()
  await expect(rankCard.locator('[data-shop-key]')).toHaveCount(2)

  // 店铺下钻：URL 使用报表返回的完整店铺键并编码。
  await rankCard.locator('[data-shop-key="wxw-erp/TemuPop/30"]').click()
  await expect(page).toHaveURL(/\/reports\/orders\?shops=wxw-erp\/TemuPop\/30$/)
})

test('Given 工作台空态 When 管理员打开 Then 引导带按真实状态展示完成标记并可收起', async ({
  page,
}) => {
  await prepare(page)
  await mockWorkspaceApis(page, { report: emptyReport() })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/')

  const ribbon = page.locator('[data-testid="workspace-onboarding"]')
  await expect(ribbon).toBeVisible()
  await expect(ribbon.locator('[data-step]')).toHaveCount(4)
  await expect(ribbon.locator('[data-step="configure"]')).toHaveAttribute('data-step-done', 'true')
  await expect(ribbon.locator('[data-step="authorize"]')).toHaveAttribute('data-step-done', 'true')
  await expect(ribbon.locator('[data-step="sync-directory"]')).toHaveAttribute(
    'data-step-done',
    'true',
  )
  await expect(ribbon.locator('[data-step="sync-orders"]')).toHaveAttribute(
    'data-step-done',
    'false',
  )
  await expect(ribbon.locator('[data-step="sync-orders"]')).toHaveAttribute(
    'data-step-executable',
    'true',
  )
  await expect(page.locator('[data-testid="onboarding-progress"]')).toHaveText('3/4')

  // 空态数字卡用真实 0，而不是骨架或假数字。
  await expect(page.locator('[data-testid="metric-orders"]')).toContainText('0')
  await expect(page.locator('[data-testid="workspace-platform-card"]')).toContainText(
    '暂无订单数据',
  )

  // 收起后保留一行进度与下一步提示。
  await page.locator('[data-testid="onboarding-toggle"]').click()
  await expect(page.locator('[data-testid="onboarding-progress"]')).toHaveText(
    '初始化进度 3/4 · 下一步：同步历史订单',
  )
  await page.locator('[data-testid="onboarding-toggle"]').click()
  await expect(ribbon.locator('[data-step="sync-orders"]')).toHaveAttribute(
    'data-step-executable',
    'true',
  )

  // 第 4 步执行入口打开真实同步弹窗。
  await ribbon.locator('[data-step="sync-orders"]').click()
  await expect(page.locator('#order-sync-panel')).toBeVisible()
})

test('Given 报表快照后刷新失败 When 手动刷新 Then 保留上次数据并标注刷新失败', async ({ page }) => {
  await prepare(page)
  await mockWorkspaceApis(page)
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/')
  await expect(page.locator('[data-testid="metric-orders"]')).toContainText('6')
  const generatedAt = await page.locator('[data-testid="report-generated-at"]').innerText()

  await page.route('**/api/reports/order-snapshot?**', (route) =>
    route.fulfill({ status: 503, json: { title: '报表数据暂不可用' } }),
  )
  await page.locator('[data-testid="workspace-refresh"]').click()

  await expect(page.locator('[data-testid="report-stale"]')).toHaveText('刷新失败，显示上次结果')
  await expect(page.locator('[data-testid="metric-orders"]')).toContainText('6')
  await expect(page.locator('[data-testid="report-generated-at"]')).toHaveText(generatedAt)
  await expect(page.locator('[data-testid="page-dashboard"]')).toBeVisible()
})

test('Given 账户菜单 When 打开个人信息、修改密码与关于 Then 焦点行为与版本信息保持一致', async ({
  page,
}) => {
  await prepare(page)
  await mockWorkspaceApis(page)
  await page.goto('/')

  const trigger = page.locator('[data-testid="account-button"]')
  await expect(page.locator('[data-testid="account-version"]')).toHaveText('v1.0.0 · 3abfbcc')

  await trigger.click()
  await page.locator('[data-account-action="profile"]').click()
  await expect(page.locator('#profileLayer')).toBeVisible()
  await expect(page.locator('#profileName')).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(page.locator('#profileLayer')).toBeHidden()
  await expect(trigger).toBeFocused()

  await trigger.click()
  await page.locator('[data-account-action="password"]').click()
  await expect(page.locator('#oldPassword')).toBeFocused()
  await page.locator('#oldPassword').fill('old-password')
  await page.locator('#newPassword').fill('new-password')
  await page.locator('#confirmPassword').fill('different')
  await page.locator('#savePassword').click()
  await expect(page.getByText('两次输入的密码不一致')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.locator('#passwordLayer')).toBeHidden()
  await expect(trigger).toBeFocused()

  // 「关于」展示匿名 /api/about 返回的版本与提交，不出现假版本。
  await trigger.click()
  await page.locator('[data-account-action="about"]').click()
  await expect(page.locator('#aboutLayer')).toBeVisible()
  await expect(page.locator('#aboutLayer')).toContainText('v1.0.0')
  await expect(page.locator('#aboutLayer')).toContainText('3abfbcc')
  await page.keyboard.press('Escape')
  await expect(page.locator('#aboutLayer')).toBeHidden()
})

// ---------- 权限与导航 ----------

test('Given 管理员侧栏 When 查看导航 Then 只出现收敛后的条目且即将上线默认收起不可导航', async ({
  page,
}) => {
  await prepare(page)
  await mockWorkspaceApis(page)
  await page.goto('/')

  const sidebar = page.locator('[data-testid="sidebar"]')
  await expect(sidebar.locator('[data-group="orders"]')).toBeVisible()
  await expect(sidebar.locator('[data-group="orders"] [data-nav-page="orders"]')).toBeVisible()
  await expect(
    sidebar.locator('[data-group="orders"] [data-nav-page="order-report"]'),
  ).toBeVisible()
  await expect(sidebar.locator('[data-nav-page="shops"]')).toBeVisible()
  await expect(sidebar.locator('[data-nav-page="users"]')).toBeVisible()
  await expect(sidebar.locator('[data-nav-page="business"]')).toBeVisible()
  for (const removed of ['newdev', 'products', 'service-connection', 'orders-sync']) {
    await expect(sidebar.locator(`[data-nav-page="${removed}"]`)).toHaveCount(0)
  }

  // 「即将上线」默认收起，展开后三项置灰并标注即将上线，不产生导航。
  const upcoming = sidebar.locator('[data-group="upcoming"]')
  const upcomingHead = upcoming.locator('button[aria-expanded]')
  await expect(upcomingHead).toHaveAttribute('aria-expanded', 'false')
  await expect(upcoming.getByRole('button', { name: '商品管理' })).toBeHidden()
  await upcomingHead.click()
  await expect(upcomingHead).toHaveAttribute('aria-expanded', 'true')
  for (const label of ['商品管理', '物流', '仓库']) {
    const item = upcoming.getByRole('button', { name: label, exact: true })
    await expect(item).toBeVisible()
    await expect(item).toBeDisabled()
    await expect(item).toHaveAttribute('title', '即将上线')
    await expect(item).toHaveAttribute('data-nav-page', '')
  }
  await expect(page).toHaveURL(/\/$/)

  // 「+」菜单按角色过滤且不含原型页面。
  await page.locator('[data-testid="tab-add"]').click()
  const menuLinks = page.locator('#tabMenu a')
  await expect(menuLinks).toHaveCount(5)
  await expect(menuLinks).toHaveText(['工作台', '订单列表', '商品量报表', 'ERP 接入', '用户管理'])
})

test('Given 普通员工 When 打开工作台 Then 侧栏无管理条目且空态显示联系管理员提示', async ({
  page,
}) => {
  await prepare(page, 'User')
  await mockWorkspaceApis(page, { report: emptyReport(), directoryItems: [], employee: true })
  await page.goto('/')

  const sidebar = page.locator('[data-testid="sidebar"]')
  await expect(sidebar.locator('[data-group="orders"] [data-nav-page="orders"]')).toBeVisible()
  await expect(
    sidebar.locator('[data-group="orders"] [data-nav-page="order-report"]'),
  ).toBeVisible()
  await expect(sidebar.locator('[data-nav-page="shops"]')).toBeVisible()
  for (const hidden of ['users', 'business']) {
    await expect(sidebar.locator(`[data-nav-page="${hidden}"]`)).toHaveCount(0)
  }

  // 「+」菜单同样按角色过滤。
  await page.locator('[data-testid="tab-add"]').click()
  await expect(page.locator('#tabMenu a')).toHaveCount(3)

  // 员工空态提供店铺授权入口和分配说明。
  const hint = page.locator('[data-testid="employee-empty-hint"]')
  await expect(hint).toBeVisible()
  await expect(hint).toContainText('尚无可见店铺')
  await expect(hint.getByRole('link', { name: '前往授权店铺' })).toHaveAttribute('href', '/shops')
  await expect(page.locator('[data-testid="workspace-onboarding"]')).toHaveCount(0)
  await expect(page.locator('[data-testid="workspace-health-card"]')).toHaveCount(0)
  await expect(page.locator('[data-testid="workspace-system-card"]')).toHaveCount(0)
  await expect(page.locator('[data-testid="workspace-sync-card"]')).toHaveCount(1)
  await expect(page.locator('[data-testid="metric-orders"]')).toContainText('0')
  await expect(page.locator('[data-testid="metric-last-sync"]')).toHaveCount(0)
  await expect(page.locator('[data-testid="metric-platforms"]')).toBeVisible()
})

test('Given 普通员工 When 直达管理路由 Then 重定向工作台并提示仅管理员可访问', async ({ page }) => {
  await prepare(page, 'User')
  await mockWorkspaceApis(page, { report: emptyReport(), employee: true })

  const managedPages = [
    { path: '/users', selector: '#page-users' },
    { path: '/settings', selector: '#page-business' },
    { path: '/visual-prototype', selector: '[data-testid="visual-prototype"]' },
    { path: '/visual-prototype/products', selector: '#page-product' },
  ]
  for (const { path, selector } of managedPages) {
    await page.goto(path)
    await expect(page).toHaveURL(/\/$/)
    await expect(page.locator('[data-testid="page-dashboard"]')).toBeVisible()
    await expect(page.locator(selector)).toHaveCount(0)
    await expect(page.locator('[data-testid="toast"]')).toHaveText('该页面仅管理员可访问。')
  }
})

// ---------- 授权店铺 ----------

test('Given 独立页面路由 When 从侧栏导航 Then 授权店铺可达且已删条目无入口', async ({ page }) => {
  await prepare(page)
  await mockShops(page)
  await page.goto('/')

  await page.locator('[data-testid="side-nav"] [data-nav-page="shops"]').click()
  await expect(page).toHaveURL(/\/shops$/)
  await expect(shopsTable(page)).toBeVisible()
  await expect(page.locator('[data-testid="side-nav"] [data-nav-page="shops"]')).toHaveAttribute(
    'aria-current',
    'page',
  )

  for (const removed of ['newdev', 'products', 'service-connection', 'orders-sync']) {
    await expect(page.locator(`[data-nav-page="${removed}"]`)).toHaveCount(0)
  }
})

test('Given 桌面端授权店铺 When 打开 Then 沿用 Dashboard 卡片语言且无横向溢出', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await prepare(page)
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
  expect(await hasPageOverflow(page)).toBe(false)

  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.reload()
  await expect(shopsTable(page)).toBeVisible()
  expect(await hasPageOverflow(page)).toBe(false)
})

test('Given 一页 20 家店铺 When 视口高度不足 Then 分页栏固定可见且表格体可滚动到最后一行', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 700 })
  await prepare(page)
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
  await prepare(page)
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
  await prepare(page)
  await page.route('**/api/erp/order-sync-availability', (route) =>
    route.fulfill({
      json: { configured: true, orderSyncEnabled: false },
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
  expect(await hasPageOverflow(page)).toBe(false)
})

// ---------- 订单列表 ----------

test('Given 订单列表 When 打开 Then 订单公告入口已移除且桌面无横向溢出', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await prepare(page)
  await mockOrders(page)
  await page.goto('/orders')

  const region = page.getByRole('region', { name: '订单列表' })
  await expect(region.locator('tbody tr').first()).toBeVisible()
  await expect(page.locator('#openNotice, #noticeLayer')).toHaveCount(0)
  await expect(page.getByText('订单公告')).toHaveCount(0)
  expect(await hasPageOverflow(page)).toBe(false)
})
