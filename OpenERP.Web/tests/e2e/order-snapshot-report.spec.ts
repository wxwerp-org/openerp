import { expect, test } from '@playwright/test'
import { enterWorkspace } from './authentication'

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

async function openReport(page: Parameters<typeof enterWorkspace>[0]): Promise<void> {
  await page.goto('/')
  await page.locator('[data-group="stats"] .nav-group-head').click()
  await page.locator('[data-nav-page="order-report"]').click()
  await expect(page).toHaveURL(/\/reports\/orders$/)
  await expect(page.locator('.page-order-snapshot-report')).toBeVisible()
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

  await openReport(page)
  await expect(page.locator('.page-tab.active .tab-label')).toHaveText('商品量报表')

  // 页签页标题只由顶部页签承载，页内不再渲染可见标题。
  const headings = page.locator('.page-order-snapshot-report h1')
  await expect(headings).toHaveCount(1)
  await expect(headings).toHaveClass(/visually-hidden/)
  await expect(headings).toHaveText('商品量报表')

  await expect(
    page.getByText('按本地来源订单计数；件数为当前商品行数量合计，包含禁用/追加行。'),
  ).toBeVisible()
  await expect(page.locator('.report-scope')).toHaveClass(/notice/)
  await expect(page.locator('.report-scope use')).toHaveAttribute('href', '#i-info')
  await expect(page.locator('[aria-label="订单总数"]')).toHaveText('1')
  await expect(page.locator('[aria-label="商品件数"]')).toHaveText('5')

  // 页签列表页骨架：白底通铺、共享筛选条与工具栏、紧凑表格区，不再出现灰底浮卡。
  await expect(page.locator('.report-searchbar')).toHaveClass(/list-searchbar/)
  await expect(page.locator('.report-toolbar')).toHaveClass(/list-toolbar/)
  await expect(page.locator('.report-toolbar .report-scope')).toHaveCount(1)
  await expect(page.locator('.report-groups-panel')).toHaveClass(/table-box/)
  await expect(page.locator('.report-orders-panel')).toHaveClass(/table-box/)
  const chrome = await page.locator('.page-order-snapshot-report').evaluate((shell) => {
    const groupsPanel = shell.querySelector('.report-groups-panel') as HTMLElement
    const ordersHeader = shell.querySelector('.report-orders-table thead th') as HTMLElement
    const groupsStyle = getComputedStyle(groupsPanel)
    const headerStyle = getComputedStyle(ordersHeader)
    return {
      background: getComputedStyle(shell).backgroundColor,
      panelBorder: groupsStyle.borderTopWidth,
      panelRadius: groupsStyle.borderTopLeftRadius,
      headerBackground: headerStyle.backgroundColor,
      headerHeight: headerStyle.height,
    }
  })
  expect(chrome.background).toBe('rgb(255, 255, 255)')
  expect(chrome.panelBorder).toBe('1px')
  expect(chrome.panelRadius).toBe('4px')
  expect(chrome.headerBackground).toBe('rgb(255, 255, 255)')
  expect(chrome.headerHeight).toBe('40px')

  // 表格复用列表页外壳：滚动区可聚焦并带区域语义，表格有显式列宽。
  const ordersRegion = page.getByRole('region', { name: '订单明细表格' })
  await expect(ordersRegion).toHaveCount(1)
  await expect(ordersRegion).toHaveAttribute('tabindex', '0')
  await expect(
    ordersRegion.locator('xpath=ancestor::div[contains(@class,"table-area")]'),
  ).toHaveCount(1)
  await expect(page.locator('.report-groups-table colgroup col')).toHaveCount(4)
  await expect(page.locator('.report-orders-table colgroup col')).toHaveCount(8)

  // 刷新、分页与下拉统一使用 AppIcon；刷新为图标按钮。
  const refresh = page.getByRole('button', { name: '刷新' })
  await expect(refresh).toHaveClass(/btn-icon/)
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
  await expect(page.locator('.page-size use')).toHaveAttribute('href', '#i-chev-down')
  await expect(page.locator('.report-select use')).toHaveAttribute('href', '#i-chev-down')
  expect(
    await page.getByLabel('平台筛选').evaluate((element) => getComputedStyle(element).appearance),
  ).toBe('none')

  // 分页固定在表格卡片底部：页面自身不滚动，滚动交给表格区。
  const paginationBox = await page.locator('.report-pagination').boundingBox()
  expect(paginationBox).not.toBeNull()
  expect(paginationBox!.y + paginationBox!.height).toBeLessThanOrEqual(900)
  const pageShell = await page.locator('.page-order-snapshot-report').evaluate((element) => ({
    overflowY: getComputedStyle(element).overflowY,
    scrollHeight: element.scrollHeight,
    clientHeight: element.clientHeight,
  }))
  expect(pageShell.overflowY).toBe('hidden')
  expect(pageShell.scrollHeight).toBe(pageShell.clientHeight)

  await page.getByLabel('平台筛选').selectOption('TemuPop')
  await page.locator('summary[aria-label="店铺筛选"]').click()
  await page.locator('input[value="wxw-erp/TemuPop/30"]').check()
  const filteredRequest = requests.at(-1)!
  expect(filteredRequest.searchParams.get('platform')).toBe('TemuPop')
  expect(filteredRequest.searchParams.getAll('shops')).toEqual(['wxw-erp/TemuPop/30'])
  await page.locator('summary[aria-label="店铺筛选"]').click()

  await page.getByRole('button', { name: '展开订单 ERP-ORDER-30 商品明细' }).click()
  const expanded = page.locator('.report-order-details')
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
  await expect(page.locator('.report-groups-panel .empty-overlay')).toContainText('暂无分店汇总')
  await expect(page.locator('.report-orders-panel .empty-overlay')).toContainText(
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
  await expect(page.locator('.report-orders-panel .empty-overlay')).toContainText('报表加载失败')

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

test('Given 视口高度仅 500px When 打开多分组报表 Then 分页仍在视口内且两个表格区可滚动到末尾', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 500 })
  await page.route('**/api/reports/order-snapshot?**', async (route) => {
    await route.fulfill({ json: manyReport() })
  })

  await openReport(page)

  const pagination = await page.locator('.report-pagination').boundingBox()
  expect(pagination).not.toBeNull()
  expect(pagination!.y + pagination!.height).toBeLessThanOrEqual(500)

  const shell = await page.locator('.page-order-snapshot-report').evaluate((element) => ({
    overflowY: getComputedStyle(element).overflowY,
    scrollHeight: element.scrollHeight,
    clientHeight: element.clientHeight,
  }))
  expect(shell.overflowY).toBe('hidden')
  expect(shell.scrollHeight).toBe(shell.clientHeight)

  for (const selector of [
    '.report-groups-panel .table-scroll',
    '.report-orders-panel .table-scroll',
  ]) {
    const metrics = await page.locator(selector).evaluate((element) => {
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
  await page.locator('.page-jump input').fill('3')
  await page.locator('.page-jump input').press('Enter')
  await expect(page.locator('.page-num')).toHaveText('3')

  expect(requestedPages).toEqual(['1', '3'])
})
