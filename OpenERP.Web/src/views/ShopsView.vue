<script setup lang="ts">
import ErpAccessNotice from '../components/ErpAccessNotice.vue'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { account, apiRequest } from '../account/session'
import AppButton from '../ui/AppButton.vue'
import AppEmptyState from '../ui/AppEmptyState.vue'
import AppIcon from '../ui/AppIcon.vue'
import AppIconButton from '../ui/AppIconButton.vue'
import AppNotice from '../ui/AppNotice.vue'
import AppPager from '../ui/AppPager.vue'
import AppTable from '../ui/AppTable.vue'
import AppTag from '../ui/AppTag.vue'
import AppTd from '../ui/AppTd.vue'
import AppTh from '../ui/AppTh.vue'
import ShopDetailDialog from '../components/shops/ShopDetailDialog.vue'
import ShopSummaryCards from '../components/shops/ShopSummaryCards.vue'
import type {
  Shop,
  ShopPlatformOption,
  ShopStatusFilter,
  ShopSummary,
} from '../components/shops/types'
import { useErpHub } from '../composables/useErpHub'

type ShopsStatus = 'idle' | 'loading' | 'success' | 'error'
type ConfigStatus = 'loading' | 'ready' | 'error'

type ShopsResponse = {
  items: Shop[]
  page: number
  pageSize: number
  totalCount: number
  pageCount: number
  summary: ShopSummary
}

const authorizationUrl = 'https://v3.wxwerp.com/system/authorize'
const isAdministrator = computed(() => account.value?.role === 'Administrator')
const pageSizeOptions = [20, 50, 100]

/** 旧样式里的控件几何按实测值显式书写，避免组合类互相覆盖。 */
const filterSelectClass =
  'h-[var(--control-h)] min-w-130 rounded-4 border border-input bg-white px-8 text-12/18 text-ink outline-none focus-visible:border-primary'
const rowActionClass =
  'inline-flex h-[var(--control-h)] min-h-[var(--control-h)] shrink-0 items-center justify-center gap-5 whitespace-nowrap rounded-4 border border-transparent bg-transparent px-4 text-12/28 text-primary hover:bg-primary-faint'
const pageSizeClass =
  'inline-flex h-28 min-w-100 shrink-0 cursor-pointer items-center justify-between gap-10 rounded-4 border border-input bg-white px-9 text-ink focus-within:border-primary'
const pageSizeClassDisabled =
  'inline-flex h-28 min-w-100 shrink-0 cursor-default items-center justify-between gap-10 rounded-4 border border-input bg-btn-disabled-bg px-9 text-muted'
const jumpInputClass =
  'h-28 w-46 rounded-4 border border-input bg-white px-4 py-0 text-center text-12/18 text-ink outline-none focus:border-primary disabled:cursor-default disabled:bg-btn-disabled-bg disabled:text-muted'

const configStatus = ref<ConfigStatus>('loading')
const configured = ref(false)
const configError = ref('')
const platforms = ref<ShopPlatformOption[]>([])
const shopsStatus = ref<ShopsStatus>('idle')
const shops = ref<Shop[]>([])
const summary = ref<ShopSummary>({
  valid: 0,
  invalid: 0,
  unknown: 0,
  lastSyncedAt: null,
  syncVersion: 0,
})
const totalCount = ref(0)
const pageCount = ref(0)
const currentPage = ref(1)
const pageSize = ref(20)
const listError = ref('')
const platform = ref('')
const statusFilter = ref<ShopStatusFilter | null>(null)
const keywordDraft = ref('')
const keyword = ref('')
const jumpPage = ref('1')
const detailOpen = ref(false)
const detailShop = ref<Shop | null>(null)
const syncError = ref('')
const now = ref(Date.now())

const hub = useErpHub()
const hubState = hub.state
const syncStatus = hub.status

let isMounted = false
let nowTimer: number | undefined
let activeController: AbortController | null = null
let requestVersion = 0
let refreshing = false

const isLoading = computed(() => shopsStatus.value === 'loading')
const hasFilters = computed(() => Boolean(platform.value || statusFilter.value || keyword.value))
const syncRunning = computed(() => syncStatus.value?.running === true)
const syncConnected = computed(() => hubState.value === 'connected')
const syncDisabled = computed(
  () =>
    !isAdministrator.value ||
    !configured.value ||
    !syncConnected.value ||
    syncRunning.value ||
    isLoading.value,
)
const syncButtonLabel = computed(() => {
  if (!syncRunning.value) return '立即同步'
  const status = syncStatus.value
  return status && status.total > 0 ? `同步中 ${status.checked}/${status.total}` : '同步中…'
})
const hubLabel = computed(() => {
  switch (hubState.value) {
    case 'connected':
      return '实时连接正常'
    case 'connecting':
      return '正在连接实时状态…'
    case 'reconnecting':
      return '实时连接重连中…'
    case 'disconnected':
      return '实时连接已断开'
    default:
      return '实时连接未建立'
  }
})
const hubTagClass = computed<'green' | 'gray'>(() =>
  hubState.value === 'connected' ? 'green' : 'gray',
)
const lastSyncedLabel = computed(() => relativeTime(summary.value.lastSyncedAt))
const detailPlatformName = computed(() =>
  detailShop.value ? platformLabel(detailShop.value.platform) : '',
)
const hubNotice = computed(
  () => hubState.value === 'disconnected' || hubState.value === 'reconnecting',
)
// 目录响应先读状态版本再读店铺行；状态版本更大说明本地列表尚未包含最近一次完成同步。
const directoryStale = computed(() => {
  const current = syncStatus.value
  return (
    shopsStatus.value === 'success' &&
    current !== null &&
    !current.running &&
    current.version > summary.value.syncVersion
  )
})

watch(directoryStale, (stale) => {
  if (stale) void refreshAfterSync()
})

onMounted(async () => {
  isMounted = true
  nowTimer = window.setInterval(() => {
    now.value = Date.now()
  }, 30_000)

  const requests = [loadConfig(), loadPlatforms(), loadShops(1)]
  if (isAdministrator.value) requests.push(hub.refreshStatus())
  await Promise.allSettled(requests)
  if (isMounted && isAdministrator.value) await hub.connect()
})

onUnmounted(() => {
  isMounted = false
  if (nowTimer !== undefined) window.clearInterval(nowTimer)
  nowTimer = undefined
  requestVersion += 1
  activeController?.abort()
  activeController = null
  refreshing = false
  void hub.disconnect()
})

async function loadConfig(): Promise<void> {
  try {
    const response = await apiRequest('/api/erp/order-sync-availability')
    if (!response.ok) {
      configError.value = await problemDetail(response, '无法读取上游连接状态。')
      configStatus.value = 'error'
      return
    }

    const body: unknown = await response.json()
    if (!isRecord(body) || typeof body.configured !== 'boolean')
      throw new Error('上游连接状态格式无效。')

    configured.value = body.configured
    configStatus.value = 'ready'
    configError.value = ''
  } catch (error) {
    configStatus.value = 'error'
    configError.value = errorMessage(error, '无法读取上游连接状态。')
  }
}

async function loadPlatforms(): Promise<void> {
  try {
    const response = await apiRequest('/api/erp/platforms')
    if (!response.ok) return
    platforms.value = parsePlatforms(await response.json())
  } catch {
    // 平台筛选是辅助能力；失败时保留现有选项，不影响本地店铺读取。
  }
}

async function refreshAfterSync(): Promise<void> {
  if (refreshing) return
  refreshing = true
  try {
    // 同步可能在本次刷新期间再次完成；只有目录版本真的前进才继续追赶。
    for (let attempt = 0; attempt < 3 && isMounted && directoryStale.value; attempt += 1) {
      const before = summary.value.syncVersion
      await Promise.allSettled([loadPlatforms(), loadShops(currentPage.value)])
      if (summary.value.syncVersion === before) break
    }
  } finally {
    refreshing = false
  }
}

async function loadShops(page: number): Promise<void> {
  const version = ++requestVersion
  activeController?.abort()
  const controller = new AbortController()
  activeController = controller
  shopsStatus.value = 'loading'
  listError.value = ''

  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize.value),
  })
  if (platform.value) params.set('platform', platform.value)
  if (statusFilter.value) params.set('status', statusFilter.value)
  if (keyword.value) params.set('keyword', keyword.value)

  try {
    const response = await apiRequest(`/api/erp/shops?${params.toString()}`, {
      signal: controller.signal,
    })
    if (version !== requestVersion) return
    if (!response.ok) {
      listError.value = await problemDetail(response, '无法获取本地店铺。')
      shopsStatus.value = 'error'
      return
    }

    const parsed = parseShopsResponse(await response.json())
    if (version !== requestVersion) return
    shops.value = parsed.items
    summary.value = parsed.summary
    totalCount.value = parsed.totalCount
    pageCount.value = parsed.pageCount
    currentPage.value = parsed.page
    jumpPage.value = String(parsed.page)
    shopsStatus.value = 'success'
  } catch (error) {
    if (version !== requestVersion || controller.signal.aborted) return
    listError.value = errorMessage(error, '无法获取本地店铺，请稍后重试。')
    shopsStatus.value = 'error'
  } finally {
    if (activeController === controller) activeController = null
  }
}

function retry(): void {
  if (configStatus.value === 'error') void loadConfig()
  void loadPlatforms()
  void loadShops(currentPage.value)
}

function applyFilters(): void {
  currentPage.value = 1
  jumpPage.value = '1'
  void loadShops(1)
}

function applyKeyword(): void {
  keyword.value = keywordDraft.value.trim()
  applyFilters()
}

function clearFilters(): void {
  platform.value = ''
  statusFilter.value = null
  keyword.value = ''
  keywordDraft.value = ''
  applyFilters()
}

function selectStatus(next: ShopStatusFilter): void {
  statusFilter.value = statusFilter.value === next ? null : next
  applyFilters()
}

function handlePageChange(page: number): void {
  if (page < 1 || page > pageCount.value || isLoading.value || page === currentPage.value) return
  currentPage.value = page
  jumpPage.value = String(page)
  void loadShops(page)
}

function handlePageSizeChange(size: number): void {
  if (isLoading.value) return
  pageSize.value = size
  applyFilters()
}

function goToPage(page: number): void {
  handlePageChange(page)
}

function submitJumpPage(): void {
  const requested = Number.parseInt(jumpPage.value, 10)
  if (!Number.isInteger(requested) || pageCount.value < 1) {
    jumpPage.value = String(currentPage.value)
    return
  }

  const target = Math.min(Math.max(requested, 1), pageCount.value)
  jumpPage.value = String(target)
  handlePageChange(target)
}

async function syncNow(): Promise<void> {
  if (syncDisabled.value) return
  syncError.value = ''
  const result = await hub.startShopSync()
  if (!result.accepted) syncError.value = result.error ?? '无法启动同步，请稍后重试。'
}

function displayName(shop: Shop): string {
  return shop.name.trim() || `店铺 ${shop.sid}`
}

function showDetail(shop: Shop): void {
  detailShop.value = shop
  detailOpen.value = true
}

function closeDetail(): void {
  detailOpen.value = false
}

function openAuthorization(): void {
  window.open(authorizationUrl, '_blank', 'noopener,noreferrer')
}

function platformLabel(name: string): string {
  return platforms.value.find((item) => item.name === name)?.displayName ?? name
}

function regionLabel(region: string | null): string {
  return region?.trim() || '未知'
}

function statusLabel(isTokenOk: boolean | null): string {
  if (isTokenOk === true) return '有效'
  if (isTokenOk === false) return '无效'
  return '未知'
}

function statusClass(isTokenOk: boolean | null): 'green' | 'red' | 'gray' {
  if (isTokenOk === true) return 'green'
  if (isTokenOk === false) return 'red'
  return 'gray'
}

function formatDateTime(value: string | null): string {
  if (!value) return '尚未检查'
  const date = new Date(value)
  return Number.isFinite(date.getTime())
    ? date.toLocaleString('zh-CN', { hour12: false })
    : '尚未检查'
}

function relativeTime(value: string | null): string {
  if (!value) return '尚未同步'
  const time = Date.parse(value)
  if (!Number.isFinite(time)) return '尚未同步'

  const diff = Math.max(0, now.value - time)
  const minutes = Math.floor(diff / 60_000)
  if (minutes < 1) return '刚刚'
  if (minutes < 60) return `${minutes} 分钟前`

  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} 小时前`

  const days = Math.floor(hours / 24)
  if (days < 30) return `${days} 天前`

  return new Date(value).toLocaleDateString('zh-CN')
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object'
}

function nullableString(value: unknown): string | null {
  return typeof value === 'string' ? value : null
}

function nullableBoolean(value: unknown): boolean | null {
  return typeof value === 'boolean' ? value : null
}

function numberOr(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback
}

function parseShopsResponse(value: unknown): ShopsResponse {
  if (!isRecord(value) || !Array.isArray(value.items)) throw new Error('本地店铺响应格式无效。')

  const items = value.items.map(parseShop)
  const summaryValue = isRecord(value.summary) ? value.summary : {}
  return {
    items,
    page: numberOr(value.page, 1),
    pageSize: numberOr(value.pageSize, 20),
    totalCount: numberOr(value.totalCount, items.length),
    pageCount: numberOr(value.pageCount, 0),
    summary: {
      valid: numberOr(summaryValue.valid, 0),
      invalid: numberOr(summaryValue.invalid, 0),
      unknown: numberOr(summaryValue.unknown, 0),
      lastSyncedAt: nullableString(summaryValue.lastSyncedAt),
      syncVersion: numberOr(summaryValue.syncVersion, 0),
    },
  }
}

function parseShop(value: unknown): Shop {
  if (!isRecord(value)) throw new Error('本地店铺响应格式无效。')

  const sid =
    typeof value.sid === 'string'
      ? value.sid
      : typeof value.sid === 'number'
        ? String(value.sid)
        : ''
  if (!sid || typeof value.name !== 'string' || typeof value.platform !== 'string')
    throw new Error('本地店铺响应格式无效。')

  return {
    sid,
    name: value.name,
    platform: value.platform,
    region: nullableString(value.region),
    memberId: nullableString(value.memberId),
    isTokenOk: nullableBoolean(value.isTokenOk),
    tokenError: nullableString(value.tokenError),
    syncedAt: nullableString(value.syncedAt),
  }
}

function parsePlatforms(value: unknown): ShopPlatformOption[] {
  if (!isRecord(value) || !Array.isArray(value.items)) return []

  return value.items.flatMap((item) => {
    if (!isRecord(item) || typeof item.name !== 'string') return []
    return [
      {
        name: item.name,
        displayName: typeof item.displayName === 'string' ? item.displayName : item.name,
        icon: nullableString(item.icon),
        isProduction: item.isProduction !== false,
        shopCount: numberOr(item.shopCount, 0),
      },
    ]
  })
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && !(error instanceof TypeError) ? error.message : fallback
}

async function problemDetail(response: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await response.clone().json()
    if (isRecord(body) && typeof body.detail === 'string' && body.detail) return body.detail
  } catch {
    // 非 ProblemDetails 响应使用兜底文案。
  }
  return fallback
}
</script>

<template>
  <section
    id="page-shops"
    class="flex h-full w-full min-h-0 flex-col overflow-hidden bg-page p-16"
    aria-labelledby="shops-title"
  >
    <header class="flex shrink-0 items-start justify-between gap-12 pb-12" data-testid="shops-head">
      <div>
        <h1 id="shops-title" class="text-20/28 font-semibold text-ink">授权店铺</h1>
        <p class="mt-4 text-12/18 text-muted">
          {{
            isAdministrator
              ? '查看上游 ERP 可见店铺的平台授权状态；授权在 wxw ERP 完成。'
              : '查看已分配店铺的授权状态，并前往旺销网办理授权。'
          }}
        </p>
      </div>
      <AppTag
        v-if="configStatus === 'ready'"
        data-testid="upstream-tag"
        :tone="configured ? 'green' : 'gray'"
        class="mt-5 shrink-0"
      >
        上游连接：{{ configured ? '已配置' : '未配置' }}
      </AppTag>
    </header>

    <div v-if="configStatus === 'error'" class="shrink-0 pb-12">
      <AppNotice tone="red" icon="i-warning" :title="configError" role="alert">
        <template #trailing>
          <AppButton class="ml-auto" @click="loadConfig">重试</AppButton>
        </template>
      </AppNotice>
    </div>

    <div v-else-if="configStatus === 'ready' && !configured" class="shrink-0 pb-12">
      <ErpAccessNotice />
    </div>

    <div v-if="listError" class="shrink-0 pb-12">
      <AppNotice tone="red" icon="i-warning" :title="listError" role="alert">
        <template #trailing>
          <AppButton class="ml-auto" :disabled="isLoading" @click="retry">重试</AppButton>
        </template>
      </AppNotice>
    </div>

    <div v-else-if="isAdministrator && syncError" class="shrink-0 pb-12">
      <AppNotice tone="red" icon="i-warning" :title="syncError" role="alert">
        <template #trailing>
          <AppButton class="ml-auto" :disabled="syncDisabled" @click="syncNow">重试同步</AppButton>
        </template>
      </AppNotice>
    </div>

    <div v-else-if="isAdministrator && syncStatus?.error && !syncRunning" class="shrink-0 pb-12">
      <AppNotice
        tone="amber"
        icon="i-warning"
        :title="`上次同步失败：${syncStatus.error}`"
        description="仍显示上次成功同步的数据；修复后点击「立即同步」重试。"
        role="alert"
      >
        <template #trailing>
          <AppButton class="ml-auto" :disabled="syncDisabled" @click="syncNow">立即同步</AppButton>
        </template>
      </AppNotice>
    </div>

    <div v-else-if="isAdministrator && hubNotice" class="shrink-0 pb-12">
      <AppNotice
        tone="amber"
        icon="i-warning"
        title="实时连接已断开"
        description="本地店铺仍可浏览；恢复连接后才能触发同步。"
        role="status"
        data-testid="hub-notice"
      >
        <template #trailing>
          <AppButton class="ml-auto" @click="hub.connect">重连</AppButton>
        </template>
      </AppNotice>
    </div>

    <ShopSummaryCards
      v-if="isAdministrator"
      :summary="summary"
      :status-filter="statusFilter"
      :last-synced-label="lastSyncedLabel"
      @select="selectStatus"
    />

    <div
      class="flex min-h-0 flex-1 flex-col overflow-hidden rounded-6 border border-line bg-white"
      data-testid="shops-card"
    >
      <div
        class="flex h-46 flex-none items-center gap-6 overflow-hidden border-b border-b-line-soft bg-white px-12 py-8 [@media(max-width:1100px)]:h-auto [@media(max-width:1100px)]:flex-wrap [@media(max-width:1100px)]:gap-8"
        data-testid="shops-toolbar"
      >
        <div
          class="flex min-w-0 flex-1 items-center gap-6 overflow-hidden [@media(max-width:1100px)]:w-full [@media(max-width:1100px)]:flex-wrap"
        >
          <label class="inline-flex flex-none items-center gap-6 text-12 text-body">
            <span>平台</span>
            <select
              v-model="platform"
              aria-label="平台筛选"
              :disabled="isLoading"
              :class="filterSelectClass"
              @change="applyFilters"
            >
              <option value="">全部平台</option>
              <option v-for="item in platforms" :key="item.name" :value="item.name">
                {{ item.displayName }}
              </option>
            </select>
          </label>

          <form
            class="flex h-[var(--control-h)] min-w-210 max-w-300 flex-1 items-center overflow-hidden rounded-4 border border-input bg-white [@media(max-width:1280px)]:min-w-160"
            role="search"
            @submit.prevent="applyKeyword"
          >
            <input
              v-model="keywordDraft"
              type="search"
              placeholder="搜索店铺名称 / SID"
              aria-label="搜索店铺名称或 SID"
              class="h-28 min-w-0 flex-1 border-0 border-current bg-transparent px-10 py-0 text-12/18 text-ink outline-none"
            />
            <button
              class="flex h-28 w-30 shrink-0 items-center justify-center text-search-icon hover:bg-search-hover-bg hover:text-primary"
              type="submit"
              title="搜索"
              aria-label="搜索"
            >
              <AppIcon name="i-search" :size="16" />
            </button>
          </form>

          <AppButton v-if="hasFilters" variant="text" :disabled="isLoading" @click="clearFilters">
            清除筛选
          </AppButton>
        </div>

        <div
          class="ml-auto flex shrink-0 items-center gap-6 [@media(max-width:1100px)]:w-full [@media(max-width:1100px)]:flex-wrap [@media(max-width:1100px)]:justify-end"
        >
          <span
            class="min-w-0 overflow-hidden text-12/18 text-muted text-ellipsis whitespace-nowrap"
            aria-live="polite"
          >
            共 {{ totalCount }} 家店铺<span v-if="pageCount > 0"
              >，第 {{ currentPage }}/{{ pageCount }} 页</span
            >
          </span>
          <AppTag v-if="isAdministrator" :tone="hubTagClass">{{ hubLabel }}</AppTag>
          <AppButton :variant="isAdministrator ? 'default' : 'primary'" @click="openAuthorization"
            >授权新店</AppButton
          >
          <AppButton
            v-if="isAdministrator"
            variant="primary"
            :disabled="syncDisabled"
            :aria-busy="syncRunning ? 'true' : undefined"
            :title="syncRunning ? '正在同步店铺与授权状态' : '同步店铺列表与平台授权状态'"
            @click="syncNow"
          >
            {{ syncButtonLabel }}
          </AppButton>
          <AppIconButton
            label="刷新本地数据"
            tone="toolbar"
            :disabled="isLoading"
            :aria-busy="isLoading ? 'true' : undefined"
            @click="retry"
          >
            <AppIcon name="i-refresh" />
          </AppIconButton>
        </div>
      </div>

      <AppTable variant="plain" caption="授权店铺列表" label="授权店铺表格" wrap scroll-y>
        <colgroup>
          <col style="width: 26%" />
          <col style="width: 18%" />
          <col style="width: 20%" />
          <col style="width: 14%" />
          <col style="width: 22%" />
        </colgroup>
        <thead>
          <tr>
            <AppTh surface="page">店铺</AppTh>
            <AppTh surface="page">平台 / 站点</AppTh>
            <AppTh surface="page">授权状态</AppTh>
            <AppTh surface="page">最后检查</AppTh>
            <AppTh surface="page" sticky="right">操作</AppTh>
          </tr>
        </thead>
        <tbody>
          <tr v-for="shop in shops" :key="shop.sid">
            <AppTd>
              <div class="font-medium text-ink">{{ displayName(shop) }}</div>
              <div class="mt-2 text-11/16 text-muted">SID {{ shop.sid }}</div>
            </AppTd>
            <AppTd>
              <div>{{ platformLabel(shop.platform) }}</div>
              <div class="mt-2 text-11/16 text-muted">{{ regionLabel(shop.region) }}</div>
            </AppTd>
            <AppTd>
              <div class="flex flex-wrap items-center gap-x-8 gap-y-4">
                <AppTag :tone="statusClass(shop.isTokenOk)">{{
                  statusLabel(shop.isTokenOk)
                }}</AppTag>
                <p
                  v-if="shop.tokenError"
                  class="m-0 w-full text-12 leading-[1.45] text-body [overflow-wrap:anywhere]"
                >
                  {{ shop.tokenError }}
                </p>
              </div>
            </AppTd>
            <AppTd>{{ formatDateTime(shop.syncedAt) }}</AppTd>
            <AppTd sticky="right">
              <div class="flex flex-wrap gap-4">
                <button :class="rowActionClass" type="button" @click="openAuthorization">
                  {{ isAdministrator ? '去 wxw 授权' : '续授权' }}
                </button>
                <button :class="rowActionClass" type="button" @click="showDetail(shop)">
                  详情
                </button>
              </div>
            </AppTd>
          </tr>
        </tbody>
        <template #overlay>
          <AppEmptyState v-if="isLoading" role="status">正在加载本地店铺…</AppEmptyState>
          <AppEmptyState v-else-if="shops.length === 0">
            <span v-if="hasFilters">没有符合条件的店铺</span>
            <span v-else-if="!isAdministrator"
              >尚未分配店铺。新店在旺销网授权后，请联系管理员同步目录并分配。</span
            >
            <span v-else-if="!configured">配置上游连接后可同步店铺</span>
            <span v-else>本地暂无店铺数据，请点击「立即同步」</span>
          </AppEmptyState>
        </template>
        <template #footer>
          <AppPager
            :page="currentPage"
            :page-count="pageCount"
            :show-page-list="pageCount > 0"
            :disabled="isLoading"
            label="授权店铺分页"
            :padding-x="12"
            class="border-t border-t-line-soft"
            @update:page="goToPage"
          >
            <template #prefix
              ><span>共 {{ totalCount }} 条</span></template
            >
            <template #suffix>
              <label :class="isLoading ? pageSizeClassDisabled : pageSizeClass">
                <select
                  :value="pageSize"
                  :disabled="isLoading"
                  aria-label="每页条数"
                  class="h-26 min-w-0 cursor-[inherit] appearance-none border-0 border-current bg-transparent p-0 text-12 text-inherit outline-none"
                  @change="handlePageSizeChange(Number(($event.target as HTMLSelectElement).value))"
                >
                  <option v-for="size in pageSizeOptions" :key="size" :value="size">
                    {{ size }}条/页
                  </option>
                </select>
              </label>
              <span class="inline-flex items-center gap-7 pl-3 text-body">
                前往<input
                  v-model="jumpPage"
                  inputmode="numeric"
                  aria-label="前往页码"
                  :disabled="isLoading || pageCount < 1"
                  :class="jumpInputClass"
                  @keydown.enter.prevent="submitJumpPage"
                  @blur="submitJumpPage"
                />页
              </span>
            </template>
          </AppPager>
        </template>
      </AppTable>
    </div>

    <ShopDetailDialog
      :open="detailOpen"
      :administrator="isAdministrator"
      :shop="detailShop"
      :platform-name="detailPlatformName"
      @close="closeDetail"
      @closed="detailShop = null"
      @authorize="openAuthorization"
    />
  </section>
</template>
