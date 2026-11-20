<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { apiRequest } from '../account/session'
import ErpAccessNotice from '../components/ErpAccessNotice.vue'
import AppButton from '../ui/AppButton.vue'
import AppCheckbox from '../ui/AppCheckbox.vue'
import AppDialog from '../ui/AppDialog.vue'
import AppEmptyState from '../ui/AppEmptyState.vue'
import AppIcon from '../ui/AppIcon.vue'
import AppIconButton from '../ui/AppIconButton.vue'
import AppNotice from '../ui/AppNotice.vue'
import AppPager from '../ui/AppPager.vue'
import AppProgress from '../ui/AppProgress.vue'
import AppTable from '../ui/AppTable.vue'
import AppTag from '../ui/AppTag.vue'
import AppTd from '../ui/AppTd.vue'
import AppTh from '../ui/AppTh.vue'
import type { OrderSyncStage, OrderSyncStatus, OrderSyncTask } from '../composables/orderSyncTask'
import type { useErpHub } from '../composables/useErpHub'

const props = defineProps<{
  open: boolean
  hub: ReturnType<typeof useErpHub>
}>()
const emit = defineEmits<{ close: [] }>()
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

function orderSyncProgressTone(task: OrderSyncTask): 'primary' | 'success' | 'warning' | 'danger' {
  if (task.status === 'succeeded') return 'success'
  if (task.status === 'partial') return 'warning'
  if (task.status === 'failed') return 'danger'
  return 'primary'
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

/** 本地 dialog 关闭后仍然通知父级，保持旧 `@close` 语义。 */
function handleClosed(): void {
  if (props.open) emit('close')
}

/**
 * 几何/颜色类按实测设计值显式书写：spacing 为 1px，字号/行高使用 theme.css token。
 * 定时同步卡片与店铺多选行用 data-testid 作为测试钩子（不承载样式）。
 */
const focusRing =
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary'
const dialogClass =
  'm-auto w-[min(960px,calc(100%_-_24px))] max-h-[calc(100%_-_24px)] overflow-y-auto rounded-8 border border-line bg-page p-12 text-ink [&:modal]:overscroll-contain'
const toolbarClass =
  'flex h-46 shrink-0 items-center gap-6 overflow-hidden rounded-t-6 bg-surface px-16'
const toolbarSummaryClass =
  'min-w-0 overflow-hidden text-12/18 text-muted text-ellipsis whitespace-nowrap'
const toolbarStatusClass = 'inline-flex shrink-0 items-center gap-6'
const tableRegionClass = 'max-h-360 min-h-320 flex-[1_0_320px] bg-transparent pb-16'
const tableBoxClass = 'overflow-hidden rounded-b-6'
const syncCardClass =
  'mb-12 flex max-h-[50vh] flex-none flex-col overflow-hidden rounded-6 bg-surface'
const rowClass =
  'grid grid-cols-[240px_minmax(0,1fr)] items-start justify-start gap-24 border-b border-b-line-soft py-16'
const hintClass = 'py-6 text-12/18 text-muted'
const metaText = 'text-12/18 text-body [overflow-wrap:anywhere]'
const countText = 'm-0 text-12 leading-[1.45] text-body [overflow-wrap:anywhere]'
const shopOptionClass =
  'flex cursor-pointer items-center gap-6 px-2 py-3 text-12/18 text-body hover:bg-primary-faint has-[input:focus-visible]:outline-2 has-[input:focus-visible]:outline-offset-2 has-[input:focus-visible]:outline-primary'
const pageSizeClass =
  'inline-flex h-28 min-w-100 shrink-0 cursor-pointer items-center justify-between gap-10 rounded-4 border border-input bg-white px-9 text-ink focus-within:border-primary'
const pageSizeDisabledClass =
  'inline-flex h-28 min-w-100 shrink-0 cursor-default items-center justify-between gap-10 rounded-4 border border-input bg-btn-disabled-bg px-9 text-muted'
const pageSizeSelectClass =
  'h-26 min-w-0 cursor-[inherit] appearance-none border-0 border-current bg-transparent p-0 text-12 text-inherit outline-none focus-visible:outline-solid focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary'
const jumpInputClass =
  'h-28 w-46 rounded-4 border border-input bg-white px-4 py-0 text-center text-12/18 text-ink outline-none focus:border-primary focus-visible:outline-solid focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary disabled:cursor-default disabled:bg-btn-disabled-bg disabled:text-muted disabled:focus:border-input'

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
  <AppDialog
    id="order-sync-panel"
    :open="open"
    layer="center"
    aria-labelledby="order-sync-title"
    :class="dialogClass"
    @close="emit('close')"
    @closed="handleClosed"
  >
    <div v-if="open" class="p-16">
      <header class="mb-16 flex items-center justify-between gap-12">
        <h2 id="order-sync-title" class="m-0 text-20/30 font-semibold text-ink">同步历史订单</h2>
        <form method="dialog">
          <AppButton type="submit" autofocus :class="focusRing">关闭</AppButton>
        </form>
      </header>

      <div v-if="orderSyncEnabled && hubState !== 'connected'" class="shrink-0 pb-12">
        <AppNotice
          tone="amber"
          role="status"
          :title="
            hubState === 'disconnected'
              ? '实时连接已断开'
              : hubState === 'reconnecting'
                ? '正在重新连接…'
                : '正在连接…'
          "
        >
          <template v-if="hubState === 'disconnected'" #trailing>
            <AppButton :class="focusRing" type="button" @click="connect">重新连接</AppButton>
          </template>
        </AppNotice>
      </div>
      <p
        v-if="orderSyncEnabled && orderSyncBusy"
        class="m-0 mb-12 text-12/18 text-muted"
        role="status"
      >
        有店铺正在同步，完成后可发起新的同步。
      </p>

      <div v-if="configStatus === 'configured' && !orderSyncEnabled" class="shrink-0 pb-12">
        <AppNotice
          tone="amber"
          role="status"
          title="当前环境未开启订单同步"
          description="仅 Development / Production 提供历史订单同步能力。"
        />
      </div>

      <div v-if="noticeKind" class="shrink-0 pb-12">
        <ErpAccessNotice v-if="noticeKind === 'unconfigured'" />
        <AppNotice v-else tone="red" icon="i-warning" role="alert" :title="errorMessage" />
      </div>

      <div
        v-if="orderSyncEnabled"
        :class="syncCardClass"
        aria-labelledby="sync-settings-title"
        data-testid="sync-settings-card"
      >
        <div class="flex items-start justify-between gap-12 border-b border-b-line-soft p-16">
          <div>
            <h3 id="sync-settings-title" class="m-0 text-14/22 font-bold text-ink">定时同步</h3>
            <p class="m-0 mt-4 text-12/18 text-muted">
              按固定周期自动同步选中的店铺，仅处理已选店铺。
            </p>
          </div>
          <AppTag
            v-if="syncSettingsStatus === 'ready'"
            :tone="syncScheduleEnabled ? 'green' : 'gray'"
          >
            {{ syncScheduleEnabled ? '已启用' : '未启用' }}
          </AppTag>
        </div>

        <p v-if="syncSettingsStatus === 'loading'" :class="hintClass">正在读取定时同步设置…</p>
        <div
          v-else-if="syncSettingsStatus === 'error'"
          class="flex min-h-0 flex-1 flex-col items-start gap-10 overflow-y-auto p-16"
        >
          <p class="m-0 text-12/18 text-body [overflow-wrap:anywhere]" role="alert">
            {{ syncSettingsError }}
          </p>
          <AppButton :class="focusRing" type="button" @click="loadSyncSchedule">重试</AppButton>
        </div>
        <div v-else class="min-h-0 overflow-y-auto px-16 pb-16">
          <div :class="rowClass">
            <div class="min-w-0 pt-2 text-12/18 text-ink">
              <span class="block font-semibold">启用定时同步</span>
              <small class="mt-4 block text-10/18 font-normal text-muted">
                每
                {{ syncScheduleIntervalMinutes }}
                分钟尝试一轮；首次执行会等待一个周期，与运行中的批次冲突时本轮跳过。
              </small>
            </div>
            <label class="inline-flex shrink-0 cursor-pointer items-center py-4">
              <AppCheckbox
                v-model="syncScheduleEnabled"
                aria-label="启用定时同步"
                :disabled="syncSettingsSaving || configStatus !== 'configured'"
                :class="focusRing"
                @change="clearSyncSettingsMessage"
              />
            </label>
          </div>

          <div :class="rowClass">
            <div class="min-w-0 pt-2 text-12/18 text-ink">
              <span class="block font-semibold">选择店铺</span>
              <small class="mt-4 block text-10/18 font-normal text-muted">
                只同步选中的店铺；启用前至少选择一家。
              </small>
            </div>
            <div class="w-full min-w-0">
              <div
                class="flex max-h-140 flex-col gap-2 overflow-y-auto rounded-4 border border-line-soft bg-page-soft px-8 py-6"
                role="group"
                aria-label="定时同步店铺"
              >
                <label
                  v-for="shop in syncSchedulePickerShops"
                  :key="shop.sid"
                  :class="shopOptionClass"
                  data-testid="sync-shop-option"
                >
                  <AppCheckbox
                    v-model="syncScheduleShopSids"
                    :value="shop.sid"
                    :disabled="syncSettingsSaving || syncScheduleShopsStatus !== 'ready'"
                    :class="focusRing"
                    @change="clearSyncSettingsMessage"
                  />
                  <span>{{ shopName(shop) }}（{{ shop.sid }}）</span>
                </label>
                <p v-if="syncScheduleShopsStatus === 'loading'" :class="hintClass">
                  正在读取可选店铺…
                </p>
                <div
                  v-else-if="syncScheduleShopsStatus === 'error'"
                  class="flex items-start gap-8 py-6"
                >
                  <p class="m-0 text-12/18 text-body [overflow-wrap:anywhere]" role="alert">
                    {{ syncScheduleShopsError }}
                  </p>
                  <AppButton
                    variant="text"
                    type="button"
                    :disabled="syncSettingsSaving"
                    :class="focusRing"
                    @click="retrySyncShops"
                  >
                    重试
                  </AppButton>
                </div>
              </div>
              <div class="mt-6 flex items-center gap-4">
                <AppButton
                  variant="text"
                  type="button"
                  :disabled="syncScheduleShopsStatus !== 'ready' || syncSettingsSaving"
                  :class="focusRing"
                  @click="selectAllSyncShops"
                >
                  全选可见店铺
                </AppButton>
                <AppButton
                  variant="text"
                  type="button"
                  :disabled="syncSettingsSaving || syncScheduleShopSids.length === 0"
                  :class="focusRing"
                  @click="clearSyncShops"
                >
                  清空
                </AppButton>
              </div>
            </div>
          </div>

          <p
            v-if="syncScheduleEnabled && syncScheduleShopSids.length === 0"
            class="m-0 text-12/18 text-body [overflow-wrap:anywhere]"
            role="alert"
          >
            启用定时同步前请至少选择一家店铺。
          </p>

          <div class="ml-[264px] flex items-center gap-10 pt-14">
            <AppButton
              variant="primary"
              type="button"
              :disabled="!syncScheduleCanSave"
              :aria-busy="syncSettingsSaving ? 'true' : undefined"
              :class="focusRing"
              @click="saveSyncSchedule"
            >
              保存
            </AppButton>
            <span v-if="syncSettingsMessage" :class="metaText" role="status">
              {{ syncSettingsMessage }}
            </span>
            <span
              v-if="syncSettingsError && syncSettingsStatus === 'ready'"
              :class="metaText"
              role="alert"
            >
              {{ syncSettingsError }}
            </span>
          </div>
        </div>
      </div>

      <div :class="toolbarClass">
        <div class="flex min-w-0 flex-1 items-center gap-6 overflow-hidden">
          <span :class="toolbarSummaryClass" aria-live="polite">
            <template v-if="shopsStatus === 'success'">
              共 {{ totalCount }} 家店铺<span v-if="totalCount > 0">，共 {{ pageCount }} 页</span>
            </template>
            <template v-else-if="shopsStatus === 'loading'">正在加载店铺列表</template>
            <template v-else-if="configStatus === 'unconfigured'"
              >保存 ERP AccessToken 后可查看店铺</template
            >
            <template v-else>店铺列表</template>
          </span>
          <span :class="toolbarStatusClass" aria-live="polite">
            <AppTag :tone="configTagClass">{{ configLabel }}</AppTag>
            <AppTag v-if="shopsLabel" :tone="shopsTagClass">{{ shopsLabel }}</AppTag>
          </span>
        </div>
        <div class="ml-auto flex shrink-0 items-center gap-6">
          <AppIconButton
            :label="retryLabel"
            tone="toolbar"
            :disabled="isLoading"
            :aria-busy="isLoading ? 'true' : undefined"
            :class="focusRing"
            @click="retry"
          >
            <AppIcon name="i-refresh" />
          </AppIconButton>
        </div>
      </div>

      <div :class="tableRegionClass">
        <AppTable
          variant="plain"
          wrap
          scroll-y
          :class="tableBoxClass"
          caption="订单同步店铺列表"
          label="订单同步店铺表格"
        >
          <colgroup>
            <col style="width: 12%" />
            <col style="width: 30%" />
            <col style="width: 16%" />
            <col style="width: 12%" />
            <col v-if="orderSyncEnabled" style="width: 30%" />
          </colgroup>
          <thead>
            <tr>
              <AppTh surface="page">SID</AppTh>
              <AppTh surface="page">名称</AppTh>
              <AppTh surface="page">平台</AppTh>
              <AppTh surface="page">区域</AppTh>
              <AppTh v-if="orderSyncEnabled" surface="page">历史订单</AppTh>
            </tr>
          </thead>
          <tbody>
            <tr v-for="shop in shops" :key="shop.sid">
              <AppTd :title="shop.sid">{{ shop.sid }}</AppTd>
              <AppTd :title="shopName(shop)">{{ shopName(shop) }}</AppTd>
              <AppTd>{{ shop.platform }}</AppTd>
              <AppTd>{{ regionLabel(shop.region) }}</AppTd>
              <AppTd v-if="orderSyncEnabled">
                <div class="flex flex-col items-start gap-8">
                  <AppButton
                    type="button"
                    :aria-label="`同步历史订单：${shopName(shop)}`"
                    :aria-busy="submittingSid === Number(shop.sid) ? 'true' : undefined"
                    :disabled="orderSyncButtonDisabled"
                    :class="focusRing"
                    @click="syncShop(shop)"
                  >
                    同步历史订单
                  </AppButton>
                  <div
                    v-if="orderSyncTasks[Number(shop.sid)]"
                    class="flex w-full min-w-0 flex-col gap-5 text-12 text-body"
                  >
                    <div class="flex items-center gap-8 text-body">
                      <AppTag :tone="orderSyncStatusClass(orderSyncTasks[Number(shop.sid)].status)">
                        {{ orderSyncStatusLabel(orderSyncTasks[Number(shop.sid)].status) }}
                      </AppTag>
                      <span>{{ orderSyncStageLabel(orderSyncTasks[Number(shop.sid)].stage) }}</span>
                    </div>
                    <AppProgress
                      :value="orderSyncProgress(orderSyncTasks[Number(shop.sid)])"
                      label="历史订单同步进度"
                      :tone="orderSyncProgressTone(orderSyncTasks[Number(shop.sid)])"
                    />
                    <p :class="countText">
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
                      :class="countText"
                    >
                      {{ reason }}：{{ count }}
                    </p>
                    <p v-if="orderSyncTasks[Number(shop.sid)].error" :class="countText">
                      {{ orderSyncTasks[Number(shop.sid)].error }}
                    </p>
                  </div>
                  <p v-if="orderSyncErrors[Number(shop.sid)]" :class="countText" role="alert">
                    {{ orderSyncErrors[Number(shop.sid)] }}
                  </p>
                </div>
              </AppTd>
            </tr>
          </tbody>
          <template #overlay>
            <AppEmptyState v-if="isLoading" role="status">正在加载店铺列表…</AppEmptyState>
            <AppEmptyState v-else-if="shopsStatus === 'success' && shops.length === 0">
              <div class="flex flex-col items-center gap-8">
                <p class="m-0">本地暂无店铺，请先同步授权店铺目录。</p>
                <AppButton
                  to="/shops"
                  :class="[focusRing, 'pointer-events-auto']"
                  @click="emit('close')"
                >
                  前往授权店铺
                </AppButton>
              </div>
            </AppEmptyState>
            <AppEmptyState v-else-if="configStatus === 'unconfigured'">
              配置完成后，这里会显示可同步的店铺
            </AppEmptyState>
          </template>
          <template #footer>
            <AppPager
              :page="currentPage"
              :page-count="pageCount"
              :show-page-list="pageCount > 0"
              :disabled="paginationDisabled"
              as="div"
              focus-ring
              :padding-x="16"
              class="border-t border-t-line-soft"
              @update:page="handlePageChange"
            >
              <template #prefix>
                <span>共 {{ totalCount }} 条</span>
              </template>
              <template #suffix>
                <label :class="paginationDisabled ? pageSizeDisabledClass : pageSizeClass">
                  <select
                    :value="pageSize"
                    :disabled="paginationDisabled"
                    aria-label="每页条数"
                    :class="pageSizeSelectClass"
                    @change="handlePageSizeSelect"
                  >
                    <option v-for="size in pageSizeOptions" :key="size" :value="size">
                      {{ size }}条/页
                    </option>
                  </select>
                  <AppIcon name="i-chev-down" :size="10" class="text-placeholder" />
                </label>
                <span class="inline-flex items-center gap-7 pl-3 text-body">
                  前往<input
                    v-model="jumpPage"
                    inputmode="numeric"
                    aria-label="前往页码"
                    :disabled="paginationDisabled"
                    :class="jumpInputClass"
                    @keyup.enter="submitJumpPage"
                    @blur="submitJumpPage"
                  />页
                </span>
              </template>
            </AppPager>
          </template>
        </AppTable>
      </div>
    </div>
  </AppDialog>
</template>

<style scoped>
#order-sync-panel::backdrop {
  overflow: hidden;
  overscroll-behavior: contain;
  background: var(--color-mask-notice);
}
</style>
