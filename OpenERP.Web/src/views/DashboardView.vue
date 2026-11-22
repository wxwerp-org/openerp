<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { account } from '../account/session'
import OrderSyncDialog from '../forms/OrderSyncDialog.vue'
import { useErpHub } from '../composables/useErpHub'
import AppButton from '../ui/AppButton.vue'
import AppCard from '../ui/AppCard.vue'
import OnboardingRibbon from '../components/workspace/OnboardingRibbon.vue'
import WorkspaceHealthCard from '../components/workspace/WorkspaceHealthCard.vue'
import WorkspaceMetricCard from '../components/workspace/WorkspaceMetricCard.vue'
import WorkspacePlatformCard from '../components/workspace/WorkspacePlatformCard.vue'
import WorkspaceShopRankCard from '../components/workspace/WorkspaceShopRankCard.vue'
import WorkspaceStatusBar from '../components/workspace/WorkspaceStatusBar.vue'
import WorkspaceSyncCard from '../components/workspace/WorkspaceSyncCard.vue'
import WorkspaceSystemCard from '../components/workspace/WorkspaceSystemCard.vue'
import WorkspaceTrendCard from '../components/workspace/WorkspaceTrendCard.vue'
import { useWorkspaceDashboard } from '../workspace/useWorkspaceDashboard'
import { onboardingSummaryDismissed } from '../workspace/onboardingState'
import {
  formatCount,
  formatDateTime,
  formatMoney,
  platformTotals,
  shopNameBySid,
  shopNameMap,
} from '../workspace/workspaceData'
import {
  buildOnboardingSteps,
  type DashboardState,
  nextOnboardingLabel,
  platformDrillQuery,
  rankTopShops,
  resolveDashboardState,
  resolveTrendRange,
  shopDrillQuery,
  type OnboardingStep,
  type TrendPreset,
  type TrendRange,
} from '../workspace/workspaceDerivation'

const authorizeUrl = 'https://v3.wxwerp.com/system/authorize'

const router = useRouter()
const administrator = computed(() => account.value?.role === 'Administrator')

const {
  report,
  reportStatus,
  reportError,
  reportRefreshing,
  reportStale,
  directory,
  directoryStatus,
  directoryError,
  config,
  configStatus,
  configError,
  availability,
  availabilityStatus,
  availabilityError,
  validTemuShopCount,
  loadReport,
  loadDirectory,
  loadValidTemuShopCount,
  loadConfig,
  loadAvailability,
  refresh,
} = useWorkspaceDashboard({ administrator: administrator.value })

const hub = useErpHub({
  shopSync: administrator.value,
  orderSync: true,
  onOrdersChanged: () => {
    refreshTrend()
    void loadDirectory()
  },
})

const {
  state: hubState,
  status: shopSyncStatus,
  orderSyncTasks,
  connect,
  disconnect,
  reconnect,
  startShopSync,
  startAllOrderSync,
} = hub

const trendPreset = ref<TrendPreset>('last7')
/** 最近一次应用的自定义区间；失败时仍保留，供刷新与重新应用重试。 */
const customTrendRange = ref<TrendRange | null>(null)
/** 已成功展示的周期：请求失败时停在旧周期，再次切换才重新取数。 */
const displayedTrendPreset = ref<TrendPreset>('last7')
const syncOpen = ref(false)
const startingAll = ref(false)
const batchMessage = ref('')
const batchError = ref('')
const syncMessage = ref('')
const shopSyncStarting = ref(false)
let disposed = false

const orderCount = computed(() => report.value?.summary.orderCount ?? null)
const directorySummary = computed(() => directory.value?.summary ?? null)
const employeeHasNoShops = computed(
  () =>
    !administrator.value &&
    directoryStatus.value === 'success' &&
    directory.value?.items.length === 0,
)
const shopNames = computed(() => shopNameMap(directory.value))
const shopNamesSid = computed(() => shopNameBySid(directory.value))
const orderTasks = computed(() => Object.values(orderSyncTasks.value))

const runningTasks = computed(() => orderTasks.value.filter((task) => task.status === 'running'))
const shopSyncRunning = computed(
  () => administrator.value && (shopSyncStatus.value?.running ?? false),
)

const platformRows = computed(() => platformTotals(report.value?.groups ?? []))

const topShops = computed(() => rankTopShops(report.value?.groups ?? [], shopNames.value))

const trend = computed(() => report.value?.trend ?? null)

const salesAmount = computed(() => report.value?.sales.amount ?? null)
const salesDetail = computed(() => {
  const sales = report.value?.sales
  if (!sales || sales.knownOrderCount > 0 || report.value?.summary.orderCount === 0)
    return undefined
  return '暂无可用金额，重新同步订单后可补齐'
})

const state = computed<DashboardState>(() =>
  resolveDashboardState({
    hasSnapshot: report.value !== null,
    reportStatus:
      reportStatus.value === 'success' || reportStatus.value === 'error'
        ? reportStatus.value
        : 'loading',
    orderCount: report.value?.summary.orderCount ?? 0,
    runningOrderTasks: runningTasks.value.length,
    administrator: administrator.value,
    shopSyncRunning: shopSyncRunning.value,
    shopSyncStarting: shopSyncStarting.value,
  }),
)

type MetricCard = {
  label: string
  value: string
  detail?: string
  testid: string
  to: { path: string }
  toLabel?: string
}

const metricCards = computed<MetricCard[]>(() => {
  const cards: MetricCard[] = [
    {
      label: '销售额（已同步）',
      value: formatMoney(salesAmount.value, report.value?.sales.currency ?? null),
      detail: salesDetail.value,
      testid: 'metric-sales',
      to: { path: '/reports/orders' },
    },
    {
      label: '订单数',
      value: formatCount(orderCount.value),
      testid: 'metric-orders',
      to: { path: '/reports/orders' },
    },
    {
      label: '商品件数',
      value: formatCount(report.value?.summary.itemQuantity ?? null),
      testid: 'metric-items',
      to: { path: '/reports/orders' },
    },
  ]
  if (administrator.value) {
    cards.push({
      label: '目录最近同步时间',
      value:
        directoryStatus.value === 'success'
          ? formatDateTime(directorySummary.value?.lastSyncedAt ?? null)
          : directoryStatus.value === 'error'
            ? '读取失败'
            : '—',
      testid: 'metric-last-sync',
      to: { path: '/shops' },
      toLabel: '查看授权店铺',
    })
  } else {
    cards.push({
      label: '有订单平台',
      value: formatCount(report.value ? report.value.platforms.length : null),
      testid: 'metric-platforms',
      to: { path: '/reports/orders' },
    })
  }
  return cards
})

const validTemuCount = computed(() => validTemuShopCount.value ?? 0)

const onboardingSteps = computed<OnboardingStep[]>(() =>
  buildOnboardingSteps({
    configured: config.value?.configured ?? null,
    directoryItems: directory.value?.items.length ?? 0,
    directoryLoaded: directoryStatus.value === 'success',
    validTemuShopCount: validTemuCount.value,
    orderSyncEnabled: availability.value?.orderSyncEnabled === true,
    lastSyncedAt: directorySummary.value?.lastSyncedAt ?? null,
    orderCount: orderCount.value ?? 0,
  }),
)

const onboardingDoneCount = computed(() => onboardingSteps.value.filter((step) => step.done).length)
const onboardingCompleted = computed(
  () => onboardingDoneCount.value === onboardingSteps.value.length,
)
/** 完成摘要同一次会话只默认出现一次；离开页面后不再自动出现。 */
const completionDisplayed = ref(false)
watch(onboardingCompleted, (completed) => {
  if (completed) completionDisplayed.value = true
})
const onboardingNextLabel = computed(() => nextOnboardingLabel(onboardingSteps.value))

const systemStatus = computed<'idle' | 'loading' | 'success' | 'error'>(() => {
  const statuses = [configStatus.value, availabilityStatus.value]
  if (statuses.includes('error')) return 'error'
  if (statuses.includes('loading')) return 'loading'
  return 'success'
})
const systemError = computed(() => configError.value || availabilityError.value)

function drillPlatform(platform: string): void {
  void router.push({ path: '/reports/orders', query: platformDrillQuery(platform) })
}

function drillShop(key: string): void {
  void router.push({ path: '/reports/orders', query: shopDrillQuery(key) })
}

/** 手动刷新同时重建实时连接：在线漏送终态时靠服务端快照恢复。 */
async function refreshAll(): Promise<void> {
  await reconnect()
  if (disposed) return
  const preset = trendPreset.value
  const range = currentTrendRange()
  // 周期与报表在 loadReport 的版本校验内一起提交；这里不能再按 refresh 的完成时机回写。
  await refresh(range, () => {
    displayedTrendPreset.value = preset
  })
}

async function syncDirectory(): Promise<void> {
  if (shopSyncStarting.value) return
  shopSyncStarting.value = true
  syncMessage.value = ''
  try {
    const result = await startShopSync()
    if (!result.accepted) {
      syncMessage.value = result.error ?? '无法启动目录同步，请稍后重试。'
      return
    }
    void loadDirectory()
    void loadValidTemuShopCount()
  } finally {
    shopSyncStarting.value = false
  }
}

async function startAllSync(): Promise<void> {
  if (startingAll.value) return
  startingAll.value = true
  batchMessage.value = ''
  batchError.value = ''
  try {
    const result = await startAllOrderSync()
    if (!result.accepted) {
      batchError.value = result.error ?? '无法启动全部同步。'
      return
    }
    batchMessage.value =
      result.busyCount > 0
        ? `已开始 ${result.startedCount} 家；${result.busyCount} 家已有店铺正在同步。`
        : `已开始同步 ${result.startedCount} 家店铺。`
  } finally {
    startingAll.value = false
  }
}

function runOnboardingAction(stepId: string): void {
  if (stepId === 'configure') {
    void router.push('/settings?section=erp')
    return
  }
  if (stepId === 'authorize') {
    window.open(authorizeUrl, '_blank', 'noopener')
    return
  }
  if (stepId === 'sync-directory') {
    void syncDirectory()
    return
  }
  syncOpen.value = true
}

/**
 * 任务开始与失败时刷新报表；进度更新不触发。
 * 成功/部分成功由 Hub 的 onOrdersChanged 统一触发，避免同一终态重复取数。
 */
const taskStatuses = new Map<number, string>()
watch(
  () => orderSyncTasks.value,
  (tasks) => {
    let changed = false
    for (const task of Object.values(tasks)) {
      const previous = taskStatuses.get(task.sid)
      if (previous !== task.status) {
        taskStatuses.set(task.sid, task.status)
        if (task.status === 'running' || task.status === 'failed') changed = true
      }
    }
    if (changed) refreshTrend()
  },
)

/** 目录同步结束（终态或失败）后刷新目录汇总。 */
watch(
  () => shopSyncStatus.value?.version,
  () => {
    const status = shopSyncStatus.value
    if (status && !status.running) {
      void loadDirectory()
      void loadValidTemuShopCount()
    }
  },
)

function handleVisibilityChange(): void {
  if (document.visibilityState !== 'visible') return
  refreshTrend()
  void loadDirectory()
}

function currentTrendRange(): TrendRange {
  return resolveTrendRange(trendPreset.value, new Date(), customTrendRange.value)
}

async function loadTrend(preset: TrendPreset, range: TrendRange): Promise<void> {
  trendPreset.value = preset
  if (preset === 'custom') customTrendRange.value = { ...range }
  await loadReport(range, () => {
    displayedTrendPreset.value = preset
  })
}

/** 用当前选择重新取数；季度/本年按「至今」重算，自定义区间保持已应用的起止日期。 */
function refreshTrend(): void {
  void loadTrend(trendPreset.value, currentTrendRange())
}

function selectTrendPreset(preset: TrendPreset): void {
  if (preset === 'custom') return
  trendPreset.value = preset
  // 已展示同一周期且没有在途请求时才去重；若有在途请求（用户刚切到别的周期），
  // 切回已展示周期必须重新取数，让旧响应按请求版本失效。
  if (preset === displayedTrendPreset.value && !reportRefreshing.value) return
  void loadTrend(preset, resolveTrendRange(preset))
}

function applyCustomRange(range: TrendRange): void {
  void loadTrend('custom', range)
}

function retrySystem(): void {
  void loadConfig()
  void loadAvailability()
}

onMounted(() => {
  void connect()
  document.addEventListener('visibilitychange', handleVisibilityChange)
})

onUnmounted(() => {
  document.removeEventListener('visibilitychange', handleVisibilityChange)
  if (completionDisplayed.value) onboardingSummaryDismissed.value = true
  disposed = true
  void disconnect()
})
</script>

<template>
  <section
    id="page-dashboard"
    data-testid="page-dashboard"
    class="flex h-full min-h-0 w-full flex-col bg-page"
  >
    <h1 class="sr-only">工作台</h1>

    <WorkspaceStatusBar
      v-if="state !== 'loading' && state !== 'error'"
      :generated-at="report?.generatedAtUtc ?? null"
      :last-synced-at="directorySummary?.lastSyncedAt ?? null"
      :show-directory="administrator"
      :refreshing="reportRefreshing"
      :stale="reportStale"
      @refresh="refreshAll()"
    />

    <div class="min-h-0 flex-1 overflow-x-hidden overflow-y-auto">
      <div
        v-if="state === 'loading'"
        class="grid grid-cols-12 gap-12 p-16"
        data-testid="dashboard-skeleton"
      >
        <div
          v-for="index in 4"
          :key="index"
          class="col-span-12 h-104 animate-pulse rounded-6 bg-white lg:col-span-6 xl:col-span-3"
        ></div>
        <div class="col-span-12 h-460 animate-pulse rounded-6 bg-white"></div>
        <div class="col-span-12 h-296 animate-pulse rounded-6 bg-white lg:col-span-6"></div>
        <div class="col-span-12 h-296 animate-pulse rounded-6 bg-white lg:col-span-6"></div>
        <div
          class="col-span-12 h-140 animate-pulse rounded-6 bg-white lg:col-span-6 xl:col-span-4"
        ></div>
        <div
          class="col-span-12 h-140 animate-pulse rounded-6 bg-white lg:col-span-6 xl:col-span-4"
        ></div>
        <div
          class="col-span-12 h-140 animate-pulse rounded-6 bg-white lg:col-span-12 xl:col-span-4"
        ></div>
      </div>

      <div
        v-else-if="state === 'error'"
        class="flex h-full flex-col items-center justify-center gap-12 p-24"
      >
        <AppCard class="flex max-w-420 flex-col items-center gap-10 p-28 text-center">
          <strong class="text-14 font-semibold text-ink">工作台数据加载失败</strong>
          <span class="text-12 text-muted">{{ reportError }}</span>
          <AppButton variant="primary" @click="refreshTrend()">重试</AppButton>
        </AppCard>
      </div>

      <div v-else class="p-16">
        <div class="grid grid-cols-12 gap-12">
          <OnboardingRibbon
            v-if="state === 'empty' && administrator"
            :steps="onboardingSteps"
            :next-label="onboardingNextLabel"
            :done-count="onboardingDoneCount"
            @action="runOnboardingAction"
          />
          <AppCard
            v-if="state === 'empty' && !administrator"
            class="col-span-12 px-16 py-14"
            data-testid="employee-empty-hint"
          >
            <template v-if="employeeHasNoShops">
              <p class="m-0 mb-8 text-12 text-body">
                尚无可见店铺。新店在旺销网授权后，请联系管理员同步目录并分配。
              </p>
              <AppButton :to="{ path: '/shops' }">前往授权店铺</AppButton>
            </template>
            <p v-else class="m-0 text-12 text-body">当前还没有订单数据。</p>
          </AppCard>

          <AppCard
            v-if="
              state === 'data' &&
              administrator &&
              onboardingCompleted &&
              !onboardingSummaryDismissed
            "
            class="col-span-12 flex h-36 items-center gap-8 px-16"
            data-testid="onboarding-completed"
          >
            <span class="text-12 font-semibold text-ink">初始化已完成</span>
            <span class="text-12 text-body"
              >四步引导已全部完成，可随时在「授权店铺」「ERP 接入」调整。</span
            >
            <button
              type="button"
              class="ml-auto text-12 text-body hover:text-primary"
              data-testid="onboarding-completed-dismiss"
              @click="onboardingSummaryDismissed = true"
            >
              收起
            </button>
          </AppCard>

          <WorkspaceMetricCard
            v-for="card in metricCards"
            :key="card.testid"
            class="col-span-12 lg:col-span-6 xl:col-span-3"
            :label="card.label"
            :value="card.value"
            :detail="card.detail"
            :testid="card.testid"
            :to="card.to"
            :to-label="card.toLabel"
          />

          <WorkspaceTrendCard
            v-if="trend"
            :trend="trend"
            :preset="displayedTrendPreset"
            :custom-range="customTrendRange"
            @preset-change="selectTrendPreset"
            @custom-apply="applyCustomRange"
            @open="router.push('/reports/orders')"
          />

          <template v-if="state === 'data'">
            <WorkspacePlatformCard
              class="col-span-12 lg:col-span-6"
              :totals="platformRows"
              :total-orders="orderCount ?? 0"
              @select="drillPlatform"
              @open="router.push('/reports/orders')"
            />
            <WorkspaceShopRankCard
              class="col-span-12 lg:col-span-6"
              :rows="topShops"
              @select="drillShop"
              @open="router.push('/reports/orders')"
            />
          </template>

          <template v-else-if="state === 'syncing'">
            <AppCard class="col-span-12 px-16 py-14" data-testid="dashboard-syncing">
              <div class="flex items-center gap-10">
                <span class="text-13 font-semibold text-ink">正在同步数据</span>
                <span class="text-11 text-muted">可离开页面，任务在后台继续</span>
                <button
                  type="button"
                  class="ml-auto text-11 text-primary hover:underline"
                  @click="syncOpen = true"
                >
                  查看同步详情
                </button>
              </div>
              <div class="mt-8 grid grid-cols-1 gap-8 lg:grid-cols-3">
                <div v-if="administrator" class="rounded-4 bg-page px-10 py-8 text-12">
                  <div class="font-medium text-body-strong">店铺目录</div>
                  <div class="mt-2 text-muted">
                    <template v-if="shopSyncStatus?.running">
                      已检查 {{ formatCount(shopSyncStatus.checked) }} /
                      {{ shopSyncStatus.total > 0 ? formatCount(shopSyncStatus.total) : '—' }}，有效
                      {{ formatCount(shopSyncStatus.valid) }}，无效
                      {{ formatCount(shopSyncStatus.invalid) }}
                    </template>
                    <template v-else>目录同步未在运行</template>
                  </div>
                </div>
                <div class="rounded-4 bg-page px-10 py-8 text-12">
                  <div class="font-medium text-body-strong">订单同步</div>
                  <div class="mt-2 text-muted">
                    进行中 {{ runningTasks.length }} 家，排队
                    {{
                      orderTasks.filter(
                        (task) => task.status === 'running' && task.stage === 'queued',
                      ).length
                    }}
                    家
                  </div>
                </div>
                <div class="rounded-4 bg-page px-10 py-8 text-12">
                  <div class="font-medium text-body-strong">本地订单</div>
                  <div class="mt-2 text-muted">当前为 0 单</div>
                </div>
              </div>
            </AppCard>
          </template>

          <template v-else>
            <WorkspacePlatformCard
              class="col-span-12 lg:col-span-6"
              :totals="[]"
              :total-orders="0"
              @select="drillPlatform"
              @open="router.push('/reports/orders')"
            />
            <WorkspaceShopRankCard
              class="col-span-12 lg:col-span-6"
              :rows="[]"
              @select="drillShop"
              @open="router.push('/reports/orders')"
            />
          </template>

          <WorkspaceHealthCard
            v-if="administrator"
            class="col-span-12 lg:col-span-6 xl:col-span-4"
            :summary="directorySummary"
            :status="directoryStatus"
            :error="directoryError"
            :running="shopSyncRunning"
            @sync="syncDirectory"
            @retry="loadDirectory()"
            @open="router.push('/shops')"
          />
          <WorkspaceSyncCard
            class="col-span-12"
            :class="administrator ? 'lg:col-span-6 xl:col-span-4' : ''"
            :administrator="administrator"
            :hub-state="hubState"
            :shop-sync="shopSyncStatus"
            :order-tasks="orderTasks"
            :shop-names="shopNamesSid"
            :shop-sync-starting="shopSyncStarting"
            :sync-message="syncMessage"
            :starting-all="startingAll"
            :batch-message="batchMessage"
            :batch-error="batchError"
            @sync-shops="syncDirectory"
            @start-all="startAllSync"
            @open="syncOpen = true"
          />
          <WorkspaceSystemCard
            v-if="administrator"
            class="col-span-12 lg:col-span-12 xl:col-span-4"
            :configured="config?.configured ?? null"
            :order-sync-enabled="availability?.orderSyncEnabled ?? null"
            :status="systemStatus"
            :error="systemError"
            @open="router.push('/settings?section=erp')"
            @retry="retrySystem"
          />
        </div>
      </div>
    </div>

    <OrderSyncDialog :open="syncOpen" :hub="hub" @close="syncOpen = false" />
  </section>
</template>
