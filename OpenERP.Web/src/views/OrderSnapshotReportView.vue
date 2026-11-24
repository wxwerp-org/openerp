<script setup lang="ts">
import { computed, onMounted, provide, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import OrderSnapshotOrdersPanel from '../components/reports/OrderSnapshotOrdersPanel.vue'
import OrderSnapshotPlatformDistribution from '../components/reports/OrderSnapshotPlatformDistribution.vue'
import { reportTableScrollClass } from '../components/reports/reportTable'
import OrderSnapshotShopRanking from '../components/reports/OrderSnapshotShopRanking.vue'
import { apiRequest } from '../account/session'
import { platformLabel, type ReportShop } from '../reports/orderSnapshot'
import { parseShopDirectory, shopNameMap } from '../workspace/workspaceData'
import {
  averageItemsPerOrder,
  formatAverageItems,
  formatSnapshotQuantity as formatQuantity,
} from '../reports/orderSnapshotPresentation'
import { useOrderSnapshotReport } from '../reports/useOrderSnapshotReport'
import AppButton from '../ui/AppButton.vue'
import AppCard from '../ui/AppCard.vue'
import AppCheckbox from '../ui/AppCheckbox.vue'
import AppEmptyState from '../ui/AppEmptyState.vue'
import AppIcon from '../ui/AppIcon.vue'
import AppIconButton from '../ui/AppIconButton.vue'
import AppSelect from '../ui/AppSelect.vue'
import AppTag from '../ui/AppTag.vue'
import AppTh from '../ui/AppTh.vue'
import { tableWrapKey } from '../ui/tableContext'

const {
  report,
  availablePlatforms,
  availableShops,
  status,
  errorMessage,
  platform,
  selectedShopKeys,
  currentPage,
  pageSize,
  expandedOrderIds,
  load,
  updateFilters,
  resetFilters,
  changePage,
  toggleOrder,
} = useOrderSnapshotReport()

// AppTable 的滚动区高度由外层 flex 决定，无法表达本页 table-scroll 的 max-height 上限；
// 保留旧 table-area/table-scroll 结构时，用 AppTd 的换行上下文复刻订单明细的 is-wrap 密度。
// 分店汇总表格保持单行密度，单元格按旧 `.grid-table td` 直接写工具类，不加载该上下文。
provide(
  tableWrapKey,
  computed(() => true),
)

const route = useRoute()
const router = useRouter()
const shopNames = ref(new Map<string, string>())

function shopName(shop: ReportShop): string {
  return shopNames.value.get(`${shop.platform}/${shop.shopId}`) ?? shop.label
}

async function loadShopNames(): Promise<void> {
  try {
    const response = await apiRequest('/api/erp/shops?page=1&pageSize=100', { cache: 'no-store' })
    if (!response.ok) return
    shopNames.value = shopNameMap(parseShopDirectory(await response.json()))
  } catch {
    // 目录读取失败时回退后端标签，不影响报表主数据。
  }
}

onMounted(loadShopNames)

function queryText(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function queryList(value: unknown): string[] {
  if (Array.isArray(value)) return value.filter((item): item is string => typeof item === 'string')
  return typeof value === 'string' ? [value] : []
}

/** 请求前剔除格式非法值：后端对空白/超长键返回 400，重复键也会放大筛选状态。 */
function normalizePlatform(value: string): string {
  const trimmed = value.trim()
  return trimmed.length > 0 && trimmed.length <= 100 ? trimmed : ''
}

function normalizeShopKeys(values: string[]): string[] {
  const seen = new Set<string>()
  const keys: string[] = []
  for (const raw of values) {
    const key = raw.trim()
    if (!key || key.length > 1000 || seen.has(key)) continue
    seen.add(key)
    keys.push(key)
    if (keys.length >= 100) break
  }
  return keys
}

/** 工作台下钻通过 URL 带入筛选；非法值在选项到达后丢弃，不报错。 */
platform.value = normalizePlatform(queryText(route.query.platform))
// URL 里的店铺键先不直接发请求：等全量选项返回后过滤未知值，避免非法键挤占 100 家上限或撑爆请求行。
const pendingUrlShops = ref(normalizeShopKeys(queryList(route.query.shops)))
const initialFiltersValidated = ref(false)

function syncFiltersToUrl(): void {
  const query = { ...route.query }
  delete query.platform
  delete query.shops
  if (platform.value) query.platform = platform.value
  if (selectedShopKeys.value.length) query.shops = [...selectedShopKeys.value]
  if (JSON.stringify(query) === JSON.stringify(route.query)) return
  void router.replace({ query })
}

function applyFilters(): void {
  updateFilters()
  syncFiltersToUrl()
}

function clearFilters(): void {
  resetFilters()
  syncFiltersToUrl()
}

function completeUrlFilters(): void {
  if (initialFiltersValidated.value || status.value !== 'success' || !report.value) return
  initialFiltersValidated.value = true
  const platformValid = !platform.value || availablePlatforms.value.includes(platform.value)
  if (!platformValid) platform.value = ''
  const allowed = new Set(availableShops.value.map((shop) => shop.key))
  const shops = pendingUrlShops.value.filter((key) => allowed.has(key)).slice(0, 100)
  const shopsChanged =
    shops.length !== selectedShopKeys.value.length ||
    shops.some((key, index) => key !== selectedShopKeys.value[index])
  pendingUrlShops.value = []
  if (!platformValid || shopsChanged) {
    selectedShopKeys.value = shops
    syncFiltersToUrl()
    void load(1)
    return
  }
  syncFiltersToUrl()
}

watch([report, status], completeUrlFilters, { immediate: true })

watch(
  () => route.query,
  (query) => {
    const nextPlatform = queryText(query.platform)
    const nextShops = queryList(query.shops)
    if (
      nextPlatform === platform.value &&
      nextShops.join('\u0000') === selectedShopKeys.value.join('\u0000')
    )
      return
    platform.value = normalizePlatform(nextPlatform)
    pendingUrlShops.value = normalizeShopKeys(nextShops)
    selectedShopKeys.value = []
    initialFiltersValidated.value = false
    void load(1)
  },
  { deep: true },
)

const platformOptions = computed(() => [
  { value: '', label: '全部平台' },
  ...availablePlatforms.value.map((item) => ({ value: item, label: platformLabel(item) })),
])

/** 分店汇总表沿用旧 `.grid-table td` 的非换行密度。 */
const groupCellClass =
  'h-48 overflow-hidden border-r border-b border-r-line-soft border-b-line-soft bg-white px-8 text-12/18 text-body text-ellipsis whitespace-nowrap group-hover:bg-row-hover last:border-r-0'
const groupNumericCellClass = `${groupCellClass} text-right tabular-nums`

const selectedShopLabel = computed(() =>
  selectedShopKeys.value.length ? `已选 ${selectedShopKeys.value.length} 家店铺` : '全部店铺',
)

const localGeneratedAt = computed(() => {
  const generatedAt = report.value?.generatedAtUtc
  return generatedAt ? new Date(generatedAt).toLocaleString('zh-CN') : ''
})

const scopeText =
  '按本地来源订单计数；件数为当前商品行数量合计，包含禁用/追加行。不判断付款或另行扣退款；同步未返回的旧记录可能仍保留。'

const orderedShopCount = computed(() => report.value?.groups.length ?? 0)
const averageItemsText = computed(() => {
  if (!report.value) return '—'
  const average = averageItemsPerOrder(report.value.summary)
  return average === null ? '—' : formatAverageItems(average)
})

const pageCount = computed(() => Math.max(report.value?.pageCount ?? 1, 1))
const paginationDisabled = computed(() => status.value === 'loading')

function onShopToggle(value: boolean | Array<string | number>): void {
  selectedShopKeys.value = Array.isArray(value) ? value.map(String) : []
  applyFilters()
}

function onPlatformChange(value: string | number): void {
  platform.value = String(value)
  applyFilters()
}
</script>

<template>
  <section
    id="page-order-snapshot-report"
    class="flex h-full min-h-0 w-full flex-col overflow-hidden bg-page"
    aria-labelledby="order-snapshot-report-title"
  >
    <h1 id="order-snapshot-report-title" class="sr-only">商品量报表</h1>

    <div
      data-testid="report-searchbar"
      class="relative z-[5] flex h-52 min-w-0 flex-none flex-nowrap items-center gap-6 overflow-visible bg-white px-20 shadow-header [@media(max-width:768px)]:h-auto [@media(max-width:768px)]:min-h-52 [@media(max-width:768px)]:flex-wrap [@media(max-width:768px)]:py-8"
    >
      <span data-testid="report-platform-filter" class="inline-flex w-160 flex-none">
        <AppSelect
          :model-value="platform"
          :options="platformOptions"
          size="sm"
          focus-ring
          aria-label="平台筛选"
          class="w-full"
          @update:model-value="onPlatformChange"
        />
      </span>

      <details class="group relative min-w-180 flex-none text-12 text-ink">
        <summary
          aria-label="店铺筛选"
          class="flex h-32 w-180 list-none items-center justify-between gap-8 rounded-4 border border-input bg-white px-10 text-12/30 text-ink hover:border-input-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary pointer-coarse:min-h-44 [&::-webkit-details-marker]:hidden"
        >
          <span>{{ selectedShopLabel }}</span>
          <AppIcon
            name="i-chev-down"
            :size="10"
            class="ml-auto text-caret transition-transform duration-150 group-open:rotate-180"
          />
        </summary>
        <div
          class="absolute left-0 top-34 z-20 flex max-h-280 w-[min(340px,calc(100vw_-_48px))] flex-col gap-2 overflow-auto rounded-6 border border-line bg-white p-8 shadow-menu-lg"
        >
          <label
            v-for="shop in availableShops"
            :key="shop.key"
            class="flex min-h-28 cursor-pointer items-center gap-8 rounded-4 px-6 py-2 text-body hover:bg-primary-faint pointer-coarse:min-h-44"
          >
            <AppCheckbox
              :model-value="selectedShopKeys"
              :value="shop.key"
              :disabled="!selectedShopKeys.includes(shop.key) && selectedShopKeys.length >= 100"
              class="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
              @update:model-value="onShopToggle"
            />
            <span>{{ platformLabel(shop.platform) }} · {{ shopName(shop) }}</span>
          </label>
          <p v-if="availableShops.length === 0" class="m-0 p-6 text-muted">暂无可选店铺</p>
        </div>
      </details>

      <button
        type="button"
        :disabled="status === 'loading'"
        class="inline-flex h-32 min-h-30 flex-none items-center justify-center gap-5 rounded-4 border border-transparent bg-soft-bg px-10 text-12/30 whitespace-nowrap text-ink hover:bg-soft-bg-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary disabled:cursor-default pointer-coarse:min-h-44"
        @click="clearFilters"
      >
        清除筛选
      </button>

      <span
        class="ml-auto min-w-0 overflow-hidden text-12/32 text-muted text-ellipsis whitespace-nowrap"
      >
        <template v-if="report"
          >查询时间：<time :datetime="report.generatedAtUtc">{{ localGeneratedAt }}</time></template
        >
        <template v-else>查询本地来源订单的当前商品行快照</template>
      </span>

      <!-- 测试钩子用 contents：状态标签与刷新按钮仍作为筛选条的直接 flex 项参与换行。 -->
      <span data-testid="report-toolbar" class="contents">
        <AppTag v-if="status === 'loading'" tone="gray" role="status">正在加载商品量报表…</AppTag>
        <AppTag v-else-if="status === 'success'" tone="green" role="status">报表已更新</AppTag>
        <AppIconButton
          label="刷新"
          :disabled="status === 'loading'"
          class="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary pointer-coarse:min-h-44"
          @click="load(currentPage, pageSize)"
        >
          <AppIcon name="i-refresh" />
        </AppIconButton>
      </span>
    </div>

    <div
      data-testid="report-scroll"
      class="@container min-h-0 flex-1 overflow-x-hidden overflow-y-auto bg-page"
    >
      <div
        class="grid grid-cols-12 items-stretch gap-12 p-16"
        :aria-busy="status === 'loading' ? 'true' : undefined"
      >
        <div
          v-if="status === 'error'"
          class="col-span-12 flex min-w-0 items-center gap-8 rounded-4 border border-tag-red-border bg-danger-faint px-12 py-10 text-12/18 text-ink"
          role="alert"
        >
          <AppIcon name="i-warning" :size="16" class="mt-1 shrink-0 text-danger" />
          <div class="min-w-0 flex-1">
            <strong class="font-semibold text-ink [overflow-wrap:anywhere]">{{
              errorMessage
            }}</strong>
            <p class="mt-2 text-muted [overflow-wrap:anywhere]">请检查连接后重试。</p>
          </div>
          <AppButton
            aria-label="重试"
            class="ml-auto shrink-0 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
            @click="load(currentPage, pageSize)"
          >
            重试
          </AppButton>
        </div>

        <AppCard data-testid="report-overview" class="col-span-12 pb-16" aria-label="快照概览">
          <div class="flex h-46 flex-none items-start justify-between gap-12 px-16 pt-12">
            <div class="flex min-w-0 items-center gap-8">
              <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
                快照概览
              </div>
            </div>
          </div>
          <div
            class="grid grid-cols-4 gap-x-16 px-16 pt-6 @max-[480px]:grid-cols-2 @max-[480px]:gap-y-12"
          >
            <div class="min-w-0 pr-12">
              <div class="overflow-hidden text-12/16 text-muted text-ellipsis whitespace-nowrap">
                本地订单数
              </div>
              <strong
                class="mt-2 block text-20/26 font-medium tabular-nums text-ink"
                aria-label="订单总数"
                >{{ report ? formatQuantity(report.summary.orderCount) : '—' }}</strong
              >
            </div>
            <div
              class="min-w-0 border-l border-toolbar pr-12 pl-16 @max-[480px]:border-l-0 @max-[480px]:pl-0"
            >
              <div class="overflow-hidden text-12/16 text-muted text-ellipsis whitespace-nowrap">
                商品件数
              </div>
              <strong
                class="mt-2 block text-20/26 font-medium tabular-nums text-ink"
                aria-label="商品件数"
                >{{ report ? formatQuantity(report.summary.itemQuantity) : '—' }}</strong
              >
            </div>
            <div
              class="min-w-0 border-l border-toolbar pr-12 pl-16 @max-[480px]:border-l-0 @max-[480px]:pl-0"
            >
              <div class="overflow-hidden text-12/16 text-muted text-ellipsis whitespace-nowrap">
                有订单店铺数
              </div>
              <strong
                class="mt-2 block text-20/26 font-medium tabular-nums text-ink"
                aria-label="有订单店铺数"
                >{{ report ? formatQuantity(orderedShopCount) : '—' }}</strong
              >
            </div>
            <div
              class="min-w-0 border-l border-toolbar pr-12 pl-16 @max-[480px]:border-l-0 @max-[480px]:pl-0"
            >
              <div class="overflow-hidden text-12/16 text-muted text-ellipsis whitespace-nowrap">
                平均每单件数
              </div>
              <strong
                class="mt-2 block text-20/26 font-medium tabular-nums text-ink"
                aria-label="平均每单件数"
                >{{ report ? averageItemsText : '—' }}</strong
              >
            </div>
          </div>
        </AppCard>

        <OrderSnapshotShopRanking
          data-testid="report-shop-ranking"
          class="col-span-8 @max-[640px]:col-span-12"
          :groups="report?.groups ?? []"
          :loading="status === 'loading'"
        />
        <OrderSnapshotPlatformDistribution
          data-testid="report-platform-distribution"
          class="col-span-4 @max-[640px]:col-span-12"
          :groups="report?.groups ?? []"
          :total-orders="report?.summary.orderCount ?? 0"
          :loading="status === 'loading'"
        />

        <AppCard data-testid="report-groups-panel" class="col-span-12">
          <header class="flex h-46 flex-none items-start justify-between gap-12 px-16 pt-12">
            <div class="flex min-w-0 items-center gap-8">
              <h2
                id="report-groups-title"
                class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink"
              >
                分店汇总
              </h2>
            </div>
            <p
              class="m-0 min-w-0 overflow-hidden text-12/22 text-muted text-ellipsis whitespace-nowrap"
            >
              统计当前筛选下的全部本地来源订单
            </p>
          </header>
          <div data-testid="report-groups-area" class="relative min-h-96 overflow-hidden bg-white">
            <div
              role="region"
              tabindex="0"
              aria-label="分店汇总表格"
              :class="reportTableScrollClass"
              class="max-h-[min(240px,32dvh)]"
            >
              <table
                data-testid="report-groups-table"
                class="w-full min-w-full table-fixed border-separate border-spacing-0 bg-white @max-[640px]:min-w-480"
              >
                <caption class="sr-only">
                  分店订单与商品件数汇总
                </caption>
                <colgroup>
                  <col style="width: 16%" />
                  <col style="width: 28%" />
                  <col style="width: 28%" />
                  <col style="width: 28%" />
                </colgroup>
                <thead>
                  <tr>
                    <AppTh>平台</AppTh>
                    <AppTh>店铺</AppTh>
                    <AppTh numeric>本地订单数</AppTh>
                    <AppTh numeric>商品件数</AppTh>
                  </tr>
                </thead>
                <tbody v-if="report?.groups.length">
                  <tr v-for="group in report.groups" :key="group.shop.key" class="group">
                    <td :class="groupCellClass">{{ platformLabel(group.shop.platform) }}</td>
                    <td :class="groupCellClass">{{ shopName(group.shop) }}</td>
                    <td :class="groupNumericCellClass">{{ formatQuantity(group.orderCount) }}</td>
                    <td :class="groupNumericCellClass">
                      {{ formatQuantity(group.itemQuantity) }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <AppEmptyState v-if="!report?.groups.length" data-testid="report-groups-empty">
              <span>{{ status === 'loading' ? '正在加载…' : '暂无分店汇总' }}</span>
            </AppEmptyState>
          </div>
        </AppCard>

        <OrderSnapshotOrdersPanel
          :orders="report?.orders ?? []"
          :status="status"
          :expanded-ids="expandedOrderIds"
          :shop-name="shopName"
          :page="currentPage"
          :page-count="pageCount"
          :page-size="pageSize"
          :pagination-disabled="paginationDisabled"
          @toggle="toggleOrder"
          @update:page="changePage"
          @update:page-size="(size: number) => load(1, size)"
        />

        <AppCard class="col-span-12 px-16 py-12">
          <p
            data-testid="report-scope"
            class="m-0 flex min-w-0 items-start gap-6 border-0 bg-transparent p-0 text-12/18 text-body"
            role="note"
            aria-label="报表统计口径"
            :title="`统计口径：${scopeText}`"
          >
            <AppIcon name="i-info" :size="14" class="mt-2 shrink-0 text-muted" />
            <span class="min-w-0"
              ><strong class="mr-2 font-semibold text-ink">统计口径</strong> {{ scopeText }}</span
            >
          </p>
        </AppCard>
      </div>
    </div>
  </section>
</template>
