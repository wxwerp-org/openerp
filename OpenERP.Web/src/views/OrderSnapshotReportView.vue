<script setup lang="ts">
import { computed, provide, ref, watch } from 'vue'
import OrderSnapshotPlatformDistribution from '../components/reports/OrderSnapshotPlatformDistribution.vue'
import OrderSnapshotShopRanking from '../components/reports/OrderSnapshotShopRanking.vue'
import { platformLabel, type ReportOrder } from '../reports/orderSnapshot'
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
import AppPager from '../ui/AppPager.vue'
import AppTag from '../ui/AppTag.vue'
import AppTd from '../ui/AppTd.vue'
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

const pageSizeOptions = [20, 50, 100]
const jumpPage = ref(String(currentPage.value))

/** 旧样式里的控件几何按实测值显式书写，避免组合类互相覆盖。 */
const pageSizeClass =
  'inline-flex h-28 min-w-100 shrink-0 cursor-pointer items-center justify-between gap-10 rounded-4 border border-input bg-white px-9 text-ink focus-within:border-primary'
const pageSizeClassDisabled =
  'inline-flex h-28 min-w-100 shrink-0 cursor-default items-center justify-between gap-10 rounded-4 border border-input bg-btn-disabled-bg px-9 text-muted'
const jumpInputClass =
  'h-28 w-46 rounded-4 border border-input bg-white px-4 py-0 text-center text-12/18 text-ink outline-none focus:border-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary disabled:cursor-default disabled:bg-btn-disabled-bg disabled:text-muted disabled:focus:border-input'

/** 分店汇总表沿用旧 `.grid-table td` 的非换行密度。 */
const groupCellClass =
  'h-48 overflow-hidden border-r border-b border-r-line-soft border-b-line-soft bg-white px-8 text-12/18 text-body text-ellipsis whitespace-nowrap group-hover:bg-row-hover last:border-r-0'
const groupNumericCellClass = `${groupCellClass} text-right tabular-nums`

/** 表格滚动区沿用列表页表格的 10px 滚动条几何（旧 `.table-scroll` 覆写）。 */
const tableScrollClass =
  'overflow-auto bg-white focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary [&::-webkit-scrollbar]:h-10 [&::-webkit-scrollbar]:w-10 [&::-webkit-scrollbar-track]:bg-scroll-track [&::-webkit-scrollbar-thumb]:rounded-8 [&::-webkit-scrollbar-thumb]:border-2 [&::-webkit-scrollbar-thumb]:border-scroll-track [&::-webkit-scrollbar-thumb]:bg-scroll-thumb'

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

watch(currentPage, (page) => {
  jumpPage.value = String(page)
})

function orderIdentity(order: ReportOrder): string {
  return JSON.stringify([order.shop.key, order.id])
}

function onShopToggle(value: boolean | Array<string | number>): void {
  selectedShopKeys.value = Array.isArray(value) ? value.map(String) : []
  updateFilters()
}

function changePageSize(event: Event): void {
  const size = Number((event.target as HTMLSelectElement).value)
  if (pageSizeOptions.includes(size)) void load(1, size)
}

function submitJumpPage(): void {
  // Chromium 在加载期间禁用聚焦的输入框会触发 blur；此时 pageCount 已回退为 1，
  // 若继续提交会把刚发出的跳页请求收敛回第 1 页，因此加载中忽略失焦提交。
  if (paginationDisabled.value) {
    jumpPage.value = String(currentPage.value)
    return
  }

  const requested = Number.parseInt(jumpPage.value, 10)
  if (!Number.isInteger(requested)) {
    jumpPage.value = String(currentPage.value)
    return
  }

  const target = Math.min(Math.max(requested, 1), pageCount.value)
  jumpPage.value = String(target)
  if (target !== currentPage.value) changePage(target)
}
</script>

<template>
  <section
    id="page-order-snapshot-report"
    class="page-order-snapshot-report flex h-full min-h-0 w-full flex-col overflow-hidden bg-page"
    aria-labelledby="order-snapshot-report-title"
  >
    <h1 id="order-snapshot-report-title" class="sr-only">商品量报表</h1>

    <div
      data-testid="report-searchbar"
      class="report-searchbar relative z-[5] flex h-52 min-w-0 flex-none flex-nowrap items-center gap-6 overflow-visible bg-white px-20 shadow-header [@media(max-width:768px)]:h-auto [@media(max-width:768px)]:min-h-52 [@media(max-width:768px)]:flex-wrap [@media(max-width:768px)]:py-8"
    >
      <span
        data-testid="report-platform-filter"
        class="report-select inline-flex h-30 w-160 flex-none items-center rounded-4 border border-input bg-white focus-within:border-primary"
      >
        <select
          v-model="platform"
          aria-label="平台筛选"
          class="h-28 min-w-0 flex-1 cursor-pointer appearance-none border-0 border-current bg-transparent px-10 text-12 text-ink outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary pointer-coarse:min-h-44"
          @change="updateFilters"
        >
          <option value="">全部平台</option>
          <option v-for="item in availablePlatforms" :key="item" :value="item">
            {{ platformLabel(item) }}
          </option>
        </select>
        <AppIcon name="i-chev-down" />
      </span>

      <details class="report-shop-filter group relative min-w-180 flex-none text-12 text-ink">
        <summary
          aria-label="店铺筛选"
          class="report-shop-trigger flex h-32 w-180 list-none items-center justify-between gap-8 rounded-4 border border-input bg-white px-10 text-12/30 text-ink hover:border-input-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary pointer-coarse:min-h-44 [&::-webkit-details-marker]:hidden"
        >
          <span>{{ selectedShopLabel }}</span>
          <AppIcon
            name="i-chev-down"
            :size="10"
            class="ml-auto text-caret transition-transform duration-150 group-open:rotate-180"
          />
        </summary>
        <div
          class="report-shop-options absolute left-0 top-34 z-20 flex max-h-280 w-[min(340px,calc(100vw_-_48px))] flex-col gap-2 overflow-auto rounded-6 border border-line bg-white p-8 shadow-menu-lg"
        >
          <label
            v-for="shop in availableShops"
            :key="shop.key"
            class="report-shop-option flex min-h-28 cursor-pointer items-center gap-8 rounded-4 px-6 py-2 text-body hover:bg-primary-faint pointer-coarse:min-h-44"
          >
            <AppCheckbox
              :model-value="selectedShopKeys"
              :value="shop.key"
              :disabled="!selectedShopKeys.includes(shop.key) && selectedShopKeys.length >= 100"
              class="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
              @update:model-value="onShopToggle"
            />
            <span>{{ platformLabel(shop.platform) }} · {{ shop.label }}</span>
          </label>
          <p v-if="availableShops.length === 0" class="report-shop-empty m-0 p-6 text-muted">
            暂无可选店铺
          </p>
        </div>
      </details>

      <button
        type="button"
        :disabled="status === 'loading'"
        class="inline-flex h-32 min-h-30 flex-none items-center justify-center gap-5 rounded-4 border border-transparent bg-soft-bg px-10 text-12/30 whitespace-nowrap text-ink hover:bg-soft-bg-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary disabled:cursor-default disabled:text-placeholder pointer-coarse:min-h-44"
        @click="resetFilters"
      >
        清除筛选
      </button>

      <span
        class="report-generated-at ml-auto min-w-0 overflow-hidden text-12/32 text-muted text-ellipsis whitespace-nowrap"
      >
        <template v-if="report"
          >查询时间：<time :datetime="report.generatedAtUtc">{{ localGeneratedAt }}</time></template
        >
        <template v-else>查询本地来源订单的当前商品行快照</template>
      </span>

      <span data-testid="report-toolbar" class="flex shrink-0 items-center gap-6">
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
        class="report-analysis-grid grid grid-cols-12 items-stretch gap-12 p-16"
        :aria-busy="status === 'loading' ? 'true' : undefined"
      >
        <div
          v-if="status === 'error'"
          class="report-error col-span-12 flex min-w-0 items-center gap-8 rounded-4 border border-tag-red-border bg-tag-red-bg px-12 py-10 text-12/18 text-ink"
          role="alert"
        >
          <AppIcon name="i-warning" :size="16" class="mt-1 shrink-0 text-danger" />
          <div class="report-error-body min-w-0 flex-1">
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

        <AppCard
          data-testid="report-overview"
          class="report-overview-card col-span-12 pb-16"
          aria-label="快照概览"
        >
          <div
            class="report-overview-head flex h-46 flex-none items-start justify-between gap-12 px-16 pt-12"
          >
            <div class="flex min-w-0 items-center gap-8">
              <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
                快照概览
              </div>
            </div>
          </div>
          <div
            class="report-overview-body grid grid-cols-4 gap-x-16 px-16 pt-6 @max-[480px]:grid-cols-2 @max-[480px]:gap-y-12"
          >
            <div class="report-stat min-w-0 pr-12">
              <div
                class="report-stat-label overflow-hidden text-12/16 text-muted text-ellipsis whitespace-nowrap"
              >
                本地订单数
              </div>
              <strong
                class="report-stat-value mt-2 block text-20/26 font-medium tabular-nums text-ink"
                aria-label="订单总数"
                >{{ report ? formatQuantity(report.summary.orderCount) : '—' }}</strong
              >
            </div>
            <div
              class="report-stat min-w-0 border-l border-toolbar pr-12 pl-16 @max-[480px]:border-l-0 @max-[480px]:pl-0"
            >
              <div
                class="report-stat-label overflow-hidden text-12/16 text-muted text-ellipsis whitespace-nowrap"
              >
                商品件数
              </div>
              <strong
                class="report-stat-value mt-2 block text-20/26 font-medium tabular-nums text-ink"
                aria-label="商品件数"
                >{{ report ? formatQuantity(report.summary.itemQuantity) : '—' }}</strong
              >
            </div>
            <div
              class="report-stat min-w-0 border-l border-toolbar pr-12 pl-16 @max-[480px]:border-l-0 @max-[480px]:pl-0"
            >
              <div
                class="report-stat-label overflow-hidden text-12/16 text-muted text-ellipsis whitespace-nowrap"
              >
                有订单店铺数
              </div>
              <strong
                class="report-stat-value mt-2 block text-20/26 font-medium tabular-nums text-ink"
                aria-label="有订单店铺数"
                >{{ report ? formatQuantity(orderedShopCount) : '—' }}</strong
              >
            </div>
            <div
              class="report-stat min-w-0 border-l border-toolbar pr-12 pl-16 @max-[480px]:border-l-0 @max-[480px]:pl-0"
            >
              <div
                class="report-stat-label overflow-hidden text-12/16 text-muted text-ellipsis whitespace-nowrap"
              >
                平均每单件数
              </div>
              <strong
                class="report-stat-value mt-2 block text-20/26 font-medium tabular-nums text-ink"
                aria-label="平均每单件数"
                >{{ report ? averageItemsText : '—' }}</strong
              >
            </div>
          </div>
        </AppCard>

        <OrderSnapshotShopRanking
          data-testid="report-shop-ranking"
          class="report-rank-card col-span-8 @max-[640px]:col-span-12"
          :groups="report?.groups ?? []"
          :loading="status === 'loading'"
        />
        <OrderSnapshotPlatformDistribution
          data-testid="report-platform-distribution"
          class="report-dist-card col-span-4 @max-[640px]:col-span-12"
          :groups="report?.groups ?? []"
          :total-orders="report?.summary.orderCount ?? 0"
          :loading="status === 'loading'"
        />

        <AppCard
          data-testid="report-groups-panel"
          class="report-panel report-groups-panel col-span-12"
        >
          <header
            class="report-panel-head flex h-46 flex-none items-start justify-between gap-12 px-16 pt-12"
          >
            <div class="flex min-w-0 items-center gap-8">
              <h2
                id="report-groups-title"
                class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink"
              >
                分店汇总
              </h2>
            </div>
            <p
              class="report-panel-desc m-0 min-w-0 overflow-hidden text-12/22 text-muted text-ellipsis whitespace-nowrap"
            >
              统计当前筛选下的全部本地来源订单
            </p>
          </header>
          <div data-testid="report-groups-area" class="relative min-h-96 overflow-hidden bg-white">
            <div
              role="region"
              tabindex="0"
              aria-label="分店汇总表格"
              :class="tableScrollClass"
              class="max-h-[min(240px,32dvh)]"
            >
              <table
                data-testid="report-groups-table"
                class="report-groups-table w-full min-w-full table-fixed border-separate border-spacing-0 bg-white @max-[640px]:min-w-[480px]"
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
                    <td :class="groupCellClass">{{ group.shop.label }}</td>
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

        <AppCard
          data-testid="report-orders-panel"
          class="report-panel report-orders-panel col-span-12"
        >
          <header
            class="report-panel-head flex h-46 flex-none items-start justify-between gap-12 px-16 pt-12"
          >
            <div class="flex min-w-0 items-center gap-8">
              <h2
                id="report-orders-title"
                class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink"
              >
                订单明细
              </h2>
            </div>
            <p
              class="report-panel-desc m-0 min-w-0 overflow-hidden text-12/22 text-muted text-ellipsis whitespace-nowrap"
            >
              一行对应一条本地来源订单；展开查看当前商品行和平台订单参考号。
            </p>
          </header>
          <div data-testid="report-orders-area" class="relative min-h-96 overflow-hidden bg-white">
            <div
              role="region"
              tabindex="0"
              aria-label="订单明细表格"
              :class="tableScrollClass"
              class="max-h-[min(520px,62dvh)]"
            >
              <table
                data-testid="report-orders-table"
                class="report-orders-table w-full min-w-full table-fixed border-separate border-spacing-0 bg-white @max-[640px]:min-w-[720px]"
              >
                <caption class="sr-only">
                  本地来源订单分页明细
                </caption>
                <colgroup>
                  <col style="width: 10%" />
                  <col style="width: 14%" />
                  <col style="width: 16%" />
                  <col style="width: 22%" />
                  <col style="width: 10%" />
                  <col style="width: 8%" />
                  <col style="width: 9%" />
                  <col style="width: 11%" />
                </colgroup>
                <thead>
                  <tr>
                    <AppTh>平台</AppTh>
                    <AppTh>店铺</AppTh>
                    <AppTh>本地订单号</AppTh>
                    <AppTh>平台订单参考号</AppTh>
                    <AppTh>本地订单状态</AppTh>
                    <AppTh numeric>商品行</AppTh>
                    <AppTh numeric>商品件数</AppTh>
                    <AppTh>明细</AppTh>
                  </tr>
                </thead>
                <tbody v-if="report?.orders.length">
                  <template v-for="order in report.orders" :key="orderIdentity(order)">
                    <tr class="group">
                      <AppTd class="group-hover:bg-row-hover">{{
                        platformLabel(order.shop.platform)
                      }}</AppTd>
                      <AppTd class="group-hover:bg-row-hover">{{ order.shop.label }}</AppTd>
                      <AppTd class="group-hover:bg-row-hover">{{ order.number }}</AppTd>
                      <AppTd class="group-hover:bg-row-hover">
                        <span v-if="order.platformOrders.length">
                          {{
                            order.platformOrders
                              .map((platformOrder) => platformOrder.number)
                              .join('、')
                          }}
                        </span>
                        <span v-else>—</span>
                      </AppTd>
                      <AppTd class="group-hover:bg-row-hover">{{ order.status || '—' }}</AppTd>
                      <AppTd numeric class="group-hover:bg-row-hover">{{
                        formatQuantity(order.items.length)
                      }}</AppTd>
                      <AppTd numeric class="group-hover:bg-row-hover">{{
                        formatQuantity(order.itemQuantity)
                      }}</AppTd>
                      <AppTd class="group-hover:bg-row-hover">
                        <button
                          class="inline-flex items-center gap-4 whitespace-nowrap text-12 text-primary hover:text-primary-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary pointer-coarse:min-h-44"
                          type="button"
                          :aria-label="`${expandedOrderIds.has(orderIdentity(order)) ? '收起' : '展开'}订单 ${order.number} 商品明细`"
                          :aria-expanded="expandedOrderIds.has(orderIdentity(order))"
                          @click="toggleOrder(orderIdentity(order))"
                        >
                          {{ expandedOrderIds.has(orderIdentity(order)) ? '收起' : '展开' }}
                        </button>
                      </AppTd>
                    </tr>
                    <tr
                      v-if="expandedOrderIds.has(orderIdentity(order))"
                      data-testid="report-order-details"
                      class="report-order-details"
                    >
                      <AppTd :colspan="8" surface="page-soft">
                        <div
                          class="report-detail-sections grid grid-cols-2 gap-x-22 gap-y-14 @max-[480px]:grid-cols-1"
                        >
                          <section>
                            <h3 class="mb-8 text-12/18 font-semibold text-ink">
                              平台订单参考号（不分配商品行）
                            </h3>
                            <ul v-if="order.platformOrders.length" class="grid gap-6">
                              <li
                                v-for="platformOrder in order.platformOrders"
                                :key="platformOrder.number"
                                class="flex flex-wrap items-center gap-x-12 gap-y-6 text-body"
                              >
                                <strong>{{ platformOrder.number }}</strong>
                                <span v-if="platformOrder.statusCode"
                                  >状态码：{{ platformOrder.statusCode }}</span
                                >
                                <span v-if="platformOrder.statusText"
                                  >状态：{{ platformOrder.statusText }}</span
                                >
                              </li>
                            </ul>
                            <p v-else class="m-0 text-muted">无平台订单参考号</p>
                          </section>
                          <section>
                            <h3 class="mb-8 text-12/18 font-semibold text-ink">当前商品行</h3>
                            <ul v-if="order.items.length" class="grid gap-6">
                              <li
                                v-for="item in order.items"
                                :key="item.lineId"
                                class="flex flex-wrap items-center gap-x-12 gap-y-6 text-body"
                              >
                                <span>{{
                                  item.skuCode || item.skuId || item.productId || '未提供商品编码'
                                }}</span>
                                <strong>{{ formatQuantity(item.quantity) }} 件</strong>
                                <AppTag
                                  :tone="
                                    item.disabled === true
                                      ? 'gray'
                                      : item.disabled === false
                                        ? 'green'
                                        : 'amber'
                                  "
                                >
                                  {{
                                    item.disabled === true
                                      ? '禁用'
                                      : item.disabled === false
                                        ? '未禁用'
                                        : '禁用状态未知'
                                  }}
                                </AppTag>
                                <AppTag
                                  :tone="
                                    item.appended === true
                                      ? 'amber'
                                      : item.appended === false
                                        ? 'gray'
                                        : 'amber'
                                  "
                                >
                                  {{
                                    item.appended === true
                                      ? '追加'
                                      : item.appended === false
                                        ? '未追加'
                                        : '追加状态未知'
                                  }}
                                </AppTag>
                              </li>
                            </ul>
                            <p v-else class="m-0 text-muted">无商品行</p>
                          </section>
                          <p class="report-order-total col-span-full font-semibold text-ink">
                            本地订单件数 {{ formatQuantity(order.itemQuantity) }}
                          </p>
                        </div>
                      </AppTd>
                    </tr>
                  </template>
                </tbody>
              </table>
            </div>
            <AppEmptyState v-if="!report?.orders.length" data-testid="report-orders-empty">
              <span>
                {{
                  status === 'loading'
                    ? '正在加载…'
                    : status === 'error'
                      ? '报表加载失败'
                      : '暂无符合条件的本地订单'
                }}
              </span>
            </AppEmptyState>
          </div>
          <AppPager
            as="footer"
            data-testid="report-pagination"
            class="report-pagination pointer-coarse:h-auto pointer-coarse:[&_button]:min-h-44 pointer-coarse:[&_input]:min-h-44 pointer-coarse:[&_select]:min-h-44 pointer-coarse:min-h-44 pointer-coarse:flex-wrap pointer-coarse:py-4 @max-[640px]:h-auto @max-[640px]:min-h-30 @max-[640px]:flex-wrap @max-[640px]:py-4"
            :page="currentPage"
            :page-count="pageCount"
            :disabled="paginationDisabled"
            :padding-x="8"
            focus-ring
            @update:page="changePage"
          >
            <template #prefix>
              <span>共 {{ pageCount }} 页</span>
            </template>
            <template #suffix>
              <label :class="paginationDisabled ? pageSizeClassDisabled : pageSizeClass">
                <select
                  :value="pageSize"
                  :disabled="paginationDisabled"
                  aria-label="每页条数"
                  class="h-26 min-w-0 cursor-[inherit] appearance-none border-0 border-current bg-transparent p-0 text-12 text-inherit outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
                  @change="changePageSize"
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
                  :disabled="paginationDisabled"
                  aria-label="前往页码"
                  inputmode="numeric"
                  :class="jumpInputClass"
                  @keyup.enter="submitJumpPage"
                  @blur="submitJumpPage"
                />页
              </span>
            </template>
          </AppPager>
        </AppCard>

        <AppCard class="report-scope-panel col-span-12 px-16 py-12">
          <p
            data-testid="report-scope"
            class="report-scope m-0 flex min-w-0 items-start gap-6 border-0 bg-transparent p-0 text-12/18 text-body"
            role="note"
            aria-label="报表统计口径"
            :title="`统计口径：${scopeText}`"
          >
            <AppIcon name="i-info" :size="14" class="mt-2 shrink-0 text-muted" />
            <span class="min-w-0"
              ><strong class="report-scope-title mr-2 font-semibold text-ink">统计口径</strong>
              {{ scopeText }}</span
            >
          </p>
        </AppCard>
      </div>
    </div>
  </section>
</template>
