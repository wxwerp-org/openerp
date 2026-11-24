<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { account, apiRequest } from '../account/session'
import ErpAccessNotice from '../components/ErpAccessNotice.vue'
import AppButton from '../ui/AppButton.vue'
import AppCheckbox from '../ui/AppCheckbox.vue'
import AppDialog from '../ui/AppDialog.vue'
import AppEmptyState from '../ui/AppEmptyState.vue'
import AppIcon from '../ui/AppIcon.vue'
import AppIconButton from '../ui/AppIconButton.vue'
import AppNotice from '../ui/AppNotice.vue'
import AppPagination from '../ui/AppPagination.vue'
import AppProgress from '../ui/AppProgress.vue'
import AppTable from '../ui/AppTable.vue'
import AppTag from '../ui/AppTag.vue'
import AppTd from '../ui/AppTd.vue'
import AppTh from '../ui/AppTh.vue'
import type { useErpHub } from '../composables/useErpHub'
import { httpErrorMessage, parseConfig, parseShopsResponse, type Shop } from './orderSyncResponses'
import { useSyncSchedule, type ConfigStatus } from './useSyncSchedule'
import {
  orderSyncProgress,
  orderSyncProgressTone,
  orderSyncStageLabel,
  orderSyncStatusClass,
  orderSyncStatusLabel,
  type TagClass,
} from './orderSyncPresentation'

const props = defineProps<{
  open: boolean
  hub: ReturnType<typeof useErpHub>
}>()
const emit = defineEmits<{ close: [] }>()
const { state: hubState, orderSyncTasks, startOrderSync, startAllOrderSync, connect } = props.hub
const submittingSids = ref(new Set<number>())
const submittingAll = ref(false)
const batchMessage = ref('')
const batchError = ref('')

type ShopsStatus = 'idle' | 'loading' | 'success' | 'error'
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
const employee = computed(() => account.value?.role === 'User')
const canManageSchedule = computed(
  () => orderSyncEnabled.value && account.value?.role === 'Administrator',
)

const schedule = useSyncSchedule({ canManage: canManageSchedule, configStatus })
const {
  syncSettingsStatus,
  syncSettingsError,
  syncSettingsMessage,
  syncSettingsSaving,
  syncScheduleEnabled,
  syncScheduleShopSids,
  syncScheduleIntervalMinutes,
  syncScheduleShopsStatus,
  syncScheduleShopsError,
  syncSchedulePickerShops,
  syncScheduleCanSave,
  loadSyncSchedule,
  retrySyncShops,
  saveSyncSchedule,
  selectAllSyncShops,
  clearSyncShops,
  clearSyncSettingsMessage,
} = schedule

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

const noticeKind = computed<'unconfigured' | 'error' | null>(() => {
  if (configStatus.value === 'unconfigured') return 'unconfigured'
  if (shopsStatus.value === 'error') return 'error'
  return null
})

const retryLabel = computed(() =>
  shopsStatus.value === 'error' || configStatus.value === 'error' ? '重试' : '刷新',
)

const paginationDisabled = computed(() => isLoading.value || pageCount.value < 1)

let requestVersion = 0
let activeController: AbortController | null = null

function clearShops(): void {
  shops.value = []
  totalCount.value = 0
  pageCount.value = 0
}

function setOrderSyncEnabled(enabled: boolean): void {
  orderSyncEnabled.value = enabled
  if (canManageSchedule.value) {
    schedule.activate()
    return
  }

  schedule.reset()
  if (!enabled) orderSyncErrors.value = {}
}

function shopSyncDisabled(sid: number): boolean {
  return (
    !canStartOrderSync.value ||
    submittingSids.value.has(sid) ||
    orderSyncTasks.value[sid]?.status === 'running'
  )
}

async function syncShop(sid: number): Promise<void> {
  if (shopSyncDisabled(sid)) return
  submittingSids.value.add(sid)
  delete orderSyncErrors.value[sid]

  try {
    const result = await startOrderSync(sid)
    if (!result.accepted) {
      orderSyncErrors.value[sid] = result.error ?? '无法启动订单同步，请稍后重试。'
    }
  } finally {
    submittingSids.value.delete(sid)
  }
}

const canStartOrderSync = computed(
  () =>
    orderSyncEnabled.value && configStatus.value === 'configured' && hubState.value === 'connected',
)
const canSyncAll = computed(
  () =>
    canStartOrderSync.value &&
    shopsStatus.value === 'success' &&
    totalCount.value > 0 &&
    !submittingAll.value,
)

async function syncAll(): Promise<void> {
  if (!canSyncAll.value) return
  submittingAll.value = true
  batchMessage.value = ''
  batchError.value = ''
  try {
    const result = await startAllOrderSync()
    if (!result.accepted) {
      batchError.value = result.error ?? '无法启动全部同步，请稍后重试。'
      return
    }
    orderSyncErrors.value = {}
    batchMessage.value = result.busyCount
      ? `已启动 ${result.startedCount} 家店铺同步，${result.busyCount} 家正在同步的店铺已跳过。`
      : `已启动 ${result.startedCount} 家店铺同步。`
  } finally {
    submittingAll.value = false
  }
}

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

      const configResponse = await apiRequest('/api/erp/order-sync-availability', {
        signal: controller.signal,
      })
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
  'm-auto w-[min(1040px,calc(100%_-_24px))] max-h-[calc(100%_-_24px)] overflow-y-auto rounded-8 border border-line bg-page p-12 text-ink [&:modal]:overscroll-contain'
const toolbarClass =
  'flex h-46 shrink-0 items-center gap-6 overflow-hidden rounded-t-6 bg-white px-16'
const toolbarSummaryClass =
  'min-w-0 overflow-hidden text-12/18 text-muted text-ellipsis whitespace-nowrap'
const toolbarStatusClass = 'inline-flex shrink-0 items-center gap-6'
const tableRegionClass = 'max-h-360 min-h-320 flex-[1_0_320px] bg-transparent pb-16'
const tableBoxClass = 'overflow-hidden rounded-b-6'
const syncCardClass =
  'mb-12 flex max-h-[50vh] flex-none flex-col overflow-hidden rounded-6 bg-white'
const rowClass =
  'grid grid-cols-[240px_minmax(0,1fr)] items-start justify-start gap-24 border-b border-b-line-soft py-16'
const hintClass = 'py-6 text-12/18 text-muted'
const metaText = 'text-12/18 text-body [overflow-wrap:anywhere]'
const countText = 'm-0 text-12 leading-[1.45] text-pretty text-body [overflow-wrap:anywhere]'
const shopOptionClass =
  'flex cursor-pointer items-center gap-6 px-2 py-3 text-12/18 text-body hover:bg-primary-faint has-[input:focus-visible]:outline-2 has-[input:focus-visible]:outline-offset-2 has-[input:focus-visible]:outline-primary'

let loadedOnOpen = false
watch(
  () => props.open,
  (open) => {
    if (!open) return

    if (!loadedOnOpen || employee.value) {
      loadedOnOpen = true
      void loadPage(currentPage.value, pageSize.value, true)
    }
    if (hubState.value === 'disconnected') void connect()
  },
  { immediate: true },
)

watch(hubState, (state, previous) => {
  if (!props.open || !employee.value || state !== 'connected' || previous === 'idle') return
  orderSyncErrors.value = {}
  void loadPage(currentPage.value, pageSize.value)
})

onUnmounted(() => {
  schedule.dispose()
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
        v-if="canManageSchedule"
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

          <div class="ml-264 flex items-center gap-10 pt-14">
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

      <p v-if="batchError" role="alert" class="m-0 mb-6 text-12/18 text-danger">{{ batchError }}</p>
      <p v-else-if="batchMessage" role="status" class="m-0 mb-6 text-12/18 text-body">
        {{ batchMessage }}
      </p>
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
          <AppButton
            v-if="orderSyncEnabled"
            variant="primary"
            type="button"
            :disabled="!canSyncAll"
            :aria-busy="submittingAll || undefined"
            :class="focusRing"
            @click="syncAll"
            >全部同步</AppButton
          >
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
            <col :class="orderSyncEnabled ? 'w-1/10' : 'w-[12%]'" />
            <col :class="orderSyncEnabled ? 'w-[24%]' : 'w-3/10'" />
            <col :class="orderSyncEnabled ? 'w-1/10' : 'w-[16%]'" />
            <col :class="orderSyncEnabled ? 'w-1/10' : 'w-[12%]'" />
            <col v-if="orderSyncEnabled" class="w-[46%]" />
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
                    :aria-busy="submittingSids.has(Number(shop.sid)) ? 'true' : undefined"
                    :disabled="shopSyncDisabled(Number(shop.sid))"
                    :class="focusRing"
                    @click="syncShop(Number(shop.sid))"
                  >
                    同步历史订单
                  </AppButton>
                  <div
                    v-if="orderSyncTasks[Number(shop.sid)]"
                    class="@container flex w-full min-w-0 flex-col gap-5 text-12 text-body"
                  >
                    <div class="flex flex-wrap items-center gap-x-8 gap-y-4 text-body">
                      <AppTag :tone="orderSyncStatusClass(orderSyncTasks[Number(shop.sid)].status)">
                        {{ orderSyncStatusLabel(orderSyncTasks[Number(shop.sid)]) }}
                      </AppTag>
                      <span>{{ orderSyncStageLabel(orderSyncTasks[Number(shop.sid)].stage) }}</span>
                    </div>
                    <AppProgress
                      :value="orderSyncProgress(orderSyncTasks[Number(shop.sid)])"
                      label="历史订单同步进度"
                      :tone="orderSyncProgressTone(orderSyncTasks[Number(shop.sid)])"
                    />
                    <p class="m-0 grid gap-x-8 text-12/18 tabular-nums @min-[32em]:grid-cols-2">
                      <span>
                        读取 {{ orderSyncTasks[Number(shop.sid)].read }} /
                        {{ orderSyncTasks[Number(shop.sid)].total }}
                      </span>
                      <span>已处理 {{ orderSyncTasks[Number(shop.sid)].processed }}</span>
                    </p>
                    <p
                      class="m-0 grid gap-x-8 text-12/18 tabular-nums @min-[17em]:grid-cols-2 @min-[34em]:grid-cols-4"
                    >
                      <span>新增 {{ orderSyncTasks[Number(shop.sid)].added }}</span>
                      <span>更新 {{ orderSyncTasks[Number(shop.sid)].updated }}</span>
                      <span>未变 {{ orderSyncTasks[Number(shop.sid)].unchanged }}</span>
                      <span>跳过 {{ orderSyncTasks[Number(shop.sid)].skipped }}</span>
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
                <p class="m-0">
                  {{
                    employee
                      ? '暂无可同步的店铺，请联系管理员确认店铺分配。'
                      : '本地暂无店铺，请先同步授权店铺目录。'
                  }}
                </p>
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
            <AppPagination
              :page="currentPage"
              :page-count="pageCount"
              :page-size="pageSize"
              :total-text="`共 ${totalCount} 条`"
              :disabled="paginationDisabled"
              as="div"
              focus-ring
              :padding-x="16"
              class="border-t border-t-line-soft"
              @update:page="handlePageChange"
              @update:page-size="handlePageSizeChange"
            />
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
