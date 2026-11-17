// Given 隔离宿主使用固定店铺 SID 30 与 wxw stub，或由 live 模式提供可访问的真实测试店铺，且管理员已初始化。
// When 从授权店铺页面两次触发历史订单同步，并等待每次受理的新任务完成。
// Then UI 显示成功或有效业务跳过后的部分完成，第二次不新增重复订单，数据库完整列与上游一致。
import { expect, test, type Page } from './fixtures'
import type { Locator } from '@playwright/test'
import { adminPassword, signInForWorkspace } from './authentication'
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

const orderFields = [
  'erpId', 'orderNumber', 'sid', 'platform', 'status',
  'createdRaw', 'createdSource', 'paidRaw', 'paidSource',
  'dispatchedRaw', 'dispatchedSource', 'refundedRaw', 'refundedSource',
] as const

const platformOrderFields = ['erpId', 'poNumber', 'sid', 'statusCode', 'statusText'] as const

const orderLineFields = [
  'erpId', 'productId', 'oguid', 'skuId', 'skuCode', 'quantity', 'disabled', 'appended',
] as const

function isRecord(value: unknown): value is DatabaseRow {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function rowsAt(value: unknown, key: string): DatabaseRow[] {
  if (!isRecord(value) || !Array.isArray(value[key]) || !value[key].every(isRecord))
    throw new Error(`E2E 数据快照的 ${key} 格式无效。`)
  return value[key] as DatabaseRow[]
}

function hasSameMultiset<T extends object>(
  actual: DatabaseRow[],
  expected: readonly T[],
  fields: readonly string[],
): boolean {
  if (actual.length !== expected.length) return false

  const rowKey = (row: object) => {
    const values = row as Record<string, unknown>
    return JSON.stringify(fields.map(field => Object.hasOwn(values, field) ? values[field] : `missing:${field}`))
  }
  const expectedCounts = new Map<string, number>()
  for (const row of expected) {
    const key = rowKey(row)
    expectedCounts.set(key, (expectedCounts.get(key) ?? 0) + 1)
  }

  for (const row of actual) {
    const key = rowKey(row)
    const count = expectedCounts.get(key) ?? 0
    if (count === 0) return false
    if (count === 1) expectedCounts.delete(key)
    else expectedCounts.set(key, count - 1)
  }

  return expectedCounts.size === 0
}

function requiredControlUrl(): string {
  const value = process.env.OPENERP_E2E_CONTROL_URL
  if (!value) throw new Error('E2E 宿主未提供数据库验收地址。')
  return value
}

type E2eMode = 'stub' | 'live'
type TerminalStatus = 'succeeded' | 'partial'

const terminalStatusesByMode: Record<E2eMode, readonly TerminalStatus[]> = {
  stub: ['succeeded'],
  live: ['succeeded', 'partial'],
}

const terminalLabels: Record<TerminalStatus, string> = {
  succeeded: '同步完成',
  partial: '部分成功',
}

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

async function findShopRow(page: Page, sid: number): Promise<Locator> {
  await expect(page.locator('.shops-table tbody tr').first(), '店铺首页应完成加载').toBeVisible({ timeout: 60_000 })
  const nextButton = page.getByRole('button', { name: '下一页', exact: true })
  while (true) {
    const row = page.getByRole('row').filter({
      has: page.getByRole('cell', { name: String(sid), exact: true }),
    })
    if (await row.count() === 1) return row
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

const assertShopNameByMode: Record<E2eMode, (row: Locator) => Promise<void>> = {
  stub: row => expect(row).toContainText(testShopName),
  live: async () => {},
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

  await expect.poll(async () => {
    const task = await readSyncTask(page, sid)
    if (task?.id === acceptedId && task.status === 'failed')
      throw new Error(`SID ${sid} 同步失败：${task.error ?? '未提供错误详情'}`)
    if (!task || task.id !== acceptedId || !terminalStatusesByMode[mode].includes(task.status as TerminalStatus))
      return '任务尚未完成'
    return '已完成'
  }, { timeout: mode === 'live' ? 9 * 60_000 : 60_000 }).toBe('已完成')

  const completed = await readSyncTask(page, sid)
  expect(completed, '完成任务仍应可查询').not.toBeNull()
  expect(completed?.id).toBe(acceptedId)
  expect(completed?.sid).toBe(sid)
  expect(terminalStatusesByMode[mode]).toContain(completed?.status)
  expect(completed?.stage).toBe('done')
  const status = completed?.status as TerminalStatus
  await expect(row.getByText(terminalLabels[status], { exact: true })).toBeVisible()
  return completed!
}

async function requestControl(page: Page, path: string): Promise<unknown> {
  const password = adminPassword
  if (!password) throw new Error('隔离 E2E 宿主未提供管理员密码。')
  const response = await page.request.get(new URL(path, `${requiredControlUrl().replace(/\/$/, '')}/`).toString(), {
    headers: { Authorization: `Bearer ${password}` },
  })
  expect(response.status(), `控制端点 ${path} 应返回成功`).toBe(200)
  return response.json()
}

async function verifyStubDatabase(page: Page, sid: number): Promise<void> {
  const database = await requestControl(page, `/database?sid=${sid}`)
  const actualOrders = rowsAt(database, 'orders')
  const actualPlatformOrders = rowsAt(database, 'platformOrders')
  const actualOrderLines = rowsAt(database, 'orderLines')
  expect(hasSameMultiset(actualOrders, expectedDatabase.orders, [...orderFields]), 'Orders 所有验收列必须与 wxw 来源多重集完全一致').toBe(true)
  expect(hasSameMultiset(actualPlatformOrders, expectedDatabase.platformOrders, [...platformOrderFields]), 'PlatformOrders 所有验收列必须与 wxw 来源多重集完全一致').toBe(true)
  expect(hasSameMultiset(actualOrderLines, expectedDatabase.orderLines, [...orderLineFields]), 'OrderLines 所有验收列必须与 wxw 来源多重集完全一致且保留重复商品行').toBe(true)
}

async function verifyLiveDatabase(page: Page, sid: number): Promise<void> {
  const verification = await requestControl(page, `/verify?sid=${sid}`)
  if (!isRecord(verification)) throw new Error('真实 wxw 对账响应格式无效。')
  expect(Array.isArray(verification.errors) && verification.errors.length === 0, '真实 wxw 与数据库完整列对账必须无差异').toBe(true)
  for (const field of ['total', 'orders'])
    expect(typeof verification[field] === 'number' && verification[field] > 0, `真实 wxw 对账应返回非零 ${field} 计数`).toBe(true)
}

const verifyDatabaseByMode: Record<E2eMode, (page: Page, sid: number) => Promise<void>> = {
  stub: verifyStubDatabase,
  live: verifyLiveDatabase,
}

function assertLiveSkipSummary(task: SyncTask): void {
  const counts = Object.values(task.skipReasons ?? {})
  expect(counts.every(count => Number.isSafeInteger(count) && count > 0), '真实 wxw 业务跳过原因应为正整数计数').toBe(true)
  expect(task.skipped).toBe(counts.reduce((sum, count) => sum + count, 0))
  expect(task.error).toBeNull()
  expect(task.status).toBe(task.skipped === 0 ? 'succeeded' : 'partial')
}

function assertStubCounts(first: SyncTask, second: SyncTask): void {
  expect(first.total).toBe(21)
  expect(first.added).toBe(21)
  expect(first.updated).toBe(0)
  expect(first.unchanged).toBe(0)
  expect(first.skipped).toBe(0)
  expect(first.skipReasons).toEqual({})
  expect(second.total).toBe(21)
  expect(second.added).toBe(0)
  expect(second.updated).toBe(0)
  expect(second.unchanged).toBe(21)
  expect(second.skipped).toBe(0)
  expect(second.skipReasons).toEqual({})
}

function assertLiveCounts(first: SyncTask, second: SyncTask): void {
  expect(first.total).toBeGreaterThan(0)
  expect(first.added).toBeGreaterThan(0)
  expect(second.added).toBe(0)
  assertLiveSkipSummary(first)
  assertLiveSkipSummary(second)
}

const assertSyncCountsByMode: Record<E2eMode, (first: SyncTask, second: SyncTask) => void> = {
  stub: assertStubCounts,
  live: assertLiveCounts,
}

test('Given wxw 店铺可访问 When 从页面连续同步两次 Then 订单全字段精确入库且第二次没有重复行', async ({ page }) => {
  test.setTimeout(19 * 60_000)
  const mode = currentMode()
  const sid = selectedSid(mode)
  await signInForWorkspace(page.request)
  await page.goto('/shops')

  const shopRow = await findShopRow(page, sid)
  await expect(shopRow, `授权店铺列表中应有 SID ${sid}`).toHaveCount(1)
  await assertShopNameByMode[mode](shopRow)
  const syncButton = shopRow.getByRole('button', { name: /^同步历史订单：/ })

  const firstTask = await startAndWaitForSync(page, sid, syncButton, shopRow, mode)
  await verifyDatabaseByMode[mode](page, sid)
  const secondTask = await startAndWaitForSync(page, sid, syncButton, shopRow, mode, firstTask.id)
  assertSyncCountsByMode[mode](firstTask, secondTask)
  await verifyDatabaseByMode[mode](page, sid)
})
