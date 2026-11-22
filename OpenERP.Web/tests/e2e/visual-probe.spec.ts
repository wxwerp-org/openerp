import { expect, test, type Page } from '@playwright/test'
import { mkdirSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import {
  captureScenario,
  desktop,
  type Probe,
  type Scenario,
  visualAccount,
  wide,
} from './visual-probe'
import type { ReportTrend } from '../../src/reports/orderSnapshot'
import { dataTrendFixture, emptyTrendFixture, salesFixture } from '../reportFixtures'

test.skip(!process.env.VISUAL_PROBE, '仅 VISUAL_PROBE=1 时采集视觉探针。')

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://127.0.0.1:4173'

const order = {
  id: 'visual-order',
  sid: 30,
  shopName: 'Temu 测试店铺 30',
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

const orderOptions = {
  shops: [{ sid: 30, name: 'Temu 测试店铺 30' }],
  platforms: ['TemuPop'],
  statuses: ['待发货', '已发货'],
}

const reportShop = {
  source: 'wxw-erp',
  platform: 'TemuPop',
  shopId: '30',
  key: 'wxw-erp/TemuPop/30',
  label: '店铺 30',
}

const reportSample = {
  generatedAtUtc: '2026-11-17T01:02:03Z',
  summary: { orderCount: 1, itemQuantity: 5 },
  sales: salesFixture(371.12, 'USD', 1, 0),
  trend: dataTrendFixture(),
  platforms: ['TemuPop'],
  shops: [reportShop],
  groups: [{ shop: reportShop, orderCount: 1, itemQuantity: 5 }],
  orders: [
    {
      id: 'sample-order-30',
      number: 'ERP-ORDER-30',
      shop: reportShop,
      status: '已发货',
      platformOrders: [{ number: 'PO-ORDER-30', statusCode: 'SHIPPED', statusText: '已发货' }],
      items: [
        {
          lineId: 'line-30',
          productId: 'product-30',
          skuId: 'sku-30',
          skuCode: 'SKU-30',
          quantity: 5,
          disabled: true,
          appended: true,
        },
      ],
      itemQuantity: 5,
    },
  ],
  page: 1,
  pageSize: 20,
  pageCount: 1,
}

const mockShop = (index: number) => ({
  sid: String(index),
  name: `Temu 测试店铺 ${index}`,
  platform: 'TemuPop',
  region: index % 3 === 0 ? null : 'US',
  memberId: `member-${index}`,
  isTokenOk: index % 3 === 0 ? null : index % 3 === 1,
  tokenError: index % 3 === 0 ? '状态读取失败' : null,
  syncedAt: '2026-10-01T08:00:00.000Z',
})

async function mockSyncAvailability(
  page: Page,
  overrides: Record<string, unknown> = {},
): Promise<void> {
  await page.route('**/api/erp/order-sync-availability', (route) =>
    route.fulfill({
      json: {
        configured: true,
        orderSyncEnabled: false,
        ...overrides,
      },
    }),
  )
}

async function mockOrders(page: Page, items = [order], total = items.length): Promise<void> {
  await page.route(/\/api\/orders(?:\?|$)/, (route) =>
    route.fulfill({
      json: { items, options: orderOptions, total, page: 1, pageSize: 20 },
    }),
  )
}

async function mockOrdersError(page: Page): Promise<void> {
  await page.route(/\/api\/orders(?:\?|$)/, (route) =>
    route.fulfill({ status: 503, json: { title: '订单数据暂不可用' } }),
  )
}

function addDays(value: string, days: number): string {
  const date = new Date(`${value}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

/** 按请求区间重排夹具日期，使探针截图里的口径文案与窗口一致。 */
function trendForRequest(trend: ReportTrend, url: URL): ReportTrend {
  const start = url.searchParams.get('trendStart')
  const end = url.searchParams.get('trendEnd')
  if (!start || !end) return trend
  const days =
    Math.round((Date.parse(`${end}T00:00:00Z`) - Date.parse(`${start}T00:00:00Z`)) / 86400000) + 1
  const source =
    trend.points.length > 0 ? trend.points : [{ orderCount: 0, itemQuantity: 0, salesAmount: 0 }]
  const points = Array.from({ length: days }, (_, index) => {
    const point = source[index % source.length]!
    return {
      date: addDays(start, index),
      orderCount: point.orderCount,
      itemQuantity: point.itemQuantity,
      salesAmount: point.salesAmount,
    }
  })
  let known = 0
  let unknown = 0
  for (const point of points) {
    if (point.orderCount === 0) continue
    if (point.salesAmount === null) unknown += point.orderCount
    else known += point.orderCount
  }
  return {
    ...trend,
    start,
    end,
    days,
    points,
    amountKnownOrderCount: known,
    amountUnknownOrderCount: unknown,
  }
}

async function mockReport(page: Page, body: unknown = reportSample): Promise<void> {
  const report = body as { trend: ReportTrend } & Record<string, unknown>
  await page.route('**/api/reports/order-snapshot?**', (route) =>
    route.fulfill({
      json: { ...report, trend: trendForRequest(report.trend, new URL(route.request().url())) },
    }),
  )
}

const shopsTable = (page: Page) => page.getByRole('table', { name: '授权店铺列表' })

async function mockShopsDirectory(
  page: Page,
  options: {
    items?: unknown[]
    totalCount?: number
    configured?: boolean
    shopsError?: boolean
  } = {},
): Promise<void> {
  const items = options.items ?? [mockShop(1), mockShop(2), mockShop(3)]
  const totalCount = options.totalCount ?? items.length
  const configured = options.configured ?? true
  await mockSyncAvailability(page, { configured })
  await page.route('**/api/erp/platforms', (route) =>
    route.fulfill({
      json: {
        items: [
          {
            name: 'TemuPop',
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
        completedAt: '2026-10-01T08:00:00.000Z',
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
  if (options.shopsError) {
    await page.route('**/api/erp/shops?**', (route) =>
      route.fulfill({
        status: 503,
        json: { title: 'ERP 服务不可用', detail: '无法获取本地店铺，请稍后重试。' },
      }),
    )
    return
  }
  await page.route('**/api/erp/shops?**', (route) => {
    const url = new URL(route.request().url())
    const pageNumber = Number(url.searchParams.get('page') ?? 1)
    const pageSize = Number(url.searchParams.get('pageSize') ?? 20)
    const pageCount = totalCount === 0 ? 0 : Math.ceil(totalCount / pageSize)
    const start = (Math.min(pageNumber, Math.max(pageCount, 1)) - 1) * pageSize
    const pageItems = items.slice(start, start + pageSize)
    return route.fulfill({
      json: {
        items: pageItems,
        page: Math.min(pageNumber, Math.max(pageCount, 1)),
        pageSize,
        totalCount,
        pageCount,
        summary: {
          valid: 1,
          invalid: 1,
          unknown: 1,
          lastSyncedAt: '2026-10-01T08:00:00.000Z',
          syncVersion: 1,
        },
      },
    })
  })
}

async function mockShopsLoading(page: Page): Promise<void> {
  await mockShopsDirectory(page)
  await page.unroute('**/api/erp/shops?**')
  await page.route('**/api/erp/shops?**', async (route) => {
    await new Promise((resolveDelay) => setTimeout(resolveDelay, 15_000))
    await route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0, pageCount: 0 } })
  })
}

async function mockOrderSync(page: Page): Promise<void> {
  await mockSyncAvailability(page, { orderSyncEnabled: true })
  await page.route('**/api/erp/order-sync-settings', (route) =>
    route.fulfill({ json: { enabled: true, shopSids: [1], intervalMinutes: 30 } }),
  )
  await page.route('**/api/erp/order-sync-settings/shops', (route) =>
    route.fulfill({ json: { items: [{ sid: '1', name: '测试店铺 1' }] } }),
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
}

const emptyReport = {
  ...reportSample,
  summary: { orderCount: 0, itemQuantity: 0 },
  sales: salesFixture(0, 'USD', 0, 0),
  trend: emptyTrendFixture(),
  groups: [],
  orders: [],
  page: 1,
  pageCount: 1,
}

const prototypeBannerText = '视觉原型 · 全部为假数据 · 冻结于 2026-11-20 · 仅内部参考，请勿对外展示'

function waitPrototype(page: Page) {
  return page.getByText(prototypeBannerText).waitFor()
}

/** 真实工作台的数据态/空态截图所需接口（报表、目录、配置、可用性、版本）。 */
async function mockWorkspaceDashboard(page: Page, empty = false): Promise<void> {
  await mockReport(page, empty ? emptyReport : reportSample)
  await mockShopsDirectory(page)
  await page.route('**/api/erp/config', (route) =>
    route.fulfill({
      json: { configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: true },
    }),
  )
  await mockSyncAvailability(page, { orderSyncEnabled: true })
  await page.route('**/api/about', (route) =>
    route.fulfill({
      json: { version: '1.0.0', commit: '3abfbcc', buildTime: '2026-11-20T00:00:00Z' },
    }),
  )
}

function waitWorkspace(page: Page) {
  return page
    .locator('[data-testid="page-dashboard"] [data-testid="workspace-status-bar"]')
    .waitFor()
}

const scenarios: Scenario[] = [
  {
    name: 'dashboard-1440',
    path: '/',
    prepare: mockWorkspaceDashboard,
    waitFor: waitWorkspace,
  },
  {
    name: 'dashboard-empty-1440',
    path: '/',
    prepare: async (page) => mockWorkspaceDashboard(page, true),
    waitFor: waitWorkspace,
  },
  {
    name: 'dashboard-quarter-1440',
    path: '/',
    prepare: mockWorkspaceDashboard,
    waitFor: waitWorkspace,
    afterGoto: async (page) => {
      await page.getByRole('button', { name: '本季度' }).click()
      await page.locator('[data-testid="trend-footnote"]').getByText('本季度（').waitFor()
    },
  },
  {
    name: 'dashboard-custom-1440',
    path: '/',
    prepare: mockWorkspaceDashboard,
    waitFor: waitWorkspace,
    afterGoto: async (page) => {
      await page.getByTestId('trend-custom-toggle').click()
      await page.getByTestId('trend-custom-panel').waitFor()
    },
  },
  {
    name: 'dashboard-custom-390',
    width: 390,
    height: 844,
    path: '/',
    prepare: mockWorkspaceDashboard,
    waitFor: waitWorkspace,
    afterGoto: async (page) => {
      await page.getByTestId('trend-custom-toggle').click()
      await page.getByTestId('trend-custom-panel').waitFor()
    },
  },
  {
    name: 'dashboard-1920',
    path: '/',
    ...wide,
    prepare: mockWorkspaceDashboard,
    waitFor: waitWorkspace,
  },
  {
    name: 'dashboard-sidebar-collapsed-1440',
    path: '/',
    prepare: mockWorkspaceDashboard,
    waitFor: waitWorkspace,
    afterGoto: async (page) => {
      await page.locator('[data-testid="collapse-nav"]').click()
      await page.locator('[data-testid="sidebar"][data-rail="true"]').waitFor()
    },
  },
  {
    name: 'prototype-dashboard-1440',
    path: '/visual-prototype',
    waitFor: waitPrototype,
  },
  {
    name: 'users-1440',
    path: '/users',
    prepare: async (page) => {
      await page.route('**/api/users', (route) =>
        route.fulfill({ json: [{ ...visualAccount, createdAt: null }] }),
      )
    },
    waitFor: async (page) => page.getByRole('rowheader', { name: 'admin', exact: true }).waitFor(),
  },
  {
    name: 'prototype-products-1440',
    path: '/visual-prototype/products',
    waitFor: async (page) => {
      await waitPrototype(page)
      await page.getByRole('table', { name: '产品列表' }).waitFor()
    },
  },
  {
    name: 'prototype-products-dialog-1440',
    path: '/visual-prototype/products',
    waitFor: async (page) => {
      await waitPrototype(page)
      await page.getByRole('table', { name: '产品列表' }).waitFor()
    },
    afterGoto: async (page) => {
      await page.locator('#openProductForm').click()
      await page.locator('#productFormLayer[open]').waitFor()
    },
  },
  {
    name: 'prototype-products-dialog-error-1440',
    path: '/visual-prototype/products',
    waitFor: async (page) => {
      await waitPrototype(page)
      await page.getByRole('table', { name: '产品列表' }).waitFor()
    },
    afterGoto: async (page) => {
      await page.locator('#openProductForm').click()
      await page.locator('#productFormLayer[open]').waitFor()
      await page.locator('#saveProductForm').click()
      await page.locator('#productNameError').waitFor()
    },
  },
  {
    name: 'orders-1440',
    path: '/orders',
    prepare: async (page) => {
      await mockOrders(page)
      await mockSyncAvailability(page)
    },
    waitFor: async (page) =>
      page.getByRole('region', { name: '订单列表' }).locator('tbody tr').first().waitFor(),
  },
  {
    name: 'orders-empty-1440',
    path: '/orders',
    prepare: async (page) => {
      await mockOrders(page, [], 0)
      await mockSyncAvailability(page)
    },
    waitFor: async (page) => page.getByText('暂无同步订单').waitFor(),
  },
  {
    name: 'orders-error-1440',
    path: '/orders',
    prepare: async (page) => {
      await mockOrdersError(page)
      await mockSyncAvailability(page)
    },
    waitFor: async (page) => page.getByText('无法加载订单，请稍后重试。').waitFor(),
  },
  {
    name: 'orders-sync-1440',
    path: '/orders',
    prepare: async (page) => {
      await mockOrders(page)
      await mockOrderSync(page)
    },
    waitFor: async (page) =>
      page.getByRole('region', { name: '订单列表' }).locator('tbody tr').first().waitFor(),
    afterGoto: async (page) => {
      await page.getByRole('button', { name: '同步历史订单', exact: true }).first().click()
      await page.locator('#order-sync-panel[open]').waitFor()
      // 面板内定时同步卡片与店铺列表是并行异步加载的；两处都等就绪，避免截图落在加载中状态。
      await page.locator('#order-sync-panel').getByText('已启用', { exact: true }).waitFor()
      await page.locator('#order-sync-panel').getByText('测试店铺 1').first().waitFor()
    },
  },
  {
    name: 'report-1440',
    path: '/reports/orders',
    prepare: mockReport,
    waitFor: async (page) => page.locator('[aria-label="订单总数"]').waitFor(),
  },
  {
    name: 'report-expanded-1440',
    path: '/reports/orders',
    prepare: mockReport,
    waitFor: async (page) => page.locator('[aria-label="订单总数"]').waitFor(),
    afterGoto: async (page) => {
      await page
        .getByRole('button', { name: /^展开订单/ })
        .first()
        .click()
      await page.locator('[data-testid="report-order-details"]').waitFor()
    },
  },
  {
    name: 'report-empty-1440',
    path: '/reports/orders',
    prepare: async (page) =>
      mockReport(page, {
        ...reportSample,
        summary: { orderCount: 0, itemQuantity: 0 },
        groups: [],
        orders: [],
        page: 1,
        pageCount: 1,
      }),
    waitFor: async (page) => page.getByText('暂无分店汇总').waitFor(),
  },
  {
    name: 'report-error-1440',
    path: '/reports/orders',
    prepare: async (page) => {
      await page.route('**/api/reports/order-snapshot?**', (route) =>
        route.fulfill({ status: 503, json: { title: '报表数据暂不可用' } }),
      )
    },
    waitFor: async (page) => page.getByRole('alert').getByText('报表数据暂不可用').waitFor(),
  },
  {
    name: 'settings-erp-1440',
    path: '/settings',
    prepare: async (page) => {
      await page.route('**/api/erp/config', (route) =>
        route.fulfill({
          json: { configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: true },
        }),
      )
    },
    // /settings 由路由守卫补成 ?section=erp，只保留 ERP 接入单分区。
    waitFor: async (page) =>
      page.locator('[data-testid="erp-config-status"] [role="status"]').waitFor(),
  },
  {
    name: 'shops-default-1440',
    path: '/shops',
    prepare: mockShopsDirectory,
    waitFor: async (page) => shopsTable(page).locator('tbody tr').first().waitFor(),
  },
  {
    name: 'shops-default-1920',
    path: '/shops',
    ...wide,
    prepare: mockShopsDirectory,
    waitFor: async (page) => shopsTable(page).locator('tbody tr').first().waitFor(),
  },
  {
    name: 'shops-loading-1440',
    path: '/shops',
    prepare: mockShopsLoading,
    waitFor: async (page) => page.getByText('正在加载本地店铺…').waitFor(),
  },
  {
    name: 'shops-empty-1440',
    path: '/shops',
    prepare: async (page) => mockShopsDirectory(page, { items: [], totalCount: 0 }),
    waitFor: async (page) => page.getByText('本地暂无店铺数据').waitFor(),
  },
  {
    name: 'shops-unconfigured-1440',
    path: '/shops',
    prepare: async (page) => mockShopsDirectory(page, { configured: false }),
    waitFor: async (page) => page.getByText('尚未配置私有 ERP AccessToken').waitFor(),
  },
  {
    name: 'shops-error-1440',
    path: '/shops',
    prepare: async (page) => mockShopsDirectory(page, { shopsError: true }),
    waitFor: async (page) => page.getByRole('alert').first().waitFor(),
  },
  {
    name: 'shops-detail-1440',
    path: '/shops',
    prepare: mockShopsDirectory,
    waitFor: async (page) => shopsTable(page).locator('tbody tr').first().waitFor(),
    afterGoto: async (page) => {
      await page.getByRole('button', { name: '详情' }).first().click()
      await page.locator('#shopDetailLayer[open]').waitFor()
    },
  },
  {
    name: 'shops-page2-1440',
    path: '/shops',
    prepare: async (page) => {
      await mockShopsDirectory(page, {
        items: Array.from({ length: 45 }, (_, index) => mockShop(index + 1)),
        totalCount: 45,
      })
    },
    waitFor: async (page) => shopsTable(page).locator('tbody tr').first().waitFor(),
    afterGoto: async (page) => {
      await page.getByRole('button', { name: '下一页' }).click()
      await page.locator('[aria-current="page"]').filter({ hasText: '2' }).waitFor()
    },
  },
  {
    name: 'login-1440',
    path: '/login',
    session: 'anonymous',
    waitFor: async (page) => page.getByRole('heading', { name: '登录 OpenERP' }).waitFor(),
  },
  {
    name: 'login-error-1440',
    path: '/login',
    session: 'anonymous',
    prepare: async (page) => {
      await page.route('**/api/auth/login', (route) =>
        route.fulfill({ status: 401, json: { detail: '用户名或密码错误' } }),
      )
    },
    waitFor: async (page) => page.getByRole('heading', { name: '登录 OpenERP' }).waitFor(),
    afterGoto: async (page) => {
      await page.getByLabel('用户名').fill('admin')
      await page.getByLabel('密码', { exact: true }).fill('wrong-password')
      await page.getByRole('button', { name: '登录', exact: true }).click()
      await page.getByRole('alert').first().waitFor()
    },
  },
  {
    name: 'initialize-1440',
    path: '/initialize',
    session: 'uninitialized',
    waitFor: async (page) => page.getByRole('heading', { name: '初始化 OpenERP' }).waitFor(),
  },
  {
    name: 'initialize-error-1440',
    path: '/initialize',
    session: 'uninitialized',
    waitFor: async (page) => page.getByRole('heading', { name: '初始化 OpenERP' }).waitFor(),
    afterGoto: async (page) => {
      await page.getByRole('button', { name: '设置管理员密码' }).click()
      await page.getByRole('alert').first().waitFor()
    },
  },
  {
    name: 'notfound-1440',
    path: '/missing-visual-page',
    waitFor: async (page) => page.getByText('页面不存在', { exact: true }).waitFor(),
  },
  {
    name: 'account-menu-1440',
    path: '/',
    prepare: mockWorkspaceDashboard,
    waitFor: waitWorkspace,
    afterGoto: async (page) => {
      await page.locator('[data-testid="account-button"]').click()
      await page.locator('#accountMenu').waitFor({ state: 'visible' })
    },
  },
  {
    name: 'profile-dialog-1440',
    path: '/',
    prepare: mockWorkspaceDashboard,
    waitFor: waitWorkspace,
    afterGoto: async (page) => {
      await page.locator('[data-testid="account-button"]').click()
      await page.locator('[data-account-action="profile"]').click()
      await page.locator('#profileLayer[open]').waitFor()
    },
  },
  {
    name: 'password-dialog-1440',
    path: '/',
    prepare: mockWorkspaceDashboard,
    waitFor: waitWorkspace,
    afterGoto: async (page) => {
      await page.locator('[data-testid="account-button"]').click()
      await page.locator('[data-account-action="password"]').click()
      await page.locator('#passwordLayer[open]').waitFor()
    },
  },
]

function selectScenarios(filterValue: string | undefined): Scenario[] {
  if (!filterValue) return scenarios
  const names = new Set(filterValue.split(',').filter(Boolean))
  return scenarios.filter((scenario) => names.has(scenario.name))
}

test('采集视觉探针', async ({ browser }, testInfo) => {
  const outRoot = resolve(process.env.VISUAL_OUT ?? testInfo.outputPath('visual'))
  const screenshots = join(outRoot, 'screenshots')
  mkdirSync(screenshots, { recursive: true })
  const selected = selectScenarios(process.env.VISUAL_SCENARIOS)
  expect(selected.length).toBeGreaterThan(0)

  const captured: Array<{
    name: string
    path: string
    viewport: { width: number; height: number }
    probe: Probe
  }> = []

  for (const scenario of selected) {
    const viewport = {
      width: scenario.width ?? desktop.width,
      height: scenario.height ?? desktop.height,
    }
    const context = await browser.newContext({
      baseURL,
      viewport,
      ignoreHTTPSErrors: true,
    })
    const page = await context.newPage()
    try {
      const probe = await captureScenario(page, { ...scenario, ...viewport }, screenshots)
      captured.push({ name: scenario.name, path: scenario.path, viewport, probe })
      console.log(`captured ${scenario.name}`)
    } finally {
      await context.close()
    }
  }

  writeFileSync(
    join(outRoot, 'probe.json'),
    `${JSON.stringify({ generatedAt: new Date().toISOString(), scenarios: captured }, null, 2)}\n`,
  )
  console.log(`probe written to ${outRoot}`)
})
