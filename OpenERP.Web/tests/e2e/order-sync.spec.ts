import { expect, test, type Locator, type Page } from '@playwright/test'
import type { OrderSyncTask } from '../../src/composables/orderSyncTask'
import { enterWorkspace } from './authentication'

const syncShop = { sid: '1', name: '测试店铺 1', platform: 'TemuPop', region: 'US' }

async function mockSyncSnapshot(page: Page, statuses: OrderSyncTask[]) {
  await page.route('**/api/hubs/erp/negotiate?**', (route) =>
    route.fulfill({
      json: {
        negotiateVersion: 1,
        connectionId: 'layout-test',
        connectionToken: 'layout-test',
        availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text'] }],
      },
    }),
  )
  await page.routeWebSocket(/\/api\/hubs\/erp\?/, (socket) => {
    socket.onMessage((message) => {
      if (typeof message !== 'string' || !message.startsWith('{"protocol":')) return
      const event = {
        type: 1,
        target: 'OrderSyncSnapshot',
        arguments: [{ epoch: statuses[0]?.epoch, statuses }],
      }
      socket.send(`{}\u001e${JSON.stringify(event)}\u001e`)
    })
  })
}

function syncMetrics(task: OrderSyncTask) {
  return [
    `读取 ${task.read} / ${task.total}`,
    `已处理 ${task.processed}`,
    `新增 ${task.added}`,
    `更新 ${task.updated}`,
    `未变 ${task.unchanged}`,
    `跳过 ${task.skipped}`,
  ]
}

async function measureSyncMetrics(cell: Locator, metrics: string[]) {
  return cell.evaluate((element, phrases) => {
    const rects: Record<string, { top: number; left: number; right: number }[]> = {}
    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT)
    const range = document.createRange()
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      for (const phrase of phrases) {
        const start = node.textContent?.indexOf(phrase) ?? -1
        if (start < 0) continue
        range.setStart(node, start)
        range.setEnd(node, start + phrase.length)
        rects[phrase] = Array.from(range.getClientRects(), ({ top, left, right }) => ({
          top,
          left,
          right,
        }))
      }
    }
    return phrases.map((phrase) => rects[phrase] ?? [])
  }, metrics)
}

async function prepareSync(page: Page, shops = [syncShop], configured = true) {
  await enterWorkspace(page)
  await page.route('**/api/erp/order-sync-availability', (route) =>
    route.fulfill({
      json: { configured, orderSyncEnabled: true },
    }),
  )
  await page.route('**/api/erp/order-sync-settings', (route) =>
    route.fulfill({
      json: { enabled: true, shopSids: [1], intervalMinutes: 30 },
    }),
  )
  await page.route('**/api/erp/order-sync-settings/shops', (route) =>
    route.fulfill({
      json: { items: [{ sid: '1', name: '测试店铺 1' }] },
    }),
  )
  await page.route('**/api/erp/shops?**', (route) =>
    route.fulfill({
      json: {
        items: shops,
        page: 1,
        pageSize: 20,
        totalCount: shops.length,
        pageCount: shops.length ? 1 : 0,
      },
    }),
  )
  await page.goto('/orders')
  await page.getByRole('button', { name: '同步历史订单', exact: true }).first().press('Enter')
}

test('Given 开启定时同步 When 视口高度不足 Then 面板内可访问设置与店铺且关闭后回到查单', async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 600 })
  await page.route('**/api/hubs/erp/**', (route) => route.abort())
  await prepareSync(page)
  const panel = page.getByRole('dialog', { name: '同步历史订单' })
  const syncSwitch = panel.getByRole('checkbox', { name: '启用定时同步' })
  await syncSwitch.focus()
  await expect(syncSwitch).toBeFocused()
  await expect(syncSwitch).toHaveCSS('outline-color', 'rgb(0, 91, 245)')
  await expect(syncSwitch).toHaveCSS('outline-style', 'solid')
  await expect(syncSwitch).toHaveCSS('outline-width', '2px')
  const row = panel.getByRole('row').filter({ hasText: '测试店铺 1' })
  await row.scrollIntoViewIfNeeded()
  await expect(row).toBeInViewport()
  await expect(row.getByRole('button')).toBeDisabled()
  await page.screenshot({ path: testInfo.outputPath('order-sync-panel.png') })
  await page.keyboard.press('Escape')
  await expect(panel).toBeHidden()
  await expect(
    page.getByRole('button', { name: '同步历史订单', exact: true }).first(),
  ).toBeFocused()
})

test('Given 多家店铺的同步状态 When 在 2112×1073 视口查看 Then 计数按进度与结果分两行且跨店对齐', async ({
  page,
}, testInfo) => {
  const emptyTask: OrderSyncTask = {
    epoch: 'layout',
    version: 1,
    id: 'layout-sync-1',
    sid: 1,
    status: 'succeeded',
    stage: 'done',
    total: 0,
    read: 0,
    processed: 0,
    added: 0,
    updated: 0,
    unchanged: 0,
    skipped: 0,
    skipReasons: {},
    error: null,
  }
  const statuses: OrderSyncTask[] = [
    emptyTask,
    {
      ...emptyTask,
      id: 'layout-sync-2',
      sid: 2,
      read: 53,
      total: 53,
      processed: 53,
      added: 53,
    },
    { ...emptyTask, id: 'layout-sync-3', sid: 3 },
    {
      ...emptyTask,
      id: 'layout-sync-4',
      sid: 4,
      status: 'running',
      stage: 'pages',
      read: 1440,
      total: 2259,
    },
  ]

  await page.setViewportSize({ width: 2112, height: 1073 })
  await mockSyncSnapshot(page, statuses)
  await prepareSync(
    page,
    statuses.map(({ sid }) => ({ ...syncShop, sid: String(sid), name: `测试店铺 ${sid}` })),
  )

  const panel = page.getByRole('dialog', { name: '同步历史订单' })
  const progressbar = page.getByRole('progressbar', { name: '历史订单同步进度' })
  const positions: number[][] = []
  for (const task of statuses) {
    const row = panel.getByRole('row').filter({ hasText: `测试店铺 ${task.sid}` })
    const cell = row.getByRole('cell').filter({ has: progressbar })
    await expect(cell).toBeVisible()
    const rects = await measureSyncMetrics(cell, syncMetrics(task))
    const tops = rects.map(([rect]) => rect?.top ?? Number.NaN)
    expect.soft(tops[0], `店铺 ${task.sid} 的进度行`).toBe(tops[1])
    expect.soft(tops[2], `店铺 ${task.sid} 的结果行`).toBeGreaterThan(tops[0]!)
    expect.soft(tops.slice(2), `店铺 ${task.sid} 的结果项`).toEqual(Array(4).fill(tops[2]))
    expect.soft(rects.map((rect) => rect.length)).toEqual([1, 1, 1, 1, 1, 1])
    positions.push(rects.map(([rect]) => rect?.left ?? Number.NaN))
  }
  for (const position of positions.slice(1)) expect.soft(position).toEqual(positions[0])
  await panel.getByRole('row').last().scrollIntoViewIfNeeded()
  await page.screenshot({ path: testInfo.outputPath('order-sync-status-desktop.png') })
})

test('Given 店铺同步状态包含多项较长计数 When 在较窄视口查看 Then 每项计数完整可见', async ({
  page,
}, testInfo) => {
  const status = {
    epoch: 'layout',
    version: 1,
    id: 'layout-sync',
    sid: 1,
    status: 'partial',
    stage: 'done',
    total: 87654321,
    read: 87654321,
    processed: 87654321,
    added: 12345678,
    updated: 23456789,
    unchanged: 34567890,
    skipped: 123456,
    skipReasons: { 订单内容或商品详情缺少必要数据: 123456 },
    error: '订单同步未能完整处理所有订单，请检查跳过原因后重试。',
  } satisfies OrderSyncTask
  await page.setViewportSize({ width: 620, height: 900 })
  await mockSyncSnapshot(page, [status])

  await prepareSync(page)
  const panel = page.getByRole('dialog', { name: '同步历史订单' })
  const table = panel.getByRole('table', { name: '订单同步店铺列表' })
  const row = table.getByRole('row').filter({ hasText: '测试店铺 1' })
  await expect(row.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '100')
  await expect(row.getByText('部分成功', { exact: true })).toBeVisible()
  await expect(row.getByText('订单内容或商品详情缺少必要数据：123456')).toBeVisible()
  await expect(row.getByText(status.error)).toBeVisible()

  const statusCell = row.getByRole('cell').filter({
    has: page.getByRole('progressbar', { name: '历史订单同步进度' }),
  })
  const bounds = await statusCell.boundingBox()
  const metrics = await measureSyncMetrics(statusCell, syncMetrics(status))
  for (const rects of metrics) {
    expect.soft(rects).toHaveLength(1)
    expect.soft(rects[0]?.right).toBeLessThanOrEqual(bounds ? bounds.x + bounds.width : 0)
  }
  expect(new Set(metrics.flat().map(({ top }) => top)).size).toBeGreaterThan(1)
  await row.scrollIntoViewIfNeeded()
  await page.screenshot({ path: testInfo.outputPath('order-sync-status.png') })
})

test('Given ERP 凭证已被移除 When 在面板发起同步 Then Hub 返回原因且历史订单仍可查询', async ({
  page,
}) => {
  test.skip(
    process.env.OPENERP_TEST_LOCAL_ORDERS !== '1',
    '使用隔离宿主的真实 Hub 和本地样例订单。',
  )
  // 模拟已打开的面板仍显示旧配置，隔离服务实际没有配置上游凭证。
  await prepareSync(page)
  const panel = page.getByRole('dialog', { name: '同步历史订单' })
  await panel.getByRole('button', { name: '全部同步' }).click()
  await expect(panel.getByRole('alert')).toContainText('尚未配置私有 ERP AccessToken')
  const row = panel.getByRole('row').filter({ hasText: '测试店铺 1' })
  await row.getByRole('button').click()
  await expect(row.getByRole('alert')).toContainText('尚未配置私有 ERP AccessToken')
  await page.keyboard.press('Escape')
  await expect(page.getByRole('row').filter({ hasText: 'TM-72801-A' })).toBeVisible()
})

test('Given 本地店铺目录为空 When 点击前往授权店铺 Then 进入店铺目录', async ({ page }) => {
  await prepareSync(page, [])
  const panel = page.getByRole('dialog', { name: '同步历史订单' })
  await panel.getByRole('link', { name: '前往授权店铺' }).click()
  await expect(page).toHaveURL(/\/shops$/)
  await expect(page.getByRole('heading', { name: '授权店铺', exact: true })).toBeVisible()
})

test('Given 缺少 ERP Token When 从订单同步提示前往配置 Then 离开弹窗并可填写 Token', async ({
  page,
}) => {
  await prepareSync(page, [], false)
  const panel = page.getByRole('dialog', { name: '同步历史订单' })
  const notice = panel.getByRole('alert').filter({ hasText: '尚未配置私有 ERP AccessToken' })
  await expect(notice).toContainText('系统设置 → 基础 → ERP 接入')
  await notice.getByRole('link', { name: '前往 ERP 接入' }).click()
  await expect(panel).toBeHidden()
  await expect(page.getByLabel('私有 ERP AccessToken', { exact: true })).toBeEditable()
})
