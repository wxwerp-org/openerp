<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import {
  ElAlert,
  ElButton,
  ElCard,
  ElConfigProvider,
  ElEmpty,
  ElPagination,
  ElProgress,
  ElTable,
  ElTableColumn,
  ElTag,
} from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'

type Shop = {
  sid: string
  name: string
  platform: string
  region: string | null
  isTokenOk: boolean | null
  statusError: string | null
}

type ShopsResponse = {
  items: Shop[]
  page: number
  pageSize: number
  totalCount: number
  pageCount: number
}

type ConfigStatus = 'loading' | 'configured' | 'unconfigured' | 'error'
type ShopsStatus = 'idle' | 'loading' | 'success' | 'error'
type OrderSyncStatus = 'running' | 'succeeded' | 'partial' | 'failed'
type OrderSyncStage = 'shop' | 'pages' | 'details' | 'saving' | 'done'

type OrderSyncTask = {
  id: string
  sid: number
  status: OrderSyncStatus
  stage: OrderSyncStage
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

const configStatus = ref<ConfigStatus>('loading')
const shopsStatus = ref<ShopsStatus>('idle')
const shops = ref<Shop[]>([])
const currentPage = ref(1)
const pageSize = ref(20)
const totalCount = ref(0)
const pageCount = ref(0)
const errorMessage = ref('')
const orderSyncTasks = ref<Record<string, OrderSyncTask>>({})
const orderSyncErrors = ref<Record<string, string>>({})
const orderSyncSubmittingSids = ref(new Set<string>())

const isLoading = computed(
  () => configStatus.value === 'loading' || shopsStatus.value === 'loading',
)

const configLabel = computed(() => {
  switch (configStatus.value) {
    case 'configured':
      return '私有 ERP AccessToken 已配置'
    case 'unconfigured':
      return '未配置私有 ERP AccessToken'
    case 'error':
      return '配置读取失败'
    default:
      return '正在检查配置'
  }
})

const configTagType = computed(() => {
  if (configStatus.value === 'configured') return 'success'
  if (configStatus.value === 'error') return 'danger'
  return 'info'
})

const shopsLabel = computed(() => {
  switch (shopsStatus.value) {
    case 'loading':
      return '正在获取店铺'
    case 'success':
      return '店铺列表已更新'
    case 'error':
      return '店铺列表获取失败'
    default:
      return ''
  }
})

const shopsTagType = computed(() => {
  if (shopsStatus.value === 'success') return 'success'
  if (shopsStatus.value === 'error') return 'danger'
  return 'info'
})

let requestVersion = 0
let activeController: AbortController | null = null
let orderSyncLifecycle = 0
let isMounted = false
let orderSyncPollTimeout: ReturnType<typeof setTimeout> | null = null
const orderSyncRequestVersions = new Map<string, number>()

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string'
}

function parseConfig(value: unknown): { configured: boolean; baseUrl: string } {
  if (!isRecord(value) || typeof value.configured !== 'boolean' || typeof value.baseUrl !== 'string') {
    throw new Error('服务器配置响应格式无效。')
  }

  return { configured: value.configured, baseUrl: value.baseUrl }
}

function parseShopsResponse(value: unknown): ShopsResponse {
  if (
    !isRecord(value) ||
    !Array.isArray(value.items) ||
    !Number.isInteger(value.page) ||
    !Number.isInteger(value.pageSize) ||
    !Number.isInteger(value.totalCount) ||
    !Number.isInteger(value.pageCount)
  ) {
    throw new Error('店铺列表响应格式无效。')
  }

  const items = value.items.map((item): Shop => {
    if (
      !isRecord(item) ||
      typeof item.sid !== 'string' ||
      typeof item.name !== 'string' ||
      typeof item.platform !== 'string' ||
      !isNullableString(item.region) ||
      (item.isTokenOk !== null && typeof item.isTokenOk !== 'boolean') ||
      !isNullableString(item.statusError)
    ) {
      throw new Error('店铺列表中包含格式无效的店铺信息。')
    }

    return {
      sid: item.sid,
      name: item.name,
      platform: item.platform,
      region: item.region,
      isTokenOk: item.isTokenOk,
      statusError: item.statusError,
    }
  })

  return {
    items,
    page: value.page as number,
    pageSize: value.pageSize as number,
    totalCount: value.totalCount as number,
    pageCount: value.pageCount as number,
  }
}

function parseOrderSyncTask(value: unknown): OrderSyncTask {
  const countKeys = ['total', 'read', 'processed', 'added', 'updated', 'unchanged', 'skipped'] as const
  const statuses: OrderSyncStatus[] = ['running', 'succeeded', 'partial', 'failed']
  const stages: OrderSyncStage[] = ['shop', 'pages', 'details', 'saving', 'done']

  if (
    !isRecord(value) ||
    typeof value.id !== 'string' ||
    !Number.isInteger(value.sid) ||
    !statuses.includes(value.status as OrderSyncStatus) ||
    !stages.includes(value.stage as OrderSyncStage) ||
    !isRecord(value.skipReasons) ||
    !isNullableString(value.error) ||
    countKeys.some((key) => !Number.isInteger(value[key]) || (value[key] as number) < 0) ||
    Object.values(value.skipReasons).some(
      (count) => typeof count !== 'number' || !Number.isInteger(count) || count < 0,
    )
  ) {
    throw new Error('订单同步任务响应格式无效。')
  }

  return {
    id: value.id,
    sid: value.sid as number,
    status: value.status as OrderSyncStatus,
    stage: value.stage as OrderSyncStage,
    total: value.total as number,
    read: value.read as number,
    processed: value.processed as number,
    added: value.added as number,
    updated: value.updated as number,
    unchanged: value.unchanged as number,
    skipped: value.skipped as number,
    skipReasons: value.skipReasons as Record<string, number>,
    error: value.error,
  }
}

async function httpErrorMessage(response: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await response.json()
    if (isRecord(body)) {
      const title = typeof body.title === 'string' ? body.title.trim() : ''
      const detail = typeof body.detail === 'string' ? body.detail.trim() : ''
      if (title && detail) return `${title}：${detail}`
      if (title || detail) return title || detail
    }
  } catch {
    // 错误响应没有可读取的 ProblemDetails 时，使用 HTTP 状态码提示。
  }

  return `${fallback}（HTTP ${response.status}）`
}

function clearShops(): void {
  shops.value = []
  totalCount.value = 0
  pageCount.value = 0
}

function nextOrderSyncRequestVersion(sid: string): number {
  const version = (orderSyncRequestVersions.get(sid) ?? 0) + 1
  orderSyncRequestVersions.set(sid, version)
  return version
}

function setOrderSyncTask(sid: string, task: OrderSyncTask): void {
  orderSyncTasks.value = { ...orderSyncTasks.value, [sid]: task }
  const nextErrors = { ...orderSyncErrors.value }
  delete nextErrors[sid]
  orderSyncErrors.value = nextErrors
}

function setOrderSyncError(sid: string, message: string): void {
  orderSyncErrors.value = { ...orderSyncErrors.value, [sid]: message }
}

function clearOrderSyncError(sid: string): void {
  if (!(sid in orderSyncErrors.value)) return
  const nextErrors = { ...orderSyncErrors.value }
  delete nextErrors[sid]
  orderSyncErrors.value = nextErrors
}

async function refreshOrderSyncStatus(sid: string): Promise<OrderSyncTask | null> {
  const version = nextOrderSyncRequestVersion(sid)
  const lifecycle = orderSyncLifecycle

  try {
    const response = await fetch(`/api/erp/shops/${encodeURIComponent(sid)}/order-sync`)
    if (!isMounted || lifecycle !== orderSyncLifecycle || orderSyncRequestVersions.get(sid) !== version) {
      return null
    }

    if (response.status === 204) {
      if (!orderSyncSubmittingSids.value.has(sid)) {
        const nextTasks = { ...orderSyncTasks.value }
        delete nextTasks[sid]
        orderSyncTasks.value = nextTasks
        clearOrderSyncError(sid)
      }
      return null
    }

    if (!response.ok) {
      const message = await httpErrorMessage(response, '无法读取订单同步状态')
      if (!isMounted || lifecycle !== orderSyncLifecycle || orderSyncRequestVersions.get(sid) !== version) {
        return null
      }
      setOrderSyncError(sid, message)
      return null
    }

    const task = parseOrderSyncTask(await response.json())
    if (!isMounted || lifecycle !== orderSyncLifecycle || orderSyncRequestVersions.get(sid) !== version) {
      return null
    }
    if (task.sid !== Number(sid)) {
      throw new Error('订单同步任务与当前店铺不匹配。')
    }
    setOrderSyncTask(sid, task)
    return task
  } catch (error) {
    if (isMounted && lifecycle === orderSyncLifecycle && orderSyncRequestVersions.get(sid) === version) {
      setOrderSyncError(
        sid,
        error instanceof Error ? error.message : '读取订单同步状态失败，请稍后重试。',
      )
    }
    return null
  }
}

function clearOrderSyncPoll(): void {
  if (orderSyncPollTimeout !== null) {
    clearTimeout(orderSyncPollTimeout)
    orderSyncPollTimeout = null
  }
}

function scheduleOrderSyncPoll(): void {
  clearOrderSyncPoll()
  const hasVisibleRunningTask = shops.value.some(
    (shop) => orderSyncTasks.value[shop.sid]?.status === 'running',
  )
  if (!isMounted || !hasVisibleRunningTask) return

  orderSyncPollTimeout = setTimeout(() => {
    orderSyncPollTimeout = null
    void pollRunningOrderSyncTasks()
  }, 2000)
}

async function pollRunningOrderSyncTasks(): Promise<void> {
  if (!isMounted) return
  const runningShopSids = shops.value
    .filter((shop) => orderSyncTasks.value[shop.sid]?.status === 'running')
    .map((shop) => shop.sid)
  await Promise.all(runningShopSids.map((sid) => refreshOrderSyncStatus(sid)))
  scheduleOrderSyncPoll()
}

async function loadVisibleOrderSyncStatuses(visibleShops: Shop[]): Promise<void> {
  const visibleSids = new Set(visibleShops.map((shop) => shop.sid))
  orderSyncTasks.value = Object.fromEntries(
    Object.entries(orderSyncTasks.value).filter(([sid]) => visibleSids.has(sid)),
  )
  orderSyncErrors.value = Object.fromEntries(
    Object.entries(orderSyncErrors.value).filter(([sid]) => visibleSids.has(sid)),
  )

  await Promise.all(visibleShops.map((shop) => refreshOrderSyncStatus(shop.sid)))
  scheduleOrderSyncPoll()
}

async function startOrderSync(shop: Shop): Promise<void> {
  const sid = shop.sid
  if (
    orderSyncSubmittingSids.value.has(sid) ||
    orderSyncTasks.value[sid]?.status === 'running'
  ) {
    return
  }

  const lifecycle = orderSyncLifecycle
  nextOrderSyncRequestVersion(sid)
  const submitting = new Set(orderSyncSubmittingSids.value)
  submitting.add(sid)
  orderSyncSubmittingSids.value = submitting
  const nextErrors = { ...orderSyncErrors.value }
  delete nextErrors[sid]
  orderSyncErrors.value = nextErrors

  try {
    const response = await fetch(`/api/erp/shops/${encodeURIComponent(sid)}/order-sync`, {
      method: 'POST',
    })

    if (!isMounted || lifecycle !== orderSyncLifecycle) return

    if (response.status === 409) {
      const currentTask = await refreshOrderSyncStatus(sid)
      if (currentTask?.status !== 'running' && isMounted && lifecycle === orderSyncLifecycle) {
        setOrderSyncError(sid, '已有同步任务，请稍后重试。')
      }
      scheduleOrderSyncPoll()
      return
    }

    if (response.status !== 202) {
      throw new Error(await httpErrorMessage(response, '无法启动历史订单同步'))
    }

    const task = parseOrderSyncTask(await response.json())
    if (task.sid !== Number(sid)) {
      throw new Error('订单同步任务与当前店铺不匹配。')
    }
    if (isMounted && lifecycle === orderSyncLifecycle) {
      nextOrderSyncRequestVersion(sid)
      setOrderSyncTask(sid, task)
      scheduleOrderSyncPoll()
    }
  } catch (error) {
    if (isMounted && lifecycle === orderSyncLifecycle) {
      setOrderSyncError(
        sid,
        error instanceof Error ? error.message : '启动订单同步失败，请稍后重试。',
      )
    }
  } finally {
    if (isMounted && lifecycle === orderSyncLifecycle) {
      const nextSubmitting = new Set(orderSyncSubmittingSids.value)
      nextSubmitting.delete(sid)
      orderSyncSubmittingSids.value = nextSubmitting
    }
  }
}

function orderSyncStatusLabel(status: OrderSyncStatus): string {
  switch (status) {
    case 'running': return '正在同步'
    case 'succeeded': return '同步完成'
    case 'partial': return '部分成功'
    case 'failed': return '同步失败'
  }
}

function orderSyncStatusType(status: OrderSyncStatus): 'success' | 'warning' | 'danger' | 'info' {
  if (status === 'succeeded') return 'success'
  if (status === 'partial') return 'warning'
  if (status === 'failed') return 'danger'
  return 'info'
}

function orderSyncStageLabel(stage: OrderSyncStage): string {
  switch (stage) {
    case 'shop': return '店铺信息'
    case 'pages': return '订单列表'
    case 'details': return '商品详情'
    case 'saving': return '保存结果'
    case 'done': return '已完成'
  }
}

function orderSyncProgress(task: OrderSyncTask): number {
  if (task.status === 'succeeded' || task.status === 'partial') return 100
  if (task.stage === 'saving' || task.stage === 'done') return 99
  if (task.total <= 0) return task.stage === 'details' ? 50 : 0

  const progress = task.stage === 'pages'
    ? Math.min(1, task.read / task.total) * 50
    : task.stage === 'details'
      ? 50 + Math.min(1, task.processed / task.total) * 49
      : 0

  return Math.min(99, Math.round(progress))
}

function orderSyncProgressStatus(task: OrderSyncTask): 'success' | 'warning' | 'exception' | undefined {
  if (task.status === 'succeeded') return 'success'
  if (task.status === 'partial') return 'warning'
  if (task.status === 'failed') return 'exception'
  return undefined
}

function orderSyncButtonDisabled(sid: string): boolean {
  return (
    orderSyncSubmittingSids.value.has(sid) ||
    orderSyncTasks.value[sid]?.status === 'running'
  )
}

async function loadPage(page: number, size: number, checkConfiguration = false): Promise<void> {
  const requestId = ++requestVersion
  activeController?.abort()
  const controller = new AbortController()
  activeController = controller

  currentPage.value = page
  pageSize.value = size
  clearShops()
  errorMessage.value = ''
  shopsStatus.value = 'loading'

  const isCurrentRequest = () => requestId === requestVersion

  try {
    if (checkConfiguration || configStatus.value === 'loading' || configStatus.value === 'error') {
      configStatus.value = 'loading'

      const configResponse = await fetch('/api/erp/config', { signal: controller.signal })
      if (!isCurrentRequest()) return
      if (!configResponse.ok) {
        throw new Error(await httpErrorMessage(configResponse, '无法读取服务端配置'))
      }

      const config = parseConfig(await configResponse.json())
      if (!isCurrentRequest()) return
      configStatus.value = config.configured ? 'configured' : 'unconfigured'

      if (!config.configured) {
        shopsStatus.value = 'idle'
        return
      }
    }

    if (configStatus.value !== 'configured') {
      shopsStatus.value = 'idle'
      return
    }

    const query = new URLSearchParams({ page: String(page), pageSize: String(size) })
    const shopsResponse = await fetch(`/api/erp/shops?${query.toString()}`, {
      signal: controller.signal,
    })
    if (!isCurrentRequest()) return
    if (!shopsResponse.ok) {
      throw new Error(await httpErrorMessage(shopsResponse, '无法获取授权店铺'))
    }

    const response = parseShopsResponse(await shopsResponse.json())
    if (!isCurrentRequest()) return

    shops.value = response.items
    currentPage.value = response.page
    pageSize.value = response.pageSize
    totalCount.value = response.totalCount
    pageCount.value = response.pageCount
    shopsStatus.value = 'success'
    await loadVisibleOrderSyncStatuses(response.items)
  } catch (error) {
    if (!isCurrentRequest()) return

    if (configStatus.value === 'loading') configStatus.value = 'error'
    clearShops()
    shopsStatus.value = 'error'
    errorMessage.value =
      error instanceof Error ? error.message : '请求失败，请稍后重试。'
  } finally {
    if (isCurrentRequest()) activeController = null
  }
}

function retry(): void {
  void loadPage(currentPage.value, pageSize.value, true)
}

function handlePageChange(page: number): void {
  if (page !== currentPage.value) void loadPage(page, pageSize.value)
}

function handlePageSizeChange(size: number): void {
  if (size !== pageSize.value) void loadPage(1, size)
}

function tokenStatusLabel(status: boolean | null): string {
  if (status === true) return '有效'
  if (status === false) return '无效'
  return '未知'
}

function tokenStatusType(status: boolean | null): 'success' | 'danger' | 'info' {
  if (status === true) return 'success'
  if (status === false) return 'danger'
  return 'info'
}

function regionLabel(region: string | null): string {
  return region?.trim() || '未知'
}

function shopFromTableRow(row: unknown): Shop {
  return row as Shop
}

onMounted(() => {
  isMounted = true
  void loadPage(1, 20, true)
})

onUnmounted(() => {
  isMounted = false
  orderSyncLifecycle += 1
  clearOrderSyncPoll()
  requestVersion += 1
  activeController?.abort()
  activeController = null
})
</script>

<template>
  <main class="shops-page">
    <section class="shops-heading" aria-labelledby="shops-title">
      <div class="shops-heading__copy">
        <p class="eyebrow">TemuPop</p>
        <h1 id="shops-title">授权店铺</h1>
        <p class="description">查看当前账号可见的 Temu 半托管跨境店铺及平台授权状态</p>
      </div>
      <div class="shops-heading__status" aria-live="polite">
        <ElTag :type="configTagType" effect="light">{{ configLabel }}</ElTag>
        <ElTag v-if="shopsLabel" :type="shopsTagType" effect="light">{{ shopsLabel }}</ElTag>
      </div>
    </section>

    <ElAlert
      v-if="configStatus === 'unconfigured'"
      class="shops-notice"
      title="尚未配置私有 ERP AccessToken"
      description="请在服务端配置私有 ERP AccessToken 后重试。"
      type="warning"
      :closable="false"
      show-icon
    />
    <ElAlert
      v-else-if="shopsStatus === 'error'"
      class="shops-notice"
      :title="errorMessage"
      type="error"
      :closable="false"
      show-icon
    />

    <ElCard class="shops-card" shadow="never">
      <div class="shops-card__toolbar">
        <div class="shops-card__summary" aria-live="polite">
          <template v-if="shopsStatus === 'success'">
            共 {{ totalCount }} 家店铺<span v-if="totalCount > 0">，共 {{ pageCount }} 页</span>
          </template>
          <template v-else-if="shopsStatus === 'loading'">正在加载店铺列表</template>
          <template v-else-if="configStatus === 'unconfigured'">完成服务端配置后可查看店铺</template>
          <template v-else>店铺列表</template>
        </div>
        <ElButton
          type="primary"
          plain
          :loading="isLoading"
          @click="retry"
        >
          {{ shopsStatus === 'error' || configStatus === 'error' ? '重试' : '刷新' }}
        </ElButton>
      </div>

      <div class="shops-table-wrap">
        <div v-if="isLoading" class="shops-loading" role="status">正在加载店铺列表…</div>
        <ElTable
          v-else-if="shopsStatus === 'success' && shops.length > 0"
          :data="shops"
          row-key="sid"
          style="width: 100%"
        >
          <ElTableColumn prop="sid" label="SID" min-width="130" />
          <ElTableColumn prop="name" label="名称" min-width="180" />
          <ElTableColumn prop="platform" label="平台" min-width="130" />
          <ElTableColumn label="区域" min-width="130">
            <template #default="{ row }">
              {{ regionLabel(shopFromTableRow(row).region) }}
            </template>
          </ElTableColumn>
          <ElTableColumn label="授权状态" min-width="180">
            <template #default="{ row }">
              <ElTag :type="tokenStatusType(shopFromTableRow(row).isTokenOk)" effect="light">
                {{ tokenStatusLabel(shopFromTableRow(row).isTokenOk) }}
              </ElTag>
              <p v-if="shopFromTableRow(row).statusError" class="shop-status-error">
                {{ shopFromTableRow(row).statusError }}
              </p>
            </template>
          </ElTableColumn>
          <ElTableColumn label="历史订单" min-width="360">
            <template #default="{ row }">
              <div class="shop-order-sync">
                <ElButton
                  size="small"
                  type="primary"
                  plain
                  :loading="orderSyncSubmittingSids.has(shopFromTableRow(row).sid)"
                  :disabled="orderSyncButtonDisabled(shopFromTableRow(row).sid)"
                  @click="startOrderSync(shopFromTableRow(row))"
                >
                  同步历史订单
                </ElButton>
                <div v-if="orderSyncTasks[shopFromTableRow(row).sid]" class="shop-order-sync__task">
                  <div class="shop-order-sync__heading">
                    <ElTag
                      :type="orderSyncStatusType(orderSyncTasks[shopFromTableRow(row).sid].status)"
                      effect="light"
                    >
                      {{ orderSyncStatusLabel(orderSyncTasks[shopFromTableRow(row).sid].status) }}
                    </ElTag>
                    <span>{{ orderSyncStageLabel(orderSyncTasks[shopFromTableRow(row).sid].stage) }}</span>
                  </div>
                  <ElProgress
                    :percentage="orderSyncProgress(orderSyncTasks[shopFromTableRow(row).sid])"
                    :status="orderSyncProgressStatus(orderSyncTasks[shopFromTableRow(row).sid])"
                    :stroke-width="8"
                  />
                  <p class="shop-order-sync__counts">
                    读取 {{ orderSyncTasks[shopFromTableRow(row).sid].read }} / {{ orderSyncTasks[shopFromTableRow(row).sid].total }}
                    · 已处理 {{ orderSyncTasks[shopFromTableRow(row).sid].processed }}
                    · 新增 {{ orderSyncTasks[shopFromTableRow(row).sid].added }}
                    · 更新 {{ orderSyncTasks[shopFromTableRow(row).sid].updated }}
                    · 未变 {{ orderSyncTasks[shopFromTableRow(row).sid].unchanged }}
                    · 跳过 {{ orderSyncTasks[shopFromTableRow(row).sid].skipped }}
                  </p>
                  <p
                    v-for="([reason, count]) in Object.entries(orderSyncTasks[shopFromTableRow(row).sid].skipReasons)"
                    :key="reason"
                    class="shop-order-sync__reason"
                  >
                    {{ reason }}：{{ count }}
                  </p>
                  <p
                    v-if="orderSyncTasks[shopFromTableRow(row).sid].error"
                    class="shop-order-sync__error"
                  >
                    {{ orderSyncTasks[shopFromTableRow(row).sid].error }}
                  </p>
                </div>
                <p
                  v-if="orderSyncErrors[shopFromTableRow(row).sid]"
                  class="shop-order-sync__error"
                  role="alert"
                >
                  {{ orderSyncErrors[shopFromTableRow(row).sid] }}
                </p>
              </div>
            </template>
          </ElTableColumn>
        </ElTable>
        <ElEmpty
          v-else-if="shopsStatus === 'success'"
          class="shops-empty"
          description="暂无授权店铺"
        />
        <ElEmpty
          v-else-if="configStatus === 'unconfigured'"
          class="shops-empty"
          description="配置完成后，这里会显示 TemuPop 授权店铺"
        />
      </div>

      <div v-if="shopsStatus === 'success' && totalCount > 0" class="shops-pagination">
        <ElConfigProvider :locale="zhCn">
        <ElPagination
          :current-page="currentPage"
          :page-size="pageSize"
          :page-sizes="[20, 50, 100]"
          :total="totalCount"
          :disabled="isLoading"
          layout="total, sizes, prev, pager, next, jumper"
          @current-change="handlePageChange"
          @size-change="handlePageSizeChange"
        />
        </ElConfigProvider>
      </div>
    </ElCard>
  </main>
</template>
