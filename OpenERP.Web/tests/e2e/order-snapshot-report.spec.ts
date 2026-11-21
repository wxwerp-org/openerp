import { devices, expect, test, type Page } from '@playwright/test'
import { bearerHeaders, enterWorkspace } from './authentication'
import { dataTrendFixture, salesFixture } from '../reportFixtures'

// 浏览器截图使用固定接口样本，真实业务数据由后端验收覆盖。
const sampleShop = {
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
  shops: [sampleShop],
  groups: [{ shop: sampleShop, orderCount: 1, itemQuantity: 5 }],
  orders: [
    {
      id: 'sample-order-30',
      number: 'ERP-ORDER-30',
      shop: sampleShop,
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

test.beforeEach(async ({ page }) => {
  await enterWorkspace(page)
})

/** 订单明细卡底部的分页条（AppPager 以 footer 渲染）。 */
function ordersPager(page: Page) {
  return page.locator('[data-testid="report-pagination"]')
}

/** 报表内容滚动区，负责页面纵向滚动。 */
function reportScroll(page: Page) {
  return page.locator('[data-testid="report-scroll"]')
}

async function openReport(page: Page): Promise<void> {
  // 「订单管理」分组默认展开，直接点击商品量报表即可；不经过工作台避免其报表请求混入用例计数。
  await page.goto('/reports/orders')
  await expect(page).toHaveURL(/\/reports\/orders$/)
  await expect(page.locator('#page-order-snapshot-report')).toBeVisible()
}

test('Given 商品量报表有平台店铺和订单样本 When 从统计导航打开并展开订单 Then 展示筛选、快照口径与完整商品行', async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  const requests: URL[] = []
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    requests.push(new URL(route.request().url()))
    await route.fulfill({ json: reportSample })
  })

  await page.goto('/orders')
  await expect(page.locator('#page-orders')).toBeVisible()
  await page.locator('[data-testid="side-nav"] [data-nav-page="order-report"]').click()
  await expect(page).toHaveURL(/\/reports\/orders$/)
  await expect(page.locator('#page-order-snapshot-report')).toBeVisible()
  await expect(page.locator('[data-testid="page-tab"] [aria-current="page"]')).toHaveText(
    '商品量报表',
  )

  // 页签页标题只由顶部页签承载，页内不再渲染可见标题。
  const headings = page.getByRole('heading', { level: 1, name: '商品量报表' })
  await expect(headings).toHaveCount(1)
  await expect(page.locator('#page-order-snapshot-report h1')).toHaveCount(1)
  // 视觉隐藏但保留无障碍名称：sr-only 裁剪为 1×1 像素。
  const headingBox = await headings.boundingBox()
  expect(headingBox).not.toBeNull()
  expect(headingBox!.width).toBeLessThanOrEqual(1)
  expect(headingBox!.height).toBeLessThanOrEqual(1)

  await expect(
    page.getByText('按本地来源订单计数；件数为当前商品行数量合计，包含禁用/追加行。'),
  ).toBeVisible()
  await expect(page.locator('[data-testid="report-scope"]')).toHaveAttribute('role', 'note')
  await expect(page.locator('[data-testid="report-scope"] use')).toHaveAttribute('href', '#i-info')
  await expect(page.locator('[aria-label="订单总数"]')).toHaveText('1')
  await expect(page.locator('[aria-label="商品件数"]')).toHaveText('5')
  await expect(page.locator('[aria-label="有订单店铺数"]')).toHaveText('1')
  await expect(page.locator('[aria-label="平均每单件数"]')).toHaveText('5')

  // 工作台骨架：筛选条承载平台/店铺筛选与工具栏，概览与图表卡片位于同一分析网格内。
  const searchbar = page.locator('[data-testid="report-searchbar"]')
  await expect(searchbar.locator('[data-testid="report-platform-filter"]')).toHaveCount(1)
  await expect(searchbar.locator('[data-testid="report-toolbar"]')).toHaveCount(1)
  const chrome = await page.locator('#page-order-snapshot-report').evaluate((shell) => {
    const overview = shell.querySelector('[data-testid="report-overview"]') as HTMLElement
    const scroll = shell.querySelector('[data-testid="report-scroll"]') as HTMLElement
    const gridEl = scroll.firstElementChild as HTMLElement
    const rank = shell.querySelector('[data-testid="report-shop-ranking"]') as HTMLElement
    const dist = shell.querySelector('[data-testid="report-platform-distribution"]') as HTMLElement
    return {
      canvas: getComputedStyle(scroll).backgroundColor,
      gap: getComputedStyle(gridEl).columnGap,
      overviewBackground: getComputedStyle(overview).backgroundColor,
      overviewRadius: getComputedStyle(overview).borderTopLeftRadius,
      rankColumnStart: getComputedStyle(rank).gridColumnStart,
      distColumnStart: getComputedStyle(dist).gridColumnStart,
    }
  })
  expect(chrome.canvas).toBe('rgb(242, 245, 250)')
  expect(chrome.gap).toBe('12px')
  expect(chrome.overviewBackground).toBe('rgb(255, 255, 255)')
  expect(chrome.overviewRadius).toBe('6px')
  expect(chrome.rankColumnStart).toBe('span 8')
  expect(chrome.distColumnStart).toBe('span 4')

  // 图表组件渲染真实快照数值：排行按商品件数取前 5，分布按平台聚合占比。
  const rankCard = page.locator('[data-testid="report-shop-ranking"]')
  const distCard = page.locator('[data-testid="report-platform-distribution"]')
  await expect(rankCard).toContainText('按商品件数取前 5 家店铺')
  await expect(rankCard).toContainText('店铺 30')
  await expect(distCard).toContainText('总订单数')
  await expect(distCard).toContainText('100.0%')
  await expect(distCard).toContainText('Temu 半托管')

  // 图表之外仍保留完整分店汇总表。
  await expect(page.locator('[data-testid="report-groups-table"] tbody tr')).toHaveCount(1)

  // 表格复用列表页外壳：滚动区可聚焦并带区域语义，表格有显式列宽。
  const ordersRegion = page.getByRole('region', { name: '订单明细表格' })
  await expect(ordersRegion).toHaveCount(1)
  await expect(ordersRegion).toHaveAttribute('tabindex', '0')
  await expect(ordersRegion).toHaveAttribute('aria-label', '订单明细表格')
  await expect(
    page
      .locator('[data-testid="report-orders-panel"]')
      .getByRole('region', { name: '订单明细表格' }),
  ).toHaveCount(1)
  await expect(page.locator('[data-testid="report-groups-table"] colgroup col')).toHaveCount(4)
  await expect(page.locator('[data-testid="report-orders-table"] colgroup col')).toHaveCount(8)

  // 刷新、分页与下拉统一使用 AppIcon；刷新为图标按钮。
  const refresh = page.getByRole('button', { name: '刷新' })
  await expect(refresh.locator('svg')).toHaveCount(1)
  await expect(refresh.locator('use')).toHaveAttribute('href', '#i-refresh')
  await expect(page.getByRole('button', { name: '上一页' }).locator('use')).toHaveAttribute(
    'href',
    '#i-chev-left',
  )
  await expect(page.getByRole('button', { name: '下一页' }).locator('use')).toHaveAttribute(
    'href',
    '#i-chev-right',
  )
  await expect(page.getByLabel('每页条数')).toBeVisible()
  await expect(page.getByLabel('前往页码')).toBeVisible()
  await expect(page.locator('label:has(select[aria-label="每页条数"]) use')).toHaveAttribute(
    'href',
    '#i-chev-down',
  )
  await expect(page.locator('[data-testid="report-platform-filter"] use')).toHaveAttribute(
    'href',
    '#i-chev-down',
  )
  expect(
    await page.getByLabel('平台筛选').evaluate((element) => getComputedStyle(element).appearance),
  ).toBe('none')

  // 工作台外壳：页面自身不滚动，滚动交给分析区；分页位于订单卡底部且在视口内。
  const paginationBox = await ordersPager(page).boundingBox()
  expect(paginationBox).not.toBeNull()
  expect(paginationBox!.y + paginationBox!.height).toBeLessThanOrEqual(900)
  const pageShell = await page.locator('#page-order-snapshot-report').evaluate((element) => ({
    overflowY: getComputedStyle(element).overflowY,
  }))
  expect(pageShell.overflowY).toBe('hidden')

  await page.getByLabel('平台筛选').selectOption('TemuPop')
  await page.locator('summary[aria-label="店铺筛选"]').click()
  await page.locator('input[value="wxw-erp/TemuPop/30"]').check()
  const filteredRequest = requests.at(-1)!
  expect(filteredRequest.searchParams.get('platform')).toBe('TemuPop')
  expect(filteredRequest.searchParams.getAll('shops')).toEqual(['wxw-erp/TemuPop/30'])
  await page.locator('summary[aria-label="店铺筛选"]').click()

  await page.getByRole('button', { name: '展开订单 ERP-ORDER-30 商品明细' }).click()
  const expanded = page.locator('[data-testid="report-order-details"]')
  await expect(expanded.getByText('PO-ORDER-30')).toBeVisible()
  await expect(expanded.getByText('SKU-30')).toBeVisible()
  await expect(expanded.getByText('禁用')).toBeVisible()
  await expect(expanded.getByText('追加')).toBeVisible()
  await expect(page.getByText('本地订单件数 5')).toBeVisible()

  await page.screenshot({
    path: testInfo.outputPath('order-report-mock-sample.png'),
    fullPage: true,
  })
})

test('Given 报表接口返回空结果 When 查询完成 Then 空态使用共享外壳且分页禁用', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    await route.fulfill({
      json: {
        ...reportSample,
        summary: { orderCount: 0, itemQuantity: 0 },
        groups: [],
        orders: [],
        page: 1,
        pageCount: 1,
      },
    })
  })

  await openReport(page)
  await expect(page.locator('[data-testid="report-groups-empty"]')).toContainText('暂无分店汇总')
  await expect(page.locator('[data-testid="report-orders-empty"]')).toContainText(
    '暂无符合条件的本地订单',
  )
  await expect(page.getByRole('button', { name: '上一页' })).toBeDisabled()
  await expect(page.getByRole('button', { name: '下一页' })).toBeDisabled()
  await expect(page.locator('[aria-label="订单总数"]')).toHaveText('0')
})

test('Given 报表数据暂不可用 When 加载失败后重试 Then 错误提示带图标并可恢复报表', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  let failed = true
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    if (failed) {
      await route.fulfill({
        status: 503,
        json: { title: '报表数据暂不可用' },
      })
      return
    }
    await route.fulfill({ json: reportSample })
  })

  await openReport(page)
  const alert = page.getByRole('alert')
  await expect(alert).toContainText('报表数据暂不可用')
  await expect(alert.locator('use')).toHaveAttribute('href', '#i-warning')
  await expect(page.locator('[data-testid="report-orders-empty"]')).toContainText('报表加载失败')

  failed = false
  await page.getByRole('button', { name: '重试' }).click()
  await expect(page.getByRole('alert')).toHaveCount(0)
  await expect(page.getByText('PO-ORDER-30')).toBeVisible()
})

function manyReport() {
  const groups = Array.from({ length: 8 }, (_, index) => {
    const shop = {
      ...sampleShop,
      shopId: String(30 + index),
      key: `wxw-erp/TemuPop/${30 + index}`,
      label: `店铺 ${30 + index}`,
    }
    return { shop, orderCount: index + 1, itemQuantity: (index + 1) * 5 }
  })
  const orders = Array.from({ length: 20 }, (_, index) => ({
    ...reportSample.orders[0],
    id: `sample-order-${index}`,
    number: `ERP-ORDER-${index}`,
    itemQuantity: (index % 5) + 1,
  }))
  return {
    ...reportSample,
    summary: { orderCount: 128, itemQuantity: 642 },
    shops: groups.map((group) => group.shop),
    groups,
    orders,
    page: 1,
    pageCount: 7,
  }
}

test('Given 视口高度仅 500px When 打开多分组报表 Then 滚动主区域可到达分页且表格区可滚动到末尾', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 500 })
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    await route.fulfill({ json: manyReport() })
  })

  await openReport(page)

  // 工作台外壳：分析区的滚动容器承担页面纵向滚动。
  const scrollMetrics = await reportScroll(page).evaluate((element) => ({
    overflowY: getComputedStyle(element).overflowY,
    scrollHeight: element.scrollHeight,
    clientHeight: element.clientHeight,
  }))
  expect(scrollMetrics.overflowY).toBe('auto')
  expect(scrollMetrics.scrollHeight).toBeGreaterThan(scrollMetrics.clientHeight)

  // 两个表格区在自身可聚焦区域内滚动。
  for (const name of ['分店汇总表格', '订单明细表格']) {
    const metrics = await page.getByRole('region', { name }).evaluate((element) => {
      element.scrollTop = element.scrollHeight
      return {
        scrollTop: element.scrollTop,
        scrollHeight: element.scrollHeight,
        clientHeight: element.clientHeight,
      }
    })
    expect(metrics.scrollHeight).toBeGreaterThan(metrics.clientHeight)
    expect(metrics.scrollTop + metrics.clientHeight).toBe(metrics.scrollHeight)
  }

  // 主区域滚动到底后分页仍完整可见。
  await reportScroll(page).evaluate((element) => {
    element.scrollTop = element.scrollHeight
  })
  const pagination = await ordersPager(page).boundingBox()
  expect(pagination).not.toBeNull()
  expect(pagination!.y + pagination!.height).toBeLessThanOrEqual(500)
})

test('Given 跳页请求尚未返回 When 输入框因加载禁用而失焦 Then 仍停留在目标页且不重复请求', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  const requestedPages: string[] = []
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    const page = new URL(route.request().url()).searchParams.get('page') ?? ''
    requestedPages.push(page)
    await new Promise((resolve) => setTimeout(resolve, 300))
    await route.fulfill({ json: { ...manyReport(), page: Number(page) } })
  })

  await openReport(page)
  await page.getByLabel('前往页码').fill('3')
  await page.getByLabel('前往页码').press('Enter')
  await expect(ordersPager(page).locator('[aria-current="page"]')).toHaveText('3')

  expect(requestedPages).toEqual(['1', '3'])
})

// 按请求页码回显的分页 mock，供窄屏与触屏用例翻页。
async function routePagedReport(page: Page): Promise<void> {
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    const requested = Number(new URL(route.request().url()).searchParams.get('page') ?? '1')
    await route.fulfill({ json: { ...manyReport(), page: requested } })
  })
}

test('Given 375×812 窄屏视口 When 打开商品量报表 Then 卡片单列、表格区内滚动且页面无横向溢出', async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await routePagedReport(page)

  await openReport(page)

  // 报表路由解除桌面最小宽度，页面本身不出现横向溢出。
  const pageMetrics = await page.evaluate(() => ({
    bodyMinWidth: getComputedStyle(document.body).minWidth,
    scrollWidth: document.documentElement.scrollWidth,
    innerWidth: window.innerWidth,
  }))
  expect(pageMetrics.bodyMinWidth).toBe('0px')
  expect(pageMetrics.scrollWidth).toBeLessThanOrEqual(pageMetrics.innerWidth)

  // 窄屏收敛为 56px 导航轨道（侧边栏宽度带过渡，轮询到稳定值）。
  await expect
    .poll(async () =>
      page
        .locator('[data-testid="sidebar"]')
        .evaluate((element) => element.getBoundingClientRect().width),
    )
    .toBe(56)

  const layout = await page.evaluate(() => {
    const rank = document.querySelector('[data-testid="report-shop-ranking"]') as HTMLElement
    const dist = document.querySelector(
      '[data-testid="report-platform-distribution"]',
    ) as HTMLElement
    const overview = document.querySelector(
      '[data-testid="report-overview"] > div:last-child',
    ) as HTMLElement
    const ordersScroll = document.querySelector('[aria-label="订单明细表格"]') as HTMLElement
    const groupsScroll = document.querySelector('[aria-label="分店汇总表格"]') as HTMLElement
    return {
      rankColumn: getComputedStyle(rank).gridColumnStart,
      distColumn: getComputedStyle(dist).gridColumnStart,
      distBelowRank: dist.getBoundingClientRect().top >= rank.getBoundingClientRect().bottom - 1,
      overviewColumns: getComputedStyle(overview).gridTemplateColumns.split(' ').length,
      ordersScrollableX: ordersScroll.scrollWidth > ordersScroll.clientWidth,
      groupsScrollableX: groupsScroll.scrollWidth > groupsScroll.clientWidth,
    }
  })
  expect(layout.rankColumn).toBe('span 12')
  expect(layout.distColumn).toBe('span 12')
  expect(layout.distBelowRank).toBe(true)
  expect(layout.overviewColumns).toBe(2)
  expect(layout.ordersScrollableX).toBe(true)
  expect(layout.groupsScrollableX).toBe(true)

  // 筛选、展开、分页在窄屏仍完整可用。
  await page.getByLabel('平台筛选').selectOption('TemuPop')
  await page.getByRole('button', { name: '展开订单 ERP-ORDER-0 商品明细' }).click()
  await expect(page.locator('[data-testid="report-order-details"]')).toBeVisible()
  await page.getByRole('button', { name: '下一页' }).click()
  await expect(ordersPager(page).locator('[aria-current="page"]')).toHaveText('2')
})

test('Given 375px 下打开过报表 When 离开报表路由 Then 恢复桌面最小宽度约束', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    await route.fulfill({ json: reportSample })
  })

  await openReport(page)
  expect(await page.evaluate(() => getComputedStyle(document.body).minWidth)).toBe('0px')

  // 单页应用内离开报表路由：进入非流式页面（订单列表）即恢复桌面最小宽度约束。
  await page.locator('[data-testid="tab-add"]').click()
  await page.locator('#tabMenu').getByRole('link', { name: '订单列表' }).click()
  await expect(page).toHaveURL(/\/orders$/)
  await expect(page.locator('#page-order-snapshot-report')).toHaveCount(0)
  const restored = await page.evaluate(() => ({
    bodyMinWidth: getComputedStyle(document.body).minWidth,
    scrollWidth: document.documentElement.scrollWidth,
    innerWidth: window.innerWidth,
  }))
  expect(restored.bodyMinWidth).toBe('1440px')
  expect(restored.scrollWidth).toBeGreaterThan(restored.innerWidth)
})

test('Given 1440px 桌面视口 When 打开报表 Then 保持双列卡片且页面无横向溢出', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    await route.fulfill({ json: manyReport() })
  })

  await openReport(page)
  const pageMetrics = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    innerWidth: window.innerWidth,
  }))
  expect(pageMetrics.scrollWidth).toBeLessThanOrEqual(pageMetrics.innerWidth)

  const layout = await page.evaluate(() => {
    const rank = document.querySelector('[data-testid="report-shop-ranking"]') as HTMLElement
    const dist = document.querySelector(
      '[data-testid="report-platform-distribution"]',
    ) as HTMLElement
    const overview = document.querySelector(
      '[data-testid="report-overview"] > div:last-child',
    ) as HTMLElement
    return {
      rankColumn: getComputedStyle(rank).gridColumnStart,
      distColumn: getComputedStyle(dist).gridColumnStart,
      overviewColumns: getComputedStyle(overview).gridTemplateColumns.split(' ').length,
    }
  })
  expect(layout.rankColumn).toBe('span 8')
  expect(layout.distColumn).toBe('span 4')
  expect(layout.overviewColumns).toBe(4)
})

test.describe('触屏窄屏', () => {
  // describe 内 test.use 不允许切换浏览器，因此只取 iPhone X 的视口与触控字段。
  test.use({
    viewport: devices['iPhone X'].viewport,
    isMobile: devices['iPhone X'].isMobile,
    hasTouch: devices['iPhone X'].hasTouch,
    deviceScaleFactor: devices['iPhone X'].deviceScaleFactor,
    userAgent: devices['iPhone X'].userAgent,
  })

  test('Given 触屏窄屏 When 操作报表 Then 交互目标至少 44px 且筛选分页可用', async ({ page }) => {
    await routePagedReport(page)

    await openReport(page)
    expect(await page.evaluate(() => matchMedia('(pointer: coarse)').matches)).toBe(true)

    const targets = page.evaluate(() => {
      const box = (selector: string) => {
        const element = document.querySelector(selector) as HTMLElement | null
        return element ? element.getBoundingClientRect().height : 0
      }
      return {
        expand: box('[data-testid="report-orders-table"] button[aria-label^="展开订单"]'),
        pageButton: box('[data-testid="report-pagination"] button[aria-label="第 2 页"]'),
        refresh: box('[data-testid="report-toolbar"] button[aria-label="刷新"]'),
        navLink: box('[data-testid="side-nav"] [data-nav-page="dashboard"]'),
        navGroupHead: box('[data-testid="sidebar"] button[aria-expanded]'),
      }
    })
    expect((await targets).expand).toBeGreaterThanOrEqual(44)
    expect((await targets).pageButton).toBeGreaterThanOrEqual(44)
    expect((await targets).refresh).toBeGreaterThanOrEqual(44)
    expect((await targets).navLink).toBeGreaterThanOrEqual(44)
    expect((await targets).navGroupHead).toBeGreaterThanOrEqual(44)

    await page.getByLabel('平台筛选').selectOption('TemuPop')
    await page.getByRole('button', { name: '下一页' }).click()
    await expect(ordersPager(page).locator('[aria-current="page"]')).toHaveText('2')
  })
})

test.describe('宽视口触屏', () => {
  // 粗指针但视口大于窄屏断点：桌面侧栏不得被 44px 规则压窄。
  test.use({
    viewport: { width: 1024, height: 768 },
    isMobile: true,
    hasTouch: true,
    deviceScaleFactor: 2,
  })

  test('Given 宽视口粗指针 When 打开报表 Then 桌面侧栏项目保持 100%×38px', async ({ page }) => {
    await page.route('**/api/reports/order-snapshot?**', async (route) => {
      await route.fulfill({ json: reportSample })
    })

    await openReport(page)
    expect(await page.evaluate(() => matchMedia('(pointer: coarse)').matches)).toBe(true)

    const nav = await page.evaluate(() => {
      const sidebar = document.querySelector('[data-testid="sidebar"]') as HTMLElement
      const link = document.querySelector(
        '[data-testid="side-nav"] [data-nav-page="dashboard"]',
      ) as HTMLElement
      const linkBox = link.getBoundingClientRect()
      return {
        sidebarWidth: sidebar.getBoundingClientRect().width,
        linkWidth: linkBox.width,
        linkHeight: linkBox.height,
      }
    })
    expect(nav.sidebarWidth).toBe(200)
    // 导航项宽度为侧栏内容区 100%（含内边距的整行），只要求不被压成 44px 方块。
    expect(nav.linkWidth).toBeGreaterThan(100)
    expect(nav.linkHeight).toBe(38)
  })
})

test('Given 报表已加载 When 用键盘遍历 Then 焦点按筛选图表表格分页顺序移动且表格区焦点可见', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    await route.fulfill({ json: manyReport() })
  })

  await openReport(page)

  // 从平台筛选出发，逐次 Tab 记录焦点目标；键盘触发的焦点必须命中 :focus-visible。
  await page.getByLabel('平台筛选').focus()
  const readStep = () =>
    page.evaluate(() => {
      const element = document.activeElement as HTMLElement
      const style = getComputedStyle(element)
      return {
        label:
          element.getAttribute('aria-label') ??
          element.closest('[aria-label]')?.getAttribute('aria-label') ??
          element.textContent?.trim().slice(0, 8) ??
          element.tagName,
        visible: element.matches(':focus-visible'),
        outlineWidth: style.outlineWidth,
        outlineOffset: style.outlineOffset,
      }
    })
  const steps: Array<Awaited<ReturnType<typeof readStep>>> = []
  for (let index = 0; index < 9; index += 1) {
    await page.keyboard.press('Tab')
    steps.push(await readStep())
  }

  expect(steps.map((step) => step.label)).toEqual([
    '店铺筛选',
    '清除筛选',
    '刷新',
    '排行指标',
    '排行指标',
    '分店汇总表格',
    '订单明细表格',
    '展开订单 ERP-ORDER-0 商品明细',
    '展开订单 ERP-ORDER-1 商品明细',
  ])
  // 原生控件可保留浏览器描边；表格滚动区使用 2px 向内描边，避免被卡片裁剪。
  for (const step of steps) {
    expect(step.visible).toBe(true)
    expect(step.outlineWidth).not.toBe('0px')
  }
  expect(steps[5]!.outlineWidth).toBe('2px')
  expect(steps[5]!.outlineOffset).toBe('-2px')
  expect(steps[6]!.outlineWidth).toBe('2px')
  expect(steps[6]!.outlineOffset).toBe('-2px')

  // 当前样本还有 18 个展开按钮；上一页在首页禁用，因此下一个分页目标是第 2 页。
  for (let index = 0; index < 19; index += 1) {
    await page.keyboard.press('Tab')
  }
  expect((await readStep()).label).toBe('第 2 页')

  const tail: string[] = []
  for (let index = 0; index < 5; index += 1) {
    await page.keyboard.press('Tab')
    tail.push((await readStep()).label)
  }
  expect(tail).toEqual(['第 3 页', '第 7 页', '下一页', '每页条数', '前往页码'])

  // 描边必须真实绘制：`outline-none` 会写入 --tw-outline-style，只有补 outline-solid 才能画出焦点环。
  const focusRing = async (label: string) => {
    await page.getByLabel(label).focus()
    await page.keyboard.press('Tab')
    await page.keyboard.press('Shift+Tab')
    return page.getByLabel(label).evaluate((element) => {
      const style = getComputedStyle(element)
      return { style: style.outlineStyle, width: style.outlineWidth, color: style.outlineColor }
    })
  }
  for (const label of ['平台筛选', '每页条数', '前往页码']) {
    expect(await focusRing(label)).toEqual({
      style: 'solid',
      width: '2px',
      color: 'rgb(0, 91, 245)',
    })
  }
})

test('Given 真实宿主本地订单 When 打开商品量报表 Then 页面与接口的订单数件数一致且覆盖平台店铺图', async ({
  page,
}, testInfo) => {
  test.skip(
    process.env.OPENERP_TEST_LOCAL_ORDERS !== '1',
    '真实报表断言依赖隔离宿主种子订单，不注册报表路由拦截。',
  )

  await openReport(page)
  await expect(page.locator('[data-testid="report-orders-table"] tbody tr')).not.toHaveCount(0)

  // 真实模式：不注册报表路由拦截，直接比对真实 API 与页面渲染。
  const response = await page.request.get('/api/reports/order-snapshot?page=1&pageSize=20', {
    headers: await bearerHeaders(page.request),
  })
  expect(response.ok()).toBe(true)
  const report = (await response.json()) as {
    summary: { orderCount: number; itemQuantity: number }
    groups: unknown[]
    orders: unknown[]
  }

  const statText = async (label: string) =>
    Number((await page.locator(`[aria-label="${label}"]`).innerText()).replace(/,/g, ''))
  expect(await statText('订单总数')).toBe(report.summary.orderCount)
  expect(await statText('商品件数')).toBe(report.summary.itemQuantity)
  await expect(page.locator('[data-testid="report-orders-table"] tbody tr')).toHaveCount(
    report.orders.length,
  )
  await expect(page.locator('[data-testid="report-groups-table"] tbody tr')).toHaveCount(
    report.groups.length,
  )

  // 种子覆盖两个平台店铺：平台分布与店铺排行均有独立条目（占比在可视列表和无障碍表各一次，只数可视行）。
  const distList = page.locator('[data-testid="report-platform-legend"]')
  await expect(distList.getByText('50.0%')).toHaveCount(2)
  await expect(distList).toContainText('Temu 半托管')
  await expect(distList.getByText('Temu', { exact: true })).toHaveCount(1)
  await expect(page.locator('[data-testid="report-shop-ranking"]')).toContainText('店铺 30')
  await expect(page.locator('[data-testid="report-shop-ranking"]')).toContainText('店铺 31')

  await page.screenshot({
    path: testInfo.outputPath('order-report-real-desktop.png'),
    fullPage: true,
  })

  await page.setViewportSize({ width: 375, height: 812 })
  // 侧栏宽度带过渡，等收敛到 56px 轨道再截图；移开鼠标避免悬停浮层遮挡。
  await expect
    .poll(async () =>
      page
        .locator('[data-testid="sidebar"]')
        .evaluate((element) => element.getBoundingClientRect().width),
    )
    .toBe(56)
  await page.mouse.move(320, 700)
  // 点击导航后焦点留在子项，focus-within 会保持浮层，截图前移除焦点。
  await page.evaluate(() => {
    if (document.activeElement instanceof HTMLElement) document.activeElement.blur()
  })
  await expect
    .poll(async () => page.evaluate(() => document.documentElement.scrollWidth))
    .toBeLessThanOrEqual(375)
  await page.screenshot({
    path: testInfo.outputPath('order-report-real-mobile.png'),
    fullPage: true,
  })
})

test('Given 空报表 When 表格没有数据 Then 空态提示完整位于表格内容区内', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    await route.fulfill({
      json: {
        ...reportSample,
        summary: { orderCount: 0, itemQuantity: 0 },
        groups: [],
        orders: [],
        page: 1,
        pageCount: 1,
      },
    })
  })

  await openReport(page)

  const panels = [
    {
      panel: '[data-testid="report-groups-panel"]',
      area: '[data-testid="report-groups-area"]',
      empty: '[data-testid="report-groups-empty"]',
    },
    {
      panel: '[data-testid="report-orders-panel"]',
      area: '[data-testid="report-orders-area"]',
      empty: '[data-testid="report-orders-empty"]',
    },
  ]
  for (const { panel, area, empty } of panels) {
    const geometry = await page.locator(panel).evaluate(
      (element, selectors) => {
        const areaBox = element.querySelector(selectors.area)!.getBoundingClientRect()
        const messageBox = element.querySelector(`${selectors.empty} span`)!.getBoundingClientRect()
        return {
          areaTop: areaBox.top,
          areaBottom: areaBox.bottom,
          messageTop: messageBox.top,
          messageBottom: messageBox.bottom,
        }
      },
      { area, empty },
    )
    expect(geometry.messageTop).toBeGreaterThanOrEqual(geometry.areaTop)
    expect(geometry.messageBottom).toBeLessThanOrEqual(geometry.areaBottom)
  }
})

test('Given 850px 桌面 When 分析区保持双列 Then 圆环与中心总数不被卡片裁切', async ({ page }) => {
  await page.setViewportSize({ width: 850, height: 900 })
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    await route.fulfill({ json: manyReport() })
  })

  await openReport(page)

  const geometry = await page
    .locator('[data-testid="report-platform-distribution"]')
    .evaluate((card) => {
      const cardBox = card.getBoundingClientRect()
      const chart = card.querySelector('[data-testid="report-platform-chart"]') as SVGElement
      const chartBox = chart.getBoundingClientRect()
      const centerBox = (chart.nextElementSibling as HTMLElement).getBoundingClientRect()
      return {
        cardLeft: cardBox.left,
        cardRight: cardBox.right,
        chartLeft: chartBox.left,
        chartRight: chartBox.right,
        centerLeft: centerBox.left,
        centerRight: centerBox.right,
      }
    })
  expect(geometry.chartLeft).toBeGreaterThanOrEqual(geometry.cardLeft)
  expect(geometry.chartRight).toBeLessThanOrEqual(geometry.cardRight)
  expect(geometry.centerLeft).toBeGreaterThanOrEqual(geometry.cardLeft)
  expect(geometry.centerRight).toBeLessThanOrEqual(geometry.cardRight)
})

test('Given 1920px 桌面 When 平台分布横向排列 Then 圆环和分布列表均完整可见', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    await route.fulfill({ json: manyReport() })
  })

  await openReport(page)

  const geometry = await page
    .locator('[data-testid="report-platform-distribution"]')
    .evaluate((card) => {
      const cardBox = card.getBoundingClientRect()
      const chart = card.querySelector('[data-testid="report-platform-chart"]') as SVGElement
      const chartBox = chart.getBoundingClientRect()
      const listBox = (
        card.querySelector('[data-testid="report-platform-legend"]') as HTMLElement
      ).getBoundingClientRect()
      return {
        cardLeft: cardBox.left,
        cardRight: cardBox.right,
        chartLeft: chartBox.left,
        chartRight: chartBox.right,
        listLeft: listBox.left,
        listRight: listBox.right,
        listWidth: listBox.width,
      }
    })
  expect(geometry.chartLeft).toBeGreaterThanOrEqual(geometry.cardLeft)
  expect(geometry.chartRight).toBeLessThanOrEqual(geometry.listLeft)
  expect(geometry.listWidth).toBeGreaterThan(0)
  expect(geometry.listRight).toBeLessThanOrEqual(geometry.cardRight)
})

test('Given 细指针窄窗口 When 展示多页报表 Then 分页控件换行且翻页可用', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await routePagedReport(page)

  await openReport(page)
  expect(await page.evaluate(() => matchMedia('(pointer: fine)').matches)).toBe(true)

  const controls = await ordersPager(page).evaluate((footer) => {
    const footerBox = footer.getBoundingClientRect()
    return Array.from(footer.querySelectorAll('button, label, input')).map((control) => {
      const box = control.getBoundingClientRect()
      return {
        name: control.getAttribute('aria-label') ?? control.textContent?.trim(),
        left: box.left,
        right: box.right,
        footerLeft: footerBox.left,
        footerRight: footerBox.right,
      }
    })
  })
  for (const control of controls) {
    expect(control.left, `${control.name} 左边界`).toBeGreaterThanOrEqual(control.footerLeft)
    expect(control.right, `${control.name} 右边界`).toBeLessThanOrEqual(control.footerRight)
  }

  await page.getByRole('button', { name: '下一页' }).click()
  await expect(ordersPager(page).locator('[aria-current="page"]')).toHaveText('2')
  await page.getByRole('button', { name: '上一页' }).click()
  await expect(ordersPager(page).locator('[aria-current="page"]')).toHaveText('1')
})

test('Given 667×375 矮屏 When 导航至设置 Then 尾部分组可达且子菜单浮层不被裁切', async ({
  page,
}) => {
  await page.setViewportSize({ width: 667, height: 375 })
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    await route.fulfill({ json: reportSample })
  })
  await page.goto('/reports/orders')
  await expect(page.locator('#page-order-snapshot-report')).toBeVisible()

  // 收敛后的导航条目变少，矮屏轨道下全部可见：尾部「即将上线」分组无需滚动即可达。
  const navigation = page.locator('[data-testid="side-nav"]')
  expect(await navigation.evaluate((element) => getComputedStyle(element).overflowY)).toBe('auto')
  await expect(page.locator('[data-group="upcoming"] button[aria-expanded]')).toBeInViewport()

  const settings = page.locator('[data-group="settings"] button[aria-expanded]')
  await settings.evaluate((element) => element.scrollIntoView({ block: 'center' }))
  await settings.hover()
  const submenu = page.locator('[data-testid="nav-flyout"]')
  await expect(submenu).toBeVisible()
  await expect
    .poll(async () => submenu.evaluate((element) => element.getBoundingClientRect().bottom))
    .toBeLessThanOrEqual(375)
  const submenuBox = await submenu.boundingBox()
  expect(submenuBox).not.toBeNull()
  expect(submenuBox!.x).toBeGreaterThanOrEqual(48)
  expect(submenuBox!.x + submenuBox!.width).toBeLessThanOrEqual(667)
  expect(submenuBox!.y).toBeGreaterThanOrEqual(0)
  expect(submenuBox!.y + submenuBox!.height).toBeLessThanOrEqual(375)
})

test('Given 窄屏悬停子菜单 When 指针从分组缓慢移入浮层 Then 菜单保持打开并可进入业务配置', async ({
  page,
}) => {
  await page.setViewportSize({ width: 667, height: 375 })
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    await route.fulfill({ json: reportSample })
  })
  await page.goto('/reports/orders')
  await expect(page.locator('#page-order-snapshot-report')).toBeVisible()

  const settings = page.locator('[data-group="settings"] button[aria-expanded]')
  await settings.evaluate((element) => element.scrollIntoView({ block: 'center' }))
  await settings.hover()
  const submenu = page.locator('[data-testid="nav-flyout"]')
  await expect(submenu).toBeVisible()
  await expect
    .poll(async () => submenu.evaluate((element) => element.getBoundingClientRect().bottom))
    .toBeLessThanOrEqual(375)
  const submenuBox = await submenu.boundingBox()
  expect(submenuBox).not.toBeNull()

  await page.mouse.move(submenuBox!.x + 20, submenuBox!.y + 20, { steps: 16 })
  await expect(submenu).toBeVisible()
  await submenu.getByRole('button', { name: 'ERP 接入', exact: true }).click()
  await expect(page).toHaveURL(/\/settings\?section=erp$/)
})

test('Given 375px 报表轨道导航 When 点击分组 Then 浮层打开且 Escape 后可再次打开', async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    await route.fulfill({ json: reportSample })
  })
  await page.goto('/reports/orders')
  await expect(page.locator('#page-order-snapshot-report')).toBeVisible()

  const settings = page.locator('[data-group="settings"] button[aria-expanded]')
  await settings.evaluate((element) => element.scrollIntoView({ block: 'center' }))
  const flyout = page.locator('[data-testid="nav-flyout"]')

  await settings.click()
  await expect(flyout).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(flyout).toHaveCount(0)
  await settings.click()
  await expect(flyout).toBeVisible()
  await flyout.getByRole('button', { name: 'ERP 接入', exact: true }).click()
  await expect(page).toHaveURL(/\/settings\?section=erp$/)
})

test('Given 工作台下钻的 URL 参数 When 打开报表并清除筛选 Then 控件与请求反映参数且清除后清理 URL', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  const requests: URL[] = []
  await page.route('**/api/reports/order-snapshot?**', (route) => {
    requests.push(new URL(route.request().url()))
    return route.fulfill({ json: reportSample })
  })

  await page.goto('/reports/orders?platform=TemuPop&shops=wxw-erp%2FTemuPop%2F30')
  await expect(page.locator('#page-order-snapshot-report')).toBeVisible()

  // 下钻后筛选控件必须反映 URL 参数。
  await expect(page.getByLabel('平台筛选')).toHaveValue('TemuPop')
  await expect(page.getByLabel('店铺筛选')).toContainText('已选 1 家店铺')

  // 两阶段：首请求只带 platform 用于取全量店铺选项；应用有效店铺键后再发带 shops 的请求。
  await expect.poll(() => requests.length).toBeGreaterThanOrEqual(2)
  const initialRequest = requests[0]!
  expect(initialRequest.searchParams.get('platform')).toBe('TemuPop')
  expect(initialRequest.searchParams.getAll('shops')).toEqual([])
  await expect
    .poll(() => requests.at(-1)?.searchParams.getAll('shops') ?? [])
    .toEqual(['wxw-erp/TemuPop/30'])
  expect(requests.at(-1)!.searchParams.get('platform')).toBe('TemuPop')

  // 清除筛选是用户手动操作：只发一次请求并同步清理 URL。
  const requestsBeforeClear = requests.length
  await page.getByRole('button', { name: '清除筛选' }).click()
  await expect(page).toHaveURL(/\/reports\/orders$/)
  await expect(page.getByLabel('平台筛选')).toHaveValue('')
  await expect(page.getByLabel('店铺筛选')).toContainText('全部店铺')
  await expect.poll(() => requests.length).toBe(requestsBeforeClear + 1)
  const clearedRequest = requests.at(-1)!
  expect(clearedRequest.searchParams.get('platform')).toBeNull()
  expect(clearedRequest.searchParams.getAll('shops')).toEqual([])
})

test('Given 非法与混合的 URL 筛选 When 打开报表 Then 丢弃非法值并保留可匹配项且不报错', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  const requests: URL[] = []
  await page.route('**/api/reports/order-snapshot?**', (route) => {
    requests.push(new URL(route.request().url()))
    return route.fulfill({ json: reportSample })
  })

  // 未知平台与未知店铺全部丢弃，按无筛选重新查询。
  await page.goto('/reports/orders?platform=UnknownPlatform&shops=wxw-erp%2FTemuPop%2F999')
  await expect(page.locator('#page-order-snapshot-report')).toBeVisible()
  await expect(page).toHaveURL(/\/reports\/orders$/)
  await expect(page.getByRole('alert')).toHaveCount(0)
  await expect(page.getByLabel('平台筛选')).toHaveValue('')
  await expect(page.getByLabel('店铺筛选')).toContainText('全部店铺')

  // 首请求只带已知的 platform（不带 URL 里的 shops），选项返回后清掉未知平台与店铺重查。
  await expect.poll(() => requests.length).toBeGreaterThanOrEqual(2)
  const invalidInitialRequest = requests[0]!
  expect(invalidInitialRequest.searchParams.get('platform')).toBe('UnknownPlatform')
  expect(invalidInitialRequest.searchParams.getAll('shops')).toEqual([])
  await expect.poll(() => requests.at(-1)?.searchParams.get('platform') ?? null).toBeNull()
  expect(requests.at(-1)!.searchParams.getAll('shops')).toEqual([])

  // 有效平台 + 有效/无效店铺混合：首请求只带 platform，过滤未知店铺后仅带有效键重查。
  const requestsBeforeMixed = requests.length
  await page.goto(
    '/reports/orders?platform=TemuPop&shops=wxw-erp%2FTemuPop%2F30&shops=wxw-erp%2FTemuPop%2F999',
  )
  await expect(page).toHaveURL(/platform=TemuPop&shops=wxw-erp\/TemuPop\/30$/)
  await expect(page.getByLabel('平台筛选')).toHaveValue('TemuPop')
  await expect(page.getByLabel('店铺筛选')).toContainText('已选 1 家店铺')
  await expect(page.getByRole('alert')).toHaveCount(0)
  await expect.poll(() => requests.length).toBeGreaterThanOrEqual(requestsBeforeMixed + 2)
  const mixedInitialRequest = requests[requestsBeforeMixed]!
  expect(mixedInitialRequest.searchParams.get('platform')).toBe('TemuPop')
  expect(mixedInitialRequest.searchParams.getAll('shops')).toEqual([])
  await expect
    .poll(() => requests.at(-1)?.searchParams.getAll('shops') ?? [])
    .toEqual(['wxw-erp/TemuPop/30'])
  expect(requests.at(-1)!.searchParams.get('platform')).toBe('TemuPop')
})
