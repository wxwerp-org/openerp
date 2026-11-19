<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { apiRequest } from '../account/session'
import AppIcon from '../components/navigation/AppIcon.vue'
import { buildPageList } from '../components/controls/pager'
import type { OrderSyncStage, OrderSyncStatus, OrderSyncTask } from '../composables/orderSyncTask'
import type { useErpHub } from '../composables/useErpHub'
import { useNativeDialog } from '../composables/useNativeDialog'

const props = defineProps<{
  open: boolean
  hub: ReturnType<typeof useErpHub>
}>()
const emit = defineEmits<{ close: [] }>()
const dialog = useNativeDialog(() => props.open)
void dialog
const { state: hubState, orderSyncTasks, startOrderSync, connect } = props.hub
const submittingSid = ref<number | null>(null)
const orderSyncBusy = computed(
  () =>
    submittingSid.value !== null ||
    Object.values(orderSyncTasks.value).some((task) => task.status === 'running'),
)

type Shop = {
  sid: string
  name: string
  platform: string
  region: string | null
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
type TagClass = 'green' | 'red' | 'gray' | 'amber'

type PagerItem = {
  key: string
  label: string
  page: number | null
  current: boolean
}

type SyncSettings = {
  enabled: boolean
  shopSids: number[]
  intervalMinutes: number
}

type SelectableShop = {
  sid: string
  name: string
}

const configStatus = ref<ConfigStatus>('loading')
const shopsStatus = ref<ShopsStatus>('idle')
const shops = ref<Shop[]>([])
const currentPage = ref(1)
const pageSize = ref(20)
const totalCount = ref(0)
const pageCount = ref(0)
const errorMessage = ref('')
const orderSyncErrors = ref<Record<string, string>>({})
const orderSyncEnabled = ref(false)

type SyncSettingsStatus = 'idle' | 'loading' | 'ready' | 'error'
type SyncShopStatus = 'idle' | 'loading' | 'ready' | 'error'

const syncSettingsStatus = ref<SyncSettingsStatus>('idle')
const syncSettingsError = ref('')
const syncSettingsMessage = ref('')
const syncSettingsSaving = ref(false)
const syncScheduleEnabled = ref(false)
const syncScheduleShopSids = ref<string[]>([])
const syncScheduleIntervalMinutes = ref(0)
const syncScheduleShops = ref<SelectableShop[]>([])
const syncScheduleShopsStatus = ref<SyncShopStatus>('idle')
const syncScheduleShopsError = ref('')
const jumpPage = ref('1')

const pageSizeOptions = [20, 50, 100]

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

const configTagClass = computed<TagClass>(() => {
  if (configStatus.value === 'configured') return 'green'
  if (configStatus.value === 'error') return 'red'
  return 'gray'
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

const shopsTagClass = computed<TagClass>(() => {
  if (shopsStatus.value === 'success') return 'green'
  if (shopsStatus.value === 'error') return 'red'
  return 'gray'
})

const syncScheduleSelectionValid = computed(
  () => !syncScheduleEnabled.value || syncScheduleShopSids.value.length > 0,
)

const syncSchedulePickerShops = computed<SelectableShop[]>(() => {
  const known = new Set(syncScheduleShops.value.map((shop) => shop.sid))
  const missing = syncScheduleShopSids.value
    .filter((sid) => !known.has(sid))
    .map((sid) => ({ sid, name: `已选店铺 ${sid}（不在当前可选列表）` }))
  return [...syncScheduleShops.value, ...missing]
})

const syncScheduleCanSave = computed(
  () =>
    syncSettingsStatus.value === 'ready' &&
    configStatus.value === 'configured' &&
    !syncSettingsSaving.value &&
    syncScheduleSelectionValid.value,
)

const noticeKind = computed<'unconfigured' | 'error' | null>(() => {
  if (configStatus.value === 'unconfigured') return 'unconfigured'
  if (shopsStatus.value === 'error') return 'error'
  return null
})

const retryLabel = computed(() =>
  shopsStatus.value === 'error' || configStatus.value === 'error' ? '重试' : '刷新',
)

const paginationDisabled = computed(() => isLoading.value || pageCount.value < 1)
const pagerItems = computed<PagerItem[]>(() =>
  buildPageList(currentPage.value, pageCount.value).map((item, index) =>
    item === 'ellipsis'
      ? { key: `ellipsis-${index}`, label: '…', page: null, current: false }
      : {
          key: `page-${item}`,
          label: String(item),
          page: item,
          current: item === currentPage.value,
        },
  ),
)

let syncScheduleRequestVersion = 0

let requestVersion = 0
let activeController: AbortController | null = null
watch(
  currentPage,
  (page) => {
    jumpPage.value = String(page)
  },
  { immediate: true },
)

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string'
}

function parseConfig(value: unknown): {
  configured: boolean
  baseUrl: string
  orderSyncEnabled: boolean
} {
  if (
    !isRecord(value) ||
    typeof value.configured !== 'boolean' ||
    typeof value.baseUrl !== 'string'
  ) {
    throw new Error('服务器配置响应格式无效。')
  }

  return {
    configured: value.configured,
    baseUrl: value.baseUrl,
    orderSyncEnabled: value.orderSyncEnabled === true,
  }
}

function parseSyncSettings(value: unknown): SyncSettings {
  if (
    !isRecord(value) ||
    typeof value.enabled !== 'boolean' ||
    !Array.isArray(value.shopSids) ||
    !Number.isInteger(value.intervalMinutes) ||
    (value.intervalMinutes as number) <= 0 ||
    value.shopSids.some((sid) => !Number.isInteger(sid) || (sid as number) <= 0)
  ) {
    throw new Error('定时同步设置响应格式无效。')
  }

  return {
    enabled: value.enabled,
    shopSids: value.shopSids as number[],
    intervalMinutes: value.intervalMinutes as number,
  }
}

function parseSyncShopSelection(value: unknown): SelectableShop[] {
  if (!isRecord(value) || !Array.isArray(value.items)) {
    throw new Error('可选店铺响应格式无效。')
  }

  return value.items.map((item): SelectableShop => {
    if (
      !isRecord(item) ||
      typeof item.sid !== 'string' ||
      !/^[1-9]\d*$/.test(item.sid) ||
      typeof item.name !== 'string'
    ) {
      throw new Error('可选店铺响应中包含格式无效的店铺。')
    }

    return { sid: item.sid, name: item.name }
  })
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
      !isNullableString(item.region)
    ) {
      throw new Error('店铺列表中包含格式无效的店铺信息。')
    }

    return {
      sid: item.sid,
      name: item.name,
      platform: item.platform,
      region: item.region,
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

function applySyncSettings(settings: SyncSettings): void {
  syncScheduleEnabled.value = settings.enabled
  syncScheduleShopSids.value = settings.shopSids.map(String)
  syncScheduleIntervalMinutes.value = settings.intervalMinutes
}

async function loadSyncSchedule(): Promise<void> {
  if (!orderSyncEnabled.value) return

  const version = ++syncScheduleRequestVersion
  syncSettingsStatus.value = 'loading'
  syncSettingsError.value = ''
  syncSettingsMessage.value = ''

  try {
    const settingsResponse = await apiRequest('/api/erp/order-sync-settings')
    if (version !== syncScheduleRequestVersion) return
    if (!settingsResponse.ok) {
      throw new Error(await httpErrorMessage(settingsResponse, '无法读取定时同步设置'))
    }

    const settings = parseSyncSettings(await settingsResponse.json())
    if (version !== syncScheduleRequestVersion) return
    applySyncSettings(settings)
    syncSettingsStatus.value = 'ready'
  } catch (error) {
    if (version !== syncScheduleRequestVersion) return
    syncSettingsStatus.value = 'error'
    syncSettingsError.value =
      error instanceof Error ? error.message : '无法读取定时同步设置，请稍后重试。'
    return
  }

  await loadSyncScheduleShops(version)
}

async function loadSyncScheduleShops(version: number): Promise<void> {
  if (!orderSyncEnabled.value) return
  if (configStatus.value !== 'configured') {
    syncScheduleShops.value = []
    syncScheduleShopsStatus.value = 'error'
    syncScheduleShopsError.value =
      configStatus.value === 'unconfigured'
        ? '配置私有 ERP AccessToken 后可选择店铺。'
        : '服务端配置读取失败，暂时无法选择店铺。'
    return
  }

  syncScheduleShopsStatus.value = 'loading'
  syncScheduleShopsError.value = ''
  try {
    const shopsResponse = await apiRequest('/api/erp/order-sync-settings/shops')
    if (version !== syncScheduleRequestVersion) return
    if (!shopsResponse.ok) {
      throw new Error(await httpErrorMessage(shopsResponse, '无法读取可选店铺'))
    }

    const shops = parseSyncShopSelection(await shopsResponse.json())
    if (version !== syncScheduleRequestVersion) return
    syncScheduleShops.value = shops
    syncScheduleShopsStatus.value = 'ready'
  } catch (error) {
    if (version !== syncScheduleRequestVersion) return
    syncScheduleShops.value = []
    syncScheduleShopsStatus.value = 'error'
    syncScheduleShopsError.value =
      error instanceof Error ? error.message : '无法读取可选店铺，请稍后重试。'
  }
}

async function retrySyncShops(): Promise<void> {
  if (!orderSyncEnabled.value || syncSettingsSaving.value) return
  await loadSyncScheduleShops(++syncScheduleRequestVersion)
}

async function saveSyncSchedule(): Promise<void> {
  if (!syncScheduleCanSave.value) return

  syncSettingsSaving.value = true
  syncSettingsError.value = ''
  syncSettingsMessage.value = ''
  try {
    const response = await apiRequest('/api/erp/order-sync-settings', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        enabled: syncScheduleEnabled.value,
        shopSids: syncScheduleShopSids.value.map(Number),
      }),
    })
    if (!response.ok) {
      throw new Error(await httpErrorMessage(response, '无法保存定时同步设置'))
    }

    applySyncSettings(parseSyncSettings(await response.json()))
    syncSettingsMessage.value = syncScheduleEnabled.value
      ? '定时同步已启用，将在下一个周期执行。'
      : '定时同步已关闭。'
  } catch (error) {
    syncSettingsError.value =
      error instanceof Error ? error.message : '保存定时同步设置失败，请稍后重试。'
  } finally {
    syncSettingsSaving.value = false
  }
}

function selectAllSyncShops(): void {
  if (syncScheduleShopsStatus.value !== 'ready') return
  syncScheduleShopSids.value = syncScheduleShops.value.map((shop) => shop.sid)
  syncSettingsMessage.value = ''
}

function clearSyncShops(): void {
  syncScheduleShopSids.value = []
  syncSettingsMessage.value = ''
}

function clearSyncSettingsMessage(): void {
  syncSettingsMessage.value = ''
}

function setOrderSyncEnabled(enabled: boolean): void {
  orderSyncEnabled.value = enabled
  if (enabled) {
    if (syncSettingsStatus.value === 'idle' || syncSettingsStatus.value === 'error') {
      void loadSyncSchedule()
      return
    }

    if (syncScheduleShopsStatus.value === 'error') void retrySyncShops()
    return
  }

  syncScheduleRequestVersion += 1
  syncSettingsStatus.value = 'idle'
  syncSettingsError.value = ''
  syncSettingsMessage.value = ''
  syncScheduleShops.value = []
  syncScheduleShopsStatus.value = 'idle'
  syncScheduleShopsError.value = ''
  syncScheduleEnabled.value = false
  syncScheduleShopSids.value = []
  syncScheduleIntervalMinutes.value = 0
  orderSyncErrors.value = {}
}

async function syncShop(shop: Shop): Promise<void> {
  if (orderSyncButtonDisabled.value) return

  const sid = Number(shop.sid)
  submittingSid.value = sid
  delete orderSyncErrors.value[sid]

  try {
    const result = await startOrderSync(sid)
    if (!result.accepted) {
      orderSyncErrors.value[sid] = result.error ?? '无法启动订单同步，请稍后重试。'
    }
  } finally {
    submittingSid.value = null
  }
}

function orderSyncStatusLabel(status: OrderSyncStatus): string {
  switch (status) {
    case 'running':
      return '正在同步'
    case 'succeeded':
      return '同步完成'
    case 'partial':
      return '部分成功'
    case 'failed':
      return '同步失败'
  }
}

function orderSyncStatusClass(status: OrderSyncStatus): TagClass {
  if (status === 'succeeded') return 'green'
  if (status === 'partial') return 'amber'
  if (status === 'failed') return 'red'
  return 'gray'
}

function orderSyncStageLabel(stage: OrderSyncStage): string {
  switch (stage) {
    case 'shop':
      return '店铺信息'
    case 'pages':
      return '订单列表'
    case 'details':
      return '商品详情'
    case 'saving':
      return '保存结果'
    case 'done':
      return '已完成'
  }
}

function orderSyncProgress(task: OrderSyncTask): number {
  if (task.status === 'succeeded' || task.status === 'partial') return 100
  if (task.stage === 'saving' || task.stage === 'done') return 99
  if (task.total <= 0) return task.stage === 'details' ? 50 : 0

  const progress =
    task.stage === 'pages'
      ? Math.min(1, task.read / task.total) * 50
      : task.stage === 'details'
        ? 50 + Math.min(1, task.processed / task.total) * 49
        : 0

  return Math.min(99, Math.round(progress))
}

function orderSyncProgressColor(task: OrderSyncTask): string {
  if (task.status === 'succeeded') return 'var(--success)'
  if (task.status === 'partial') return 'var(--warning)'
  if (task.status === 'failed') return 'var(--danger)'
  return 'var(--primary)'
}

const orderSyncButtonDisabled = computed(
  () => !orderSyncEnabled.value || hubState.value !== 'connected' || orderSyncBusy.value,
)

async function loadPage(
  page: number,
  size: number,
  checkConfiguration = false,
  clampPage = true,
): Promise<void> {
  const requestId = ++requestVersion
  activeController?.abort()
  const controller = new AbortController()
  activeController = controller

  currentPage.value = page
  pageSize.value = size
  shops.value = []
  errorMessage.value = ''
  shopsStatus.value = 'loading'

  const isCurrentRequest = () => requestId === requestVersion

  try {
    if (checkConfiguration || configStatus.value === 'loading' || configStatus.value === 'error') {
      configStatus.value = 'loading'

      const configResponse = await apiRequest('/api/erp/config', { signal: controller.signal })
      if (!isCurrentRequest()) return
      if (!configResponse.ok) {
        throw new Error(await httpErrorMessage(configResponse, '无法读取服务端配置'))
      }

      const config = parseConfig(await configResponse.json())
      if (!isCurrentRequest()) return
      configStatus.value = config.configured ? 'configured' : 'unconfigured'
      setOrderSyncEnabled(config.orderSyncEnabled)

      if (!config.configured) {
        clearShops()
        shopsStatus.value = 'idle'
        return
      }
    }

    if (configStatus.value !== 'configured') {
      shopsStatus.value = 'idle'
      return
    }

    const query = new URLSearchParams({
      page: String(page),
      pageSize: String(size),
      platform: 'TemuPop',
    })
    const shopsResponse = await apiRequest(`/api/erp/shops?${query.toString()}`, {
      signal: controller.signal,
    })
    if (!isCurrentRequest()) return
    if (!shopsResponse.ok) {
      throw new Error(await httpErrorMessage(shopsResponse, '无法获取授权店铺'))
    }

    const response = parseShopsResponse(await shopsResponse.json())
    if (!isCurrentRequest()) return

    const lastPage = Math.max(response.pageCount, 1)
    if (clampPage && response.page > lastPage) {
      await loadPage(lastPage, response.pageSize, false, false)
      return
    }

    shops.value = response.items
    currentPage.value = response.page
    pageSize.value = response.pageSize
    totalCount.value = response.totalCount
    pageCount.value = response.pageCount
    shopsStatus.value = 'success'
  } catch (error) {
    if (!isCurrentRequest()) return

    if (configStatus.value === 'loading') configStatus.value = 'error'
    clearShops()
    shopsStatus.value = 'error'
    errorMessage.value = error instanceof Error ? error.message : '请求失败，请稍后重试。'
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

function handlePageSizeSelect(event: Event): void {
  const nextSize = Number((event.target as HTMLSelectElement).value)
  if (Number.isInteger(nextSize) && pageSizeOptions.includes(nextSize)) {
    handlePageSizeChange(nextSize)
  }
}

function goToPage(item: PagerItem): void {
  if (item.page === null) return
  handlePageChange(item.page)
}

function submitJumpPage(): void {
  const requested = Number.parseInt(jumpPage.value, 10)
  if (!Number.isInteger(requested) || pageCount.value < 1) {
    jumpPage.value = String(currentPage.value)
    return
  }

  const target = Math.min(Math.max(requested, 1), pageCount.value)
  jumpPage.value = String(target)
  if (target !== currentPage.value) void loadPage(target, pageSize.value)
}

function shopName(shop: { sid: string; name: string }): string {
  return shop.name.trim() || `店铺 ${shop.sid}`
}

function regionLabel(region: string | null): string {
  return region?.trim() || '未知'
}

let loadedOnOpen = false
watch(
  () => props.open,
  (open) => {
    if (!open) return

    if (!loadedOnOpen) {
      loadedOnOpen = true
      void loadPage(1, 20, true)
    }
    if (hubState.value === 'disconnected') void connect()
  },
  { immediate: true },
)

onUnmounted(() => {
  syncScheduleRequestVersion += 1
  requestVersion += 1
  activeController?.abort()
})
</script>

<template>
  <dialog
    id="order-sync-panel"
    ref="dialog"
    class="order-sync-dialog"
    aria-labelledby="order-sync-title"
    @close="emit('close')"
  >
    <div v-if="open" class="order-sync-dialog__content">
      <header class="order-sync-dialog__header">
        <h2 id="order-sync-title">同步历史订单</h2>
        <form method="dialog">
          <button type="submit" class="btn" autofocus>关闭</button>
        </form>
      </header>

      <div v-if="orderSyncEnabled && hubState !== 'connected'" class="shops-notice">
        <div class="notice amber" role="status">
          <p v-if="hubState === 'disconnected'" class="notice-title">实时连接已断开</p>
          <p v-else class="notice-title">
            {{ hubState === 'reconnecting' ? '正在重新连接…' : '正在连接…' }}
          </p>
          <button v-if="hubState === 'disconnected'" class="btn" type="button" @click="connect">
            重新连接
          </button>
        </div>
      </div>
      <p v-if="orderSyncEnabled && orderSyncBusy" class="order-sync-dialog__busy" role="status">
        有店铺正在同步，完成后可发起新的同步。
      </p>

      <div v-if="configStatus === 'configured' && !orderSyncEnabled" class="shops-notice">
        <div class="notice amber" role="status">
          <p class="notice-title">当前环境未开启订单同步</p>
          <p class="notice-desc">仅 Development / Production 提供历史订单同步能力。</p>
        </div>
      </div>

      <div v-if="noticeKind" class="shops-notice">
        <div v-if="noticeKind === 'unconfigured'" class="notice amber" role="alert">
          <AppIcon name="i-warning" />
          <div>
            <p class="notice-title">尚未配置私有 ERP AccessToken</p>
            <p class="notice-desc">请在服务端配置私有 ERP AccessToken 后重试。</p>
          </div>
        </div>
        <div v-else class="notice red" role="alert">
          <AppIcon name="i-warning" />
          <p class="notice-title">{{ errorMessage }}</p>
        </div>
      </div>

      <div v-if="orderSyncEnabled" class="sync-settings-card" aria-labelledby="sync-settings-title">
        <div class="sync-settings-card__header">
          <div>
            <h3 id="sync-settings-title">定时同步</h3>
            <p class="description">按固定周期自动同步选中的店铺，仅处理已选店铺。</p>
          </div>
          <span
            v-if="syncSettingsStatus === 'ready'"
            class="tag"
            :class="syncScheduleEnabled ? 'green' : 'gray'"
          >
            {{ syncScheduleEnabled ? '已启用' : '未启用' }}
          </span>
        </div>

        <p v-if="syncSettingsStatus === 'loading'" class="sync-settings-card__hint">
          正在读取定时同步设置…
        </p>
        <div v-else-if="syncSettingsStatus === 'error'" class="sync-settings-card__error-block">
          <p class="sync-settings-card__error" role="alert">{{ syncSettingsError }}</p>
          <button class="btn" type="button" @click="loadSyncSchedule">重试</button>
        </div>
        <div v-else class="sync-settings-card__body">
          <div class="sync-settings-card__row">
            <div class="sync-settings-card__label">
              <span>启用定时同步</span>
              <small
                >每
                {{ syncScheduleIntervalMinutes }}
                分钟尝试一轮；首次执行会等待一个周期，与运行中的批次冲突时本轮跳过。</small
              >
            </div>
            <label class="sync-settings-card__switch">
              <input
                v-model="syncScheduleEnabled"
                class="checkbox"
                type="checkbox"
                aria-label="启用定时同步"
                :disabled="syncSettingsSaving || configStatus !== 'configured'"
                @change="clearSyncSettingsMessage"
              />
            </label>
          </div>

          <div class="sync-settings-card__row sync-settings-card__row--shops">
            <div class="sync-settings-card__label">
              <span>选择店铺</span>
              <small>只同步选中的店铺；启用前至少选择一家。</small>
            </div>
            <div class="sync-settings-card__shop-picker">
              <div class="sync-shop-list" role="group" aria-label="定时同步店铺">
                <label
                  v-for="shop in syncSchedulePickerShops"
                  :key="shop.sid"
                  class="sync-shop-option"
                >
                  <input
                    v-model="syncScheduleShopSids"
                    class="checkbox"
                    type="checkbox"
                    :value="shop.sid"
                    :disabled="syncSettingsSaving || syncScheduleShopsStatus !== 'ready'"
                    @change="clearSyncSettingsMessage"
                  />
                  <span>{{ shopName(shop) }}（{{ shop.sid }}）</span>
                </label>
                <p v-if="syncScheduleShopsStatus === 'loading'" class="sync-settings-card__hint">
                  正在读取可选店铺…
                </p>
                <div
                  v-else-if="syncScheduleShopsStatus === 'error'"
                  class="sync-settings-card__shop-error"
                >
                  <p class="sync-settings-card__error" role="alert">{{ syncScheduleShopsError }}</p>
                  <button
                    class="btn text"
                    type="button"
                    :disabled="syncSettingsSaving"
                    @click="retrySyncShops"
                  >
                    重试
                  </button>
                </div>
              </div>
              <div class="sync-settings-card__shop-actions">
                <button
                  class="btn text"
                  type="button"
                  :disabled="syncScheduleShopsStatus !== 'ready' || syncSettingsSaving"
                  @click="selectAllSyncShops"
                >
                  全选可见店铺
                </button>
                <button
                  class="btn text"
                  type="button"
                  :disabled="syncSettingsSaving || syncScheduleShopSids.length === 0"
                  @click="clearSyncShops"
                >
                  清空
                </button>
              </div>
            </div>
          </div>

          <p
            v-if="syncScheduleEnabled && syncScheduleShopSids.length === 0"
            class="sync-settings-card__error"
            role="alert"
          >
            启用定时同步前请至少选择一家店铺。
          </p>

          <div class="sync-settings-card__footer">
            <button
              class="btn primary"
              type="button"
              :disabled="!syncScheduleCanSave"
              :aria-busy="syncSettingsSaving ? 'true' : undefined"
              @click="saveSyncSchedule"
            >
              保存
            </button>
            <span v-if="syncSettingsMessage" class="sync-settings-card__success" role="status">
              {{ syncSettingsMessage }}
            </span>
            <span
              v-if="syncSettingsError && syncSettingsStatus === 'ready'"
              class="sync-settings-card__error"
              role="alert"
            >
              {{ syncSettingsError }}
            </span>
          </div>
        </div>
      </div>

      <div class="list-toolbar">
        <div class="toolbar-left">
          <span class="toolbar-summary" aria-live="polite">
            <template v-if="shopsStatus === 'success'">
              共 {{ totalCount }} 家店铺<span v-if="totalCount > 0">，共 {{ pageCount }} 页</span>
            </template>
            <template v-else-if="shopsStatus === 'loading'">正在加载店铺列表</template>
            <template v-else-if="configStatus === 'unconfigured'"
              >完成服务端配置后可查看店铺</template
            >
            <template v-else>店铺列表</template>
          </span>
          <span class="toolbar-status" aria-live="polite">
            <span class="tag" :class="configTagClass">{{ configLabel }}</span>
            <span v-if="shopsLabel" class="tag" :class="shopsTagClass">{{ shopsLabel }}</span>
          </span>
        </div>
        <div class="toolbar-right">
          <button
            class="btn-icon"
            type="button"
            :title="retryLabel"
            :aria-label="retryLabel"
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
            <div class="table-scroll is-y" role="region" aria-label="订单同步店铺表格" tabindex="0">
              <table class="grid-table shops-table is-wrap">
                <caption class="visually-hidden">
                  订单同步店铺列表
                </caption>
                <colgroup>
                  <col style="width: 12%" />
                  <col style="width: 30%" />
                  <col style="width: 16%" />
                  <col style="width: 12%" />
                  <col v-if="orderSyncEnabled" style="width: 30%" />
                </colgroup>
                <thead>
                  <tr>
                    <th scope="col">SID</th>
                    <th scope="col">名称</th>
                    <th scope="col">平台</th>
                    <th scope="col">区域</th>
                    <th v-if="orderSyncEnabled" scope="col">历史订单</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="shop in shops" :key="shop.sid">
                    <td :title="shop.sid">{{ shop.sid }}</td>
                    <td :title="shopName(shop)">{{ shopName(shop) }}</td>
                    <td>{{ shop.platform }}</td>
                    <td>{{ regionLabel(shop.region) }}</td>
                    <td v-if="orderSyncEnabled">
                      <div class="shop-order-sync">
                        <button
                          class="btn"
                          type="button"
                          :aria-label="`同步历史订单：${shopName(shop)}`"
                          :aria-busy="submittingSid === Number(shop.sid) ? 'true' : undefined"
                          :disabled="orderSyncButtonDisabled"
                          @click="syncShop(shop)"
                        >
                          同步历史订单
                        </button>
                        <div v-if="orderSyncTasks[Number(shop.sid)]" class="shop-order-sync__task">
                          <div class="shop-order-sync__heading">
                            <span
                              class="tag"
                              :class="orderSyncStatusClass(orderSyncTasks[Number(shop.sid)].status)"
                            >
                              {{ orderSyncStatusLabel(orderSyncTasks[Number(shop.sid)].status) }}
                            </span>
                            <span>{{
                              orderSyncStageLabel(orderSyncTasks[Number(shop.sid)].stage)
                            }}</span>
                          </div>
                          <div
                            class="dist-bar shop-order-sync__progress"
                            role="progressbar"
                            aria-label="历史订单同步进度"
                            aria-valuemin="0"
                            aria-valuemax="100"
                            :aria-valuenow="orderSyncProgress(orderSyncTasks[Number(shop.sid)])"
                          >
                            <span
                              :style="{
                                width: `${orderSyncProgress(orderSyncTasks[Number(shop.sid)])}%`,
                                background: orderSyncProgressColor(
                                  orderSyncTasks[Number(shop.sid)],
                                ),
                              }"
                            ></span>
                          </div>
                          <p class="shop-order-sync__counts">
                            读取 {{ orderSyncTasks[Number(shop.sid)].read }} /
                            {{ orderSyncTasks[Number(shop.sid)].total }} · 已处理
                            {{ orderSyncTasks[Number(shop.sid)].processed }} · 新增
                            {{ orderSyncTasks[Number(shop.sid)].added }} · 更新
                            {{ orderSyncTasks[Number(shop.sid)].updated }} · 未变
                            {{ orderSyncTasks[Number(shop.sid)].unchanged }} · 跳过
                            {{ orderSyncTasks[Number(shop.sid)].skipped }}
                          </p>
                          <p
                            v-for="[reason, count] in Object.entries(
                              orderSyncTasks[Number(shop.sid)].skipReasons,
                            )"
                            :key="reason"
                            class="shop-order-sync__reason"
                          >
                            {{ reason }}：{{ count }}
                          </p>
                          <p
                            v-if="orderSyncTasks[Number(shop.sid)].error"
                            class="shop-order-sync__error"
                          >
                            {{ orderSyncTasks[Number(shop.sid)].error }}
                          </p>
                        </div>
                        <p
                          v-if="orderSyncErrors[Number(shop.sid)]"
                          class="shop-order-sync__error"
                          role="alert"
                        >
                          {{ orderSyncErrors[Number(shop.sid)] }}
                        </p>
                      </div>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div v-if="isLoading" class="empty-overlay" role="status">正在加载店铺列表…</div>
            <div
              v-else-if="shopsStatus === 'success' && shops.length === 0"
              class="empty-overlay order-sync-dialog__empty"
            >
              <p>本地暂无店铺，请先同步授权店铺目录。</p>
              <RouterLink class="btn" to="/shops" @click="emit('close')">前往授权店铺</RouterLink>
            </div>
            <div v-else-if="configStatus === 'unconfigured'" class="empty-overlay">
              配置完成后，这里会显示可同步的店铺
            </div>
          </div>

          <div class="table-pagination">
            <span>共 {{ totalCount }} 条</span>
            <button
              class="page-btn"
              type="button"
              aria-label="上一页"
              :disabled="paginationDisabled || pageCount < 1 || currentPage <= 1"
              @click="handlePageChange(currentPage - 1)"
            >
              <AppIcon name="i-chev-left" icon-class="ic-12" />
            </button>
            <template v-for="item in pagerItems" :key="item.key">
              <span v-if="item.page === null" class="page-ellipsis">{{ item.label }}</span>
              <span v-else-if="item.current" class="page-num" aria-current="page">{{
                item.label
              }}</span>
              <button
                v-else
                class="page-btn"
                type="button"
                :aria-label="`第 ${item.label} 页`"
                :disabled="paginationDisabled"
                @click="goToPage(item)"
              >
                {{ item.label }}
              </button>
            </template>
            <button
              class="page-btn"
              type="button"
              aria-label="下一页"
              :disabled="paginationDisabled || currentPage >= pageCount"
              @click="handlePageChange(currentPage + 1)"
            >
              <AppIcon name="i-chev-right" icon-class="ic-12" />
            </button>
            <label class="page-size" :class="{ 'is-disabled': paginationDisabled }">
              <select
                :value="pageSize"
                :disabled="paginationDisabled"
                aria-label="每页条数"
                @change="handlePageSizeSelect"
              >
                <option v-for="size in pageSizeOptions" :key="size" :value="size">
                  {{ size }}条/页
                </option>
              </select>
              <AppIcon name="i-chev-down" />
            </label>
            <span class="page-jump">
              前往<input
                v-model="jumpPage"
                :disabled="paginationDisabled"
                aria-label="前往页码"
                inputmode="numeric"
                @keyup.enter="submitJumpPage"
                @blur="submitJumpPage"
              />页
            </span>
          </div>
        </div>
      </div>
    </div>
  </dialog>
</template>

<style scoped>
.order-sync-dialog {
  width: min(960px, calc(100% - 24px));
  max-height: calc(100% - 24px);
  margin: auto;
  overflow-y: auto;
  border: 1px solid var(--line);
  border-radius: 8px;
  background: var(--page);
  color: var(--text);
}

.order-sync-dialog:modal {
  overscroll-behavior: contain;
}

.order-sync-dialog::backdrop {
  overflow: hidden;
  overscroll-behavior: contain;
  background: rgb(15 23 42 / 52%);
}

.order-sync-dialog__content {
  padding: 16px;
}

.order-sync-dialog__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 16px;
}

.order-sync-dialog__header h2 {
  margin: 0;
  font-size: 20px;
  font-weight: 600;
}

.order-sync-dialog__empty {
  flex-direction: column;
  gap: 8px;
}

.order-sync-dialog__busy {
  margin: 0 0 12px;
  color: var(--muted);
}

.shops-notice {
  flex: none;
  padding: 0 0 12px;
}

.list-toolbar {
  height: 46px;
  flex: none;
  align-items: center;
  margin: 0;
  padding: 0 16px;
  border: 0;
  border-radius: 6px 6px 0 0;
  background: var(--surface);
}

.table-region {
  flex: 1 0 320px;
  min-height: 320px;
  max-height: 360px;
  padding: 0 0 16px;
  background: transparent;
}

.table-box {
  border: 0;
  border-radius: 0 0 6px 6px;
}

.grid-table th {
  background: var(--page);
}

.table-pagination {
  padding: 0 16px;
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

.toolbar-status {
  display: inline-flex;
  flex: none;
  align-items: center;
  gap: 6px;
}

.shops-table td {
  vertical-align: top;
}

.shop-order-sync {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 8px;
}

.shop-order-sync__task {
  display: flex;
  width: 100%;
  min-width: 0;
  flex-direction: column;
  gap: 5px;
  color: var(--body);
  font-size: 12px;
}

.shop-order-sync__heading {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--body);
}

.shop-order-sync__counts,
.shop-order-sync__reason,
.shop-order-sync__error {
  margin: 0;
  line-height: 1.45;
  overflow-wrap: anywhere;
}

.shop-order-sync__reason {
  color: var(--body);
}

.shop-order-sync__error {
  color: var(--body);
}

.sync-settings-card {
  display: flex;
  max-height: 50vh;
  flex: none;
  flex-direction: column;
  margin: 0 0 12px;
  overflow: hidden;
  border: 0;
  border-radius: 6px;
  background: var(--surface);
}

.sync-settings-card__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding: 16px;
  border-bottom: 1px solid var(--line-soft);
}

.sync-settings-card__header h3 {
  color: var(--text);
  font-size: 14px;
  line-height: 22px;
  font-weight: 700;
}

.sync-settings-card__header .description {
  margin-top: 4px;
  color: var(--muted);
  font-size: 12px;
  line-height: 18px;
}

.sync-settings-card__body {
  --sync-label-width: 240px;
  --sync-row-gap: 24px;
  min-height: 0;
  padding: 0 16px 16px;
  overflow-y: auto;
}

.sync-settings-card__row {
  display: grid;
  grid-template-columns: var(--sync-label-width) minmax(0, 1fr);
  gap: var(--sync-row-gap);
  justify-content: start;
  align-items: start;
  padding: 16px 0;
  border-bottom: 1px solid var(--line-soft);
}

.sync-settings-card__row:last-of-type {
  border-bottom: 0;
}

.sync-settings-card__label {
  min-width: 0;
  padding-top: 2px;
  color: var(--text);
  font-size: 12px;
  line-height: 18px;
}

.sync-settings-card__label span {
  display: block;
  font-weight: 600;
}

.sync-settings-card__label small {
  display: block;
  margin-top: 4px;
  color: var(--muted);
  font-weight: 400;
}

.sync-settings-card__switch {
  display: inline-flex;
  flex: none;
  align-items: center;
  padding: 4px 0;
  cursor: pointer;
}

.sync-settings-card__switch .checkbox:disabled {
  cursor: default;
}

.sync-settings-card__shop-picker {
  width: 100%;
  min-width: 0;
}

.sync-shop-list {
  display: flex;
  max-height: 140px;
  overflow-y: auto;
  flex-direction: column;
  gap: 2px;
  padding: 6px 8px;
  border: 1px solid var(--line-soft);
  border-radius: 4px;
  background: var(--page-soft);
}

.sync-shop-option {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 3px 2px;
  color: var(--body);
  font-size: 12px;
  line-height: 18px;
  cursor: pointer;
}

.sync-shop-option:hover {
  background: var(--primary-faint);
}

.sync-shop-option .checkbox:disabled {
  cursor: default;
}

.sync-settings-card__shop-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-top: 6px;
}

.sync-settings-card__hint {
  padding: 6px 0;
  color: var(--muted);
  font-size: 12px;
  line-height: 18px;
}

.sync-settings-card__error-block {
  display: flex;
  min-height: 0;
  flex: 1 1 auto;
  flex-direction: column;
  align-items: flex-start;
  gap: 10px;
  padding: 16px;
  overflow-y: auto;
}

.sync-settings-card__shop-error {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 6px 0;
}

.sync-settings-card__error {
  margin: 0;
  color: var(--body);
  font-size: 12px;
  line-height: 18px;
  overflow-wrap: anywhere;
}

.sync-settings-card__success {
  color: var(--body);
  font-size: 12px;
  line-height: 18px;
}

.sync-settings-card__footer {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-left: calc(var(--sync-label-width) + var(--sync-row-gap));
  padding-top: 14px;
}

.checkbox:focus-visible,
.sync-shop-option:has(.checkbox:focus-visible),
.btn:focus-visible,
.btn-icon:focus-visible,
.page-btn:focus-visible,
.page-jump input:focus-visible,
.page-size select:focus-visible {
  outline: 2px solid var(--primary);
  outline-offset: 2px;
}
</style>
