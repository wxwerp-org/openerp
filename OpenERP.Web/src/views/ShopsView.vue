<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { apiRequest } from '../account/session'
import AppIcon from '../components/navigation/AppIcon.vue'
import { buildPageList } from '../components/controls/pager'
import ShopDetailDialog from '../components/shops/ShopDetailDialog.vue'
import ShopSummaryCards from '../components/shops/ShopSummaryCards.vue'
import type {
  Shop,
  ShopPlatformOption,
  ShopStatusFilter,
  ShopSummary,
} from '../components/shops/types'
import { useErpHub, type ShopSyncStatus } from '../composables/useErpHub'

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
const pageSizeOptions = [20, 50, 100]

const configStatus = ref<ConfigStatus>('loading')
const configured = ref(false)
const configError = ref('')
const platforms = ref<ShopPlatformOption[]>([])
const shopsStatus = ref<ShopsStatus>('idle')
const shops = ref<Shop[]>([])
const summary = ref<ShopSummary>({ valid: 0, invalid: 0, unknown: 0, lastSyncedAt: null })
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
let stopStatusListener: (() => void) | null = null
let activeController: AbortController | null = null
let requestVersion = 0
let lastRunning = false
let lastCompletedAt: string | null = null
let hasStatusBaseline = false

const isLoading = computed(() => shopsStatus.value === 'loading')
const hasFilters = computed(() => Boolean(platform.value || statusFilter.value || keyword.value))
const syncRunning = computed(() => syncStatus.value?.running === true)
const syncConnected = computed(() => hubState.value === 'connected')
const syncDisabled = computed(
  () => !configured.value || !syncConnected.value || syncRunning.value || isLoading.value,
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
const hubTagClass = computed(() => (hubState.value === 'connected' ? 'green' : 'gray'))
const lastSyncedLabel = computed(() => relativeTime(summary.value.lastSyncedAt))
const pagerItems = computed(() => buildPageList(currentPage.value, pageCount.value))
const canPrev = computed(() => currentPage.value > 1 && !isLoading.value)
const canNext = computed(() => currentPage.value < pageCount.value && !isLoading.value)
const detailPlatformName = computed(() =>
  detailShop.value ? platformLabel(detailShop.value.platform) : '',
)
const hubNotice = computed(
  () => hubState.value === 'disconnected' || hubState.value === 'reconnecting',
)

onMounted(async () => {
  isMounted = true
  nowTimer = window.setInterval(() => {
    now.value = Date.now()
  }, 30_000)
  stopStatusListener = hub.onStatus((next) => {
    const completed = lastRunning && !next.running
    const completedAtChanged = hasStatusBaseline && next.completedAt !== lastCompletedAt
    lastRunning = next.running
    lastCompletedAt = next.completedAt
    hasStatusBaseline = true
    if (completed || completedAtChanged) void refreshAfterSync()
  })

  await Promise.allSettled([loadConfig(), loadPlatforms(), loadShops(1), loadSyncStatus()])
  if (isMounted) await hub.connect()
})

onUnmounted(() => {
  isMounted = false
  if (nowTimer !== undefined) window.clearInterval(nowTimer)
  nowTimer = undefined
  stopStatusListener?.()
  stopStatusListener = null
  requestVersion += 1
  activeController?.abort()
  activeController = null
  void hub.disconnect()
})

async function loadConfig(): Promise<void> {
  try {
    const response = await apiRequest('/api/erp/config')
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

async function loadSyncStatus(): Promise<void> {
  try {
    const response = await apiRequest('/api/erp/shop-sync')
    if (!response.ok) return
    const body: unknown = await response.json()
    if (isShopSyncStatus(body)) {
      syncStatus.value = body
      lastRunning = body.running
      lastCompletedAt = body.completedAt
      hasStatusBaseline = true
    }
  } catch {
    // Hub 连接后仍会推送当前状态。
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

async function refreshAfterSync(): Promise<void> {
  await Promise.allSettled([loadPlatforms(), loadShops(currentPage.value)])
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

function openAuthorization(shop?: Shop): void {
  void shop
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

function statusClass(isTokenOk: boolean | null): string {
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

function isShopSyncStatus(value: unknown): value is ShopSyncStatus {
  if (!isRecord(value) || typeof value.running !== 'boolean') return false
  return [value.total, value.checked, value.valid, value.invalid, value.unknown].every(
    (count) => typeof count === 'number',
  )
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
  <section class="page page-shops active" aria-labelledby="shops-title">
    <header class="shops-head">
      <div>
        <h1 id="shops-title">授权店铺</h1>
        <p class="description">查看上游 ERP 可见店铺的平台授权状态；授权在 wxw ERP 完成。</p>
      </div>
      <span v-if="configStatus === 'ready'" class="tag" :class="configured ? 'green' : 'gray'">
        上游连接：{{ configured ? '已配置' : '未配置' }}
      </span>
    </header>

    <div v-if="configStatus === 'error'" class="shops-notice">
      <div class="notice red" role="alert">
        <AppIcon name="i-warning" />
        <p class="notice-title">{{ configError }}</p>
        <button class="btn" type="button" @click="loadConfig">重试</button>
      </div>
    </div>

    <div v-else-if="configStatus === 'ready' && !configured" class="shops-notice">
      <div class="notice amber" role="alert">
        <AppIcon name="i-warning" />
        <div>
          <p class="notice-title">尚未配置私有 ERP AccessToken</p>
          <p class="notice-desc">请前往系统设置 → 基础 → ERP 接入填写 Token 后重试。</p>
        </div>
        <a class="btn" href="/service-connection">去服务连接</a>
      </div>
    </div>

    <div v-if="listError" class="shops-notice">
      <div class="notice red" role="alert">
        <AppIcon name="i-warning" />
        <p class="notice-title">{{ listError }}</p>
        <button class="btn" type="button" :disabled="isLoading" @click="retry">重试</button>
      </div>
    </div>

    <div v-else-if="syncError" class="shops-notice">
      <div class="notice red" role="alert">
        <AppIcon name="i-warning" />
        <p class="notice-title">{{ syncError }}</p>
        <button class="btn" type="button" :disabled="syncDisabled" @click="syncNow">
          重试同步
        </button>
      </div>
    </div>

    <div v-else-if="syncStatus?.error && !syncRunning" class="shops-notice">
      <div class="notice amber" role="alert">
        <AppIcon name="i-warning" />
        <div>
          <p class="notice-title">上次同步失败：{{ syncStatus.error }}</p>
          <p class="notice-desc">仍显示上次成功同步的数据；修复后点击「立即同步」重试。</p>
        </div>
        <button class="btn" type="button" :disabled="syncDisabled" @click="syncNow">
          立即同步
        </button>
      </div>
    </div>

    <div v-else-if="hubNotice" class="shops-notice">
      <div class="notice amber" role="status">
        <AppIcon name="i-warning" />
        <div>
          <p class="notice-title">实时连接已断开</p>
          <p class="notice-desc">本地店铺仍可浏览；恢复连接后才能触发同步。</p>
        </div>
        <button class="btn" type="button" @click="hub.connect">重连</button>
      </div>
    </div>

    <ShopSummaryCards
      :summary="summary"
      :status-filter="statusFilter"
      :last-synced-label="lastSyncedLabel"
      @select="selectStatus"
    />

    <div class="shops-card">
      <div class="list-toolbar shops-toolbar">
        <div class="toolbar-left">
          <label class="shop-filter">
            <span>平台</span>
            <select
              v-model="platform"
              aria-label="平台筛选"
              :disabled="isLoading"
              @change="applyFilters"
            >
              <option value="">全部平台</option>
              <option v-for="item in platforms" :key="item.name" :value="item.name">
                {{ item.displayName }}
              </option>
            </select>
          </label>

          <form class="shop-search" role="search" @submit.prevent="applyKeyword">
            <input
              v-model="keywordDraft"
              type="search"
              placeholder="搜索店铺名称 / SID"
              aria-label="搜索店铺名称或 SID"
            />
            <button class="search-btn" type="submit" title="搜索" aria-label="搜索">
              <AppIcon name="i-search" icon-class="ic-16" />
            </button>
          </form>

          <button
            v-if="hasFilters"
            class="btn text"
            type="button"
            :disabled="isLoading"
            @click="clearFilters"
          >
            清除筛选
          </button>
        </div>

        <div class="toolbar-right">
          <span class="toolbar-summary" aria-live="polite">
            共 {{ totalCount }} 家店铺<span v-if="pageCount > 0"
              >，第 {{ currentPage }}/{{ pageCount }} 页</span
            >
          </span>
          <span class="tag" :class="hubTagClass">{{ hubLabel }}</span>
          <button
            class="btn primary"
            type="button"
            :disabled="syncDisabled"
            :aria-busy="syncRunning ? 'true' : undefined"
            :title="syncRunning ? '正在同步店铺与授权状态' : '同步店铺列表与平台授权状态'"
            @click="syncNow"
          >
            {{ syncButtonLabel }}
          </button>
          <button
            class="btn-icon"
            type="button"
            title="刷新本地数据"
            aria-label="刷新本地数据"
            :disabled="isLoading"
            :aria-busy="isLoading ? 'true' : undefined"
            @click="retry"
          >
            <AppIcon name="i-refresh" />
          </button>
        </div>
      </div>

      <div class="table-region">
        <div class="table-box">
          <div class="table-area">
            <div class="table-scroll is-y" role="region" aria-label="授权店铺表格" tabindex="0">
              <table class="grid-table shops-table is-wrap">
                <caption class="visually-hidden">
                  授权店铺列表
                </caption>
                <colgroup>
                  <col style="width: 26%" />
                  <col style="width: 18%" />
                  <col style="width: 20%" />
                  <col style="width: 14%" />
                  <col style="width: 22%" />
                </colgroup>
                <thead>
                  <tr>
                    <th scope="col">店铺</th>
                    <th scope="col">平台 / 站点</th>
                    <th scope="col">授权状态</th>
                    <th scope="col">最后检查</th>
                    <th scope="col" class="col-action">操作</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="shop in shops" :key="shop.sid">
                    <td>
                      <div class="shop-name">{{ displayName(shop) }}</div>
                      <div class="shop-sid">SID {{ shop.sid }}</div>
                    </td>
                    <td>
                      <div>{{ platformLabel(shop.platform) }}</div>
                      <div class="shop-region">{{ regionLabel(shop.region) }}</div>
                    </td>
                    <td>
                      <div class="shop-status">
                        <span class="tag" :class="statusClass(shop.isTokenOk)">{{
                          statusLabel(shop.isTokenOk)
                        }}</span>
                        <p v-if="shop.tokenError" class="shop-status-error">
                          {{ shop.tokenError }}
                        </p>
                      </div>
                    </td>
                    <td>{{ formatDateTime(shop.syncedAt) }}</td>
                    <td class="col-action">
                      <div class="shop-actions">
                        <button class="btn text" type="button" @click="openAuthorization(shop)">
                          去 wxw 授权
                        </button>
                        <button class="btn text" type="button" @click="showDetail(shop)">
                          详情
                        </button>
                      </div>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>

            <div v-if="isLoading" class="empty-overlay" role="status">正在加载本地店铺…</div>
            <div v-else-if="shops.length === 0" class="empty-overlay">
              <span v-if="hasFilters">没有符合条件的店铺</span>
              <span v-else-if="!configured">配置上游连接后可同步店铺</span>
              <span v-else>本地暂无店铺数据，请点击「立即同步」</span>
            </div>
          </div>

          <div class="table-pagination">
            <span>共 {{ totalCount }} 条</span>
            <button
              class="page-btn"
              type="button"
              :disabled="!canPrev"
              aria-label="上一页"
              @click="handlePageChange(currentPage - 1)"
            >
              <AppIcon name="i-chev-left" icon-class="ic-12" />
            </button>
            <template v-for="(item, index) in pagerItems" :key="`${item}-${index}`">
              <span v-if="item === 'ellipsis'" class="page-ellipsis">…</span>
              <span v-else-if="item === currentPage" class="page-num" aria-current="page">{{
                item
              }}</span>
              <button v-else class="page-btn" type="button" @click="goToPage(item)">
                {{ item }}
              </button>
            </template>
            <button
              class="page-btn"
              type="button"
              :disabled="!canNext"
              aria-label="下一页"
              @click="handlePageChange(currentPage + 1)"
            >
              <AppIcon name="i-chev-right" icon-class="ic-12" />
            </button>
            <label class="page-size" :class="{ 'is-disabled': isLoading }">
              <select
                :value="pageSize"
                :disabled="isLoading"
                aria-label="每页条数"
                @change="handlePageSizeChange(Number(($event.target as HTMLSelectElement).value))"
              >
                <option v-for="size in pageSizeOptions" :key="size" :value="size">
                  {{ size }}条/页
                </option>
              </select>
            </label>
            <span class="page-jump">
              前往<input
                v-model="jumpPage"
                inputmode="numeric"
                aria-label="前往页码"
                :disabled="isLoading || pageCount < 1"
                @keydown.enter.prevent="submitJumpPage"
                @blur="submitJumpPage"
              />页
            </span>
          </div>
        </div>
      </div>
    </div>

    <ShopDetailDialog
      :open="detailOpen"
      :shop="detailShop"
      :platform-name="detailPlatformName"
      @close="closeDetail"
      @closed="detailShop = null"
      @authorize="openAuthorization"
    />
  </section>
</template>

<style scoped>
.page-shops {
  overflow-y: hidden;
}

.shops-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding: 0 0 12px;
}

.shops-head h1 {
  color: var(--text);
  font-size: 20px;
  line-height: 28px;
  font-weight: 600;
}

.shops-head .description {
  margin-top: 4px;
  color: var(--muted);
  font-size: 12px;
  line-height: 18px;
}

.shops-head .tag {
  flex: none;
  margin-top: 5px;
}

.shops-notice {
  flex: none;
  padding: 0 0 12px;
}

.shops-notice .notice {
  display: flex;
  align-items: flex-start;
  gap: 8px;
}

.shops-notice .btn {
  margin-left: auto;
  flex: none;
  text-decoration: none;
}

.shops-card {
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
  border: 1px solid var(--line);
  border-radius: 6px;
  background: #fff;
}

.shops-toolbar {
  height: 46px;
  flex: none;
  align-items: center;
  padding: 8px 12px;
  border-bottom: 1px solid var(--line-soft);
  background: var(--surface);
}

.shop-filter {
  display: inline-flex;
  flex: none;
  align-items: center;
  gap: 6px;
  color: var(--body);
  font-size: 12px;
}

.shop-filter select {
  height: 30px;
  min-width: 130px;
  padding: 0 8px;
  border: 1px solid var(--input-border);
  border-radius: 4px;
  outline: none;
  background: #fff;
  color: var(--text);
  font-size: 12px;
}

.shop-filter select:focus-visible {
  border-color: var(--primary);
}

.shop-search {
  display: flex;
  height: 30px;
  min-width: 210px;
  max-width: 300px;
  flex: 1;
  align-items: center;
  overflow: hidden;
  border: 1px solid var(--input-border);
  border-radius: 4px;
  background: #fff;
}

.shop-search input {
  height: 28px;
  min-width: 0;
  flex: 1;
  border: 0;
  outline: none;
  padding: 0 10px;
  background: transparent;
  color: var(--text);
  font-size: 12px;
}

.shop-search .search-btn {
  display: flex;
  height: 28px;
  width: 30px;
  flex: none;
  align-items: center;
  justify-content: center;
  color: #586474;
}

.shop-search .search-btn:hover {
  background: #f5f8fd;
  color: var(--primary);
}

.page-shops .table-region {
  flex: 1;
  min-height: 0;
  padding: 0;
  background: #fff;
}

.page-shops .table-box {
  border: 0;
  border-radius: 0;
}

.page-shops .grid-table th {
  background: var(--page);
}

.page-shops .table-pagination {
  padding: 0 12px;
  border-top: 1px solid var(--line-soft);
}

.toolbar-summary {
  min-width: 0;
  overflow: hidden;
  color: var(--muted);
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.shops-table td {
  vertical-align: top;
}

.shop-name {
  color: var(--text);
  font-weight: 500;
  overflow-wrap: anywhere;
}

.shop-sid,
.shop-region {
  margin-top: 2px;
  color: var(--muted);
  font-size: 11px;
  line-height: 16px;
  overflow-wrap: anywhere;
}

.shop-status {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 8px;
}

.shop-status-error {
  width: 100%;
  margin: 0;
  color: var(--body);
  font-size: 12px;
  line-height: 1.45;
  overflow-wrap: anywhere;
}

.shop-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.shop-actions .btn {
  height: 24px;
  padding: 0 4px;
}

@media (max-width: 1280px) {
  .shop-search {
    min-width: 160px;
  }
}

@media (max-width: 1100px) {
  .shops-toolbar {
    height: auto;
    flex-wrap: wrap;
    gap: 8px;
  }

  .shops-toolbar .toolbar-left,
  .shops-toolbar .toolbar-right {
    width: 100%;
    flex-wrap: wrap;
  }

  .shops-toolbar .toolbar-right {
    justify-content: flex-end;
  }
}
</style>
