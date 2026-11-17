// Given 隔离宿主使用固定店铺 SID 30 与 wxw stub，或由 live 模式提供可访问的真实测试店铺，且管理员已初始化。
// When 从授权店铺页面两次触发历史订单同步，并等待每次受理的新任务完成。
// Then UI 显示成功或有效业务跳过后的部分完成，第二次不新增重复订单，数据库完整列与上游一致。
import { expect, test, type Page } from './fixtures'
import type { Locator } from '@playwright/test'
import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { signInForWorkspace } from './authentication'
import { expectedDatabase, testShopName } from './wxw-stub'

type SyncTask = {
  id: string
  sid: number
  status: string
  stage: string
  total: number
  read: number
  processed: number
  added: number
  updated: number
  unchanged: number
  skipped: number
  skipReasons: Record<string, number>
  error: string | null
}

type DatabaseRow = Record<string, unknown>

function isRecord(value: unknown): value is DatabaseRow {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function rowsAt(value: unknown, key: string): DatabaseRow[] {
  if (!isRecord(value) || !Array.isArray(value[key]) || !value[key].every(isRecord))
    throw new Error(`E2E 数据快照的 ${key} 格式无效。`)
  return value[key] as DatabaseRow[]
}

// 排序只消除数据库返回顺序的差异；原生深比较仍检查所有字段和重复行数量。
function sortedRows(rows: DatabaseRow[]): string[] {
  return rows.map(row => JSON.stringify(row, Object.keys(row).sort())).sort()
}

type E2eMode = 'stub' | 'live'

function currentMode(): E2eMode {
  const mode = process.env.OPENERP_E2E_MODE ?? 'stub'
  if (mode !== 'stub' && mode !== 'live') throw new Error('OPENERP_E2E_MODE 必须为 stub 或 live。')
  return mode
}

function selectedSid(mode: E2eMode): number {
  if (mode === 'stub') return 30
  const value = Number(process.env.OPENERP_TEST_SHOP_SID ?? '30')
  if (!Number.isSafeInteger(value) || value <= 0)
    throw new Error('OPENERP_TEST_SHOP_SID 必须是正整数。')
  return value
}

async function findShopRow(page: Page, sid: number, mode: E2eMode): Promise<Locator> {
  await expect(page.locator('.shops-table tbody tr').first(), '店铺首页应完成加载').toBeVisible({ timeout: 60_000 })
  const nextButton = page.getByRole('button', { name: '下一页', exact: true })
  while (true) {
    const row = page.getByRole('row').filter({
      has: page.getByRole('cell', { name: String(sid), exact: true }),
    })
    if (await row.count() === 1) {
      if (mode === 'stub') await expect(row).toContainText(testShopName)
      return row
    }
    if (await nextButton.isDisabled()) break
    const previousFirstRow = await page.locator('.shops-table tbody tr').first().innerText()
    const nextResponse = page.waitForResponse(response =>
      new URL(response.url()).pathname === '/api/erp/shops' && response.request().method() === 'GET',
    )
    await nextButton.click()
    await nextResponse
    await expect(page.locator('.shops-table tbody tr').first(), '店铺分页应完成加载').not.toHaveText(previousFirstRow)
  }
  throw new Error(`授权店铺列表中未找到 SID ${sid}。`)
}

async function readSyncTask(page: Page, sid: number): Promise<SyncTask | null> {
  const response = await page.request.get(`/api/erp/shops/${sid}/order-sync`)
  if (response.status() === 204) return null
  if (!response.ok()) throw new Error(`读取 SID ${sid} 同步任务失败，HTTP ${response.status()}。`)
  const value: unknown = await response.json()
  if (!isRecord(value) || typeof value.id !== 'string' || typeof value.status !== 'string' ||
      typeof value.sid !== 'number')
    throw new Error('订单同步任务响应格式无效。')
  return value as SyncTask
}

async function startAndWaitForSync(
  page: Page,
  sid: number,
  button: Locator,
  row: Locator,
  mode: E2eMode,
  previousId?: string,
): Promise<SyncTask> {
  await expect(button).toBeEnabled({ timeout: 30_000 })
  const postPath = `/api/erp/shops/${sid}/order-sync`
  const acceptedResponse = page.waitForResponse(response =>
    new URL(response.url()).pathname === postPath && response.request().method() === 'POST',
  )
  await button.click()
  const response = await acceptedResponse
  expect(response.status(), '同步任务应由真实 OpenERP API 受理').toBe(202)
  const acceptedValue: unknown = await response.json()
  if (!isRecord(acceptedValue) || typeof acceptedValue.id !== 'string')
    throw new Error('受理的订单同步任务缺少新任务 ID。')
  const acceptedId = acceptedValue.id
  expect(acceptedId.length, '每次同步都必须有任务 ID').toBeGreaterThan(0)
  if (previousId) expect(acceptedId, '第二次同步必须创建新任务').not.toBe(previousId)

  const terminalStatuses = mode === 'live' ? ['succeeded', 'partial'] : ['succeeded']
  await expect.poll(async () => {
    const task = await readSyncTask(page, sid)
    if (task?.id === acceptedId && task.status === 'failed')
      throw new Error(`SID ${sid} 同步失败：${task.error ?? '未提供错误详情'}`)
    if (!task || task.id !== acceptedId || !terminalStatuses.includes(task.status))
      return '任务尚未完成'
    return '已完成'
  }, { timeout: mode === 'live' ? 9 * 60_000 : 60_000 }).toBe('已完成')

  const completed = await readSyncTask(page, sid)
  expect(completed, '完成任务仍应可查询').not.toBeNull()
  expect(completed?.id).toBe(acceptedId)
  expect(completed?.sid).toBe(sid)
  expect(terminalStatuses).toContain(completed?.status)
  expect(completed?.stage).toBe('done')
  await expect(row.getByText(completed?.status === 'partial' ? '部分成功' : '同步完成', { exact: true })).toBeVisible()
  return completed!
}

async function readDatabase(mode: E2eMode, sid: number): Promise<unknown> {
  const { stdout } = await promisify(execFile)('dotnet',
    ['run', '--file', 'e2e/verify-orders.cs', '--no-build', '--', mode, String(sid)],
    { timeout: 5 * 60_000, maxBuffer: 16 * 1024 * 1024 })
  return JSON.parse(stdout)
}

async function verifyDatabase(mode: E2eMode, sid: number): Promise<void> {
  const snapshot = await readDatabase(mode, sid)
  if (!isRecord(snapshot)) throw new Error('订单数据快照格式无效。')
  const actual = mode === 'live' ? snapshot.actual : snapshot
  const expected = mode === 'live' ? snapshot.expected : expectedDatabase
  if (mode === 'live') {
    expect(snapshot.errors, '真实 wxw 读取必须成功').toEqual([])
    expect(snapshot.total, '真实 wxw 应有订单').toBeGreaterThan(0)
    expect(rowsAt(expected, 'orders').length, '真实 wxw 应有可入库订单').toBeGreaterThan(0)
  }
  for (const table of ['orders', 'platformOrders', 'orderLines']) {
    expect(sortedRows(rowsAt(actual, table)), table + ' 全字段及重复行数量应与 wxw 一致')
      .toStrictEqual(sortedRows(rowsAt(expected, table)))
  }
}

function assertLiveSkipSummary(task: SyncTask): void {
  const counts = Object.values(task.skipReasons ?? {})
  expect(counts.every(count => Number.isSafeInteger(count) && count > 0), '真实 wxw 业务跳过原因应为正整数计数').toBe(true)
  expect(task.skipped).toBe(counts.reduce((sum, count) => sum + count, 0))
  expect(task.error).toBeNull()
  expect(task.status).toBe(task.skipped === 0 ? 'succeeded' : 'partial')
}

function assertSyncCounts(mode: E2eMode, firstTask: SyncTask, secondTask: SyncTask): void {
  if (mode === 'stub') {
    expect(firstTask).toMatchObject({ total: 21, added: 21, updated: 0, unchanged: 0, skipped: 0 })
    expect(secondTask).toMatchObject({ total: 21, added: 0, updated: 0, unchanged: 21, skipped: 0 })
    expect(firstTask.skipReasons).toEqual({})
    expect(secondTask.skipReasons).toEqual({})
  } else {
    expect(firstTask.total).toBeGreaterThan(0)
    expect(firstTask.added).toBeGreaterThan(0)
    expect(secondTask.added).toBe(0)
    assertLiveSkipSummary(firstTask)
    assertLiveSkipSummary(secondTask)
  }
}

test('Given wxw 店铺可访问 When 从页面连续同步两次 Then 订单全字段精确入库且第二次没有重复行', async ({ page }) => {
  test.setTimeout(19 * 60_000)
  const mode = currentMode()
  const sid = selectedSid(mode)
  await signInForWorkspace(page.request)
  await page.goto('/shops')

  const shopRow = await findShopRow(page, sid, mode)
  await expect(shopRow, `授权店铺列表中应有 SID ${sid}`).toHaveCount(1)
  const syncButton = shopRow.getByRole('button', { name: /^同步历史订单：/ })

  const firstTask = await startAndWaitForSync(page, sid, syncButton, shopRow, mode)
  await verifyDatabase(mode, sid)
  const secondTask = await startAndWaitForSync(page, sid, syncButton, shopRow, mode, firstTask.id)
  assertSyncCounts(mode, firstTask, secondTask)
  await verifyDatabase(mode, sid)
})
