import { expect, test, type Page } from '@playwright/test'
import { mkdirSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { captureScenario, desktop, type Probe, type Scenario, wide } from './visual-probe'

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

async function mockErpConfig(page: Page, overrides: Record<string, unknown> = {}): Promise<void> {
  await page.route('**/api/erp/config', (route) =>
    route.fulfill({
      json: {
        configured: true,
        baseUrl: 'https://erp.test',
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

async function mockReport(page: Page, body: unknown = reportSample): Promise<void> {
  await page.route('**/api/reports/order-snapshot?**', (route) => route.fulfill({ json: body }))
}

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
  await mockErpConfig(page, { configured })
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
  await mockErpConfig(page, { orderSyncEnabled: true })
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

const scenarios: Scenario[] = [
  {
    name: 'dashboard-1440',
    path: '/',
    waitFor: async (page) => page.locator('.dash-grid .card').first().waitFor(),
  },
  {
    name: 'dashboard-1920',
    path: '/',
    ...wide,
    waitFor: async (page) => page.locator('.dash-grid .card').first().waitFor(),
  },
  {
    name: 'dashboard-sidebar-collapsed-1440',
    path: '/',
    waitFor: async (page) => page.locator('.dash-grid .card').first().waitFor(),
    afterGoto: async (page) => {
      await page.locator('[data-testid="collapse-nav"]').click()
      await page.locator('[data-testid="sidebar"][data-rail="true"]').waitFor()
    },
  },
  {
    name: 'users-1440',
    path: '/users',
    waitFor: async (page) => page.locator('#userTableBody tr').first().waitFor(),
  },
  {
    name: 'products-1440',
    path: '/products',
    waitFor: async (page) => page.getByRole('region', { name: '产品列表' }).waitFor(),
  },
  {
    name: 'products-dialog-1440',
    path: '/products',
    waitFor: async (page) => page.getByRole('region', { name: '产品列表' }).waitFor(),
    afterGoto: async (page) => {
      await page.locator('#openProductForm').click()
      await page.locator('#productFormLayer[open]').waitFor()
    },
  },
  {
    name: 'products-dialog-error-1440',
    path: '/products',
    waitFor: async (page) => page.getByRole('region', { name: '产品列表' }).waitFor(),
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
      await mockErpConfig(page)
    },
    waitFor: async (page) =>
      page.getByRole('region', { name: '订单列表' }).locator('tbody tr').first().waitFor(),
  },
  {
    name: 'orders-empty-1440',
    path: '/orders',
    prepare: async (page) => {
      await mockOrders(page, [], 0)
      await mockErpConfig(page)
    },
    waitFor: async (page) => page.getByText('暂无同步订单').waitFor(),
  },
  {
    name: 'orders-error-1440',
    path: '/orders',
    prepare: async (page) => {
      await mockOrdersError(page)
      await mockErpConfig(page)
    },
    waitFor: async (page) => page.getByText('无法加载订单，请稍后重试。').waitFor(),
  },
  {
    name: 'orders-notice-1440',
    path: '/orders',
    prepare: async (page) => {
      await mockOrders(page)
      await mockErpConfig(page)
    },
    waitFor: async (page) =>
      page.getByRole('region', { name: '订单列表' }).locator('tbody tr').first().waitFor(),
    afterGoto: async (page) => {
      await page.locator('#openNotice').click()
      await page.locator('#noticeLayer[open]').waitFor()
    },
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
      await page.locator('.report-order-details').waitFor()
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
    name: 'settings-base-1440',
    path: '/settings',
    waitFor: async (page) => page.locator('.config-row').first().waitFor(),
  },
  {
    name: 'settings-erp-1440',
    path: '/settings',
    prepare: async (page) => {
      await page.route('**/api/erp/config', (route) =>
        route.fulfill({ json: { configured: true } }),
      )
    },
    waitFor: async (page) => page.locator('.config-row').first().waitFor(),
    afterGoto: async (page) => {
      await page.locator('[data-settings-item="ERP 接入"]').click()
      await page.locator('.erp-config-status [role="status"]').waitFor()
    },
  },
  {
    name: 'service-loading-1440',
    path: '/service-connection',
    prepare: async (page) => {
      await page.route('**/api/ping', async (route) => {
        await new Promise((resolveDelay) => setTimeout(resolveDelay, 15_000))
        await route.fulfill({ json: { message: 'pong' } }).catch(() => undefined)
      })
    },
    waitFor: async (page) => page.locator('[data-testid="service-tag"]').first().waitFor(),
  },
  {
    name: 'service-success-1440',
    path: '/service-connection',
    prepare: async (page) => {
      await page.route('**/api/ping', (route) => route.fulfill({ json: { message: 'pong' } }))
    },
    waitFor: async (page) => page.getByText('后端响应：pong').waitFor(),
  },
  {
    name: 'service-error-1440',
    path: '/service-connection',
    prepare: async (page) => {
      await page.route('**/api/ping', (route) => route.fulfill({ status: 500, body: 'boom' }))
    },
    waitFor: async (page) => page.getByText('连接失败').waitFor(),
  },
  {
    name: 'shops-default-1440',
    path: '/shops',
    prepare: mockShopsDirectory,
    waitFor: async (page) => page.locator('.shops-table tbody tr').first().waitFor(),
  },
  {
    name: 'shops-default-1920',
    path: '/shops',
    ...wide,
    prepare: mockShopsDirectory,
    waitFor: async (page) => page.locator('.shops-table tbody tr').first().waitFor(),
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
    waitFor: async (page) => page.locator('.page-shops .notice.red').first().waitFor(),
  },
  {
    name: 'shops-detail-1440',
    path: '/shops',
    prepare: mockShopsDirectory,
    waitFor: async (page) => page.locator('.shops-table tbody tr').first().waitFor(),
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
    waitFor: async (page) => page.locator('.shops-table tbody tr').first().waitFor(),
    afterGoto: async (page) => {
      await page.getByRole('button', { name: '下一页' }).click()
      await page.locator('.page-num').filter({ hasText: '2' }).waitFor()
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
    name: 'newdev-1440',
    path: '/newdev',
    waitFor: async (page) => page.getByRole('heading', { name: '新品开发' }).waitFor(),
  },
  {
    name: 'account-menu-1440',
    path: '/',
    waitFor: async (page) => page.locator('.dash-grid .card').first().waitFor(),
    afterGoto: async (page) => {
      await page.locator('[data-testid="account-button"]').click()
      await page.locator('#accountMenu').waitFor({ state: 'visible' })
    },
  },
  {
    name: 'profile-dialog-1440',
    path: '/',
    waitFor: async (page) => page.locator('.dash-grid .card').first().waitFor(),
    afterGoto: async (page) => {
      await page.locator('[data-testid="account-button"]').click()
      await page.locator('[data-account-action="profile"]').click()
      await page.locator('#profileLayer[open]').waitFor()
    },
  },
  {
    name: 'password-dialog-1440',
    path: '/',
    waitFor: async (page) => page.locator('.dash-grid .card').first().waitFor(),
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
