<script setup lang="ts">
import { platformLabel, type ReportOrder, type ReportShop } from '../../reports/orderSnapshot'
import { formatSnapshotQuantity as formatQuantity } from '../../reports/orderSnapshotPresentation'
import type { ReportStatus } from '../../reports/useOrderSnapshotReport'
import AppCard from '../../ui/AppCard.vue'
import AppEmptyState from '../../ui/AppEmptyState.vue'
import AppPagination from '../../ui/AppPagination.vue'
import AppTag from '../../ui/AppTag.vue'
import AppTd from '../../ui/AppTd.vue'
import AppTh from '../../ui/AppTh.vue'
import { reportTableScrollClass } from './reportTable'

/**
 * 订单明细：一行对应一条本地来源订单，可展开查看商品行和平台订单参考号，下方是分页。
 * 展开状态、翻页与页大小都由页面持有，这里只负责呈现并把操作通过事件交回。
 */
defineProps<{
  orders: ReportOrder[]
  status: ReportStatus
  expandedIds: Set<string>
  shopName: (shop: ReportShop) => string
  page: number
  pageCount: number
  pageSize: number
  paginationDisabled: boolean
}>()

const emit = defineEmits<{
  toggle: [identity: string]
  'update:page': [page: number]
  'update:pageSize': [size: number]
}>()

function orderIdentity(order: ReportOrder): string {
  return JSON.stringify([order.shop.key, order.id])
}
</script>

<template>
  <AppCard data-testid="report-orders-panel" class="col-span-12">
    <header class="flex h-46 flex-none items-start justify-between gap-12 px-16 pt-12">
      <div class="flex min-w-0 items-center gap-8">
        <h2
          id="report-orders-title"
          class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink"
        >
          订单明细
        </h2>
      </div>
      <p class="m-0 min-w-0 overflow-hidden text-12/22 text-muted text-ellipsis whitespace-nowrap">
        一行对应一条本地来源订单；展开查看当前商品行和平台订单参考号。
      </p>
    </header>
    <div data-testid="report-orders-area" class="relative min-h-96 overflow-hidden bg-white">
      <div
        role="region"
        tabindex="0"
        aria-label="订单明细表格"
        :class="reportTableScrollClass"
        class="max-h-[min(520px,62dvh)]"
      >
        <table
          data-testid="report-orders-table"
          class="w-full min-w-full table-fixed border-separate border-spacing-0 bg-white @max-[640px]:min-w-720"
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
          <tbody v-if="orders.length">
            <template v-for="order in orders" :key="orderIdentity(order)">
              <tr class="group">
                <AppTd class="group-hover:bg-row-hover">{{
                  platformLabel(order.shop.platform)
                }}</AppTd>
                <AppTd class="group-hover:bg-row-hover">{{ shopName(order.shop) }}</AppTd>
                <AppTd class="group-hover:bg-row-hover">{{ order.number }}</AppTd>
                <AppTd class="group-hover:bg-row-hover">
                  <span v-if="order.platformOrders.length">
                    {{
                      order.platformOrders.map((platformOrder) => platformOrder.number).join('、')
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
                    :aria-label="`${expandedIds.has(orderIdentity(order)) ? '收起' : '展开'}订单 ${order.number} 商品明细`"
                    :aria-expanded="expandedIds.has(orderIdentity(order))"
                    @click="emit('toggle', orderIdentity(order))"
                  >
                    {{ expandedIds.has(orderIdentity(order)) ? '收起' : '展开' }}
                  </button>
                </AppTd>
              </tr>
              <tr v-if="expandedIds.has(orderIdentity(order))" data-testid="report-order-details">
                <AppTd :colspan="8" surface="page-soft">
                  <div class="grid grid-cols-2 gap-x-22 gap-y-14 @max-[480px]:grid-cols-1">
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
                    <p class="col-span-full font-semibold text-ink">
                      本地订单件数 {{ formatQuantity(order.itemQuantity) }}
                    </p>
                  </div>
                </AppTd>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
      <AppEmptyState v-if="!orders.length" data-testid="report-orders-empty">
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
    <AppPagination
      as="footer"
      data-testid="report-pagination"
      class="pointer-coarse:h-auto pointer-coarse:[&_button]:min-h-44 pointer-coarse:[&_input]:min-h-44 pointer-coarse:[&_select]:min-h-44 pointer-coarse:min-h-44 pointer-coarse:flex-wrap pointer-coarse:py-4 @max-[640px]:h-auto @max-[640px]:min-h-30 @max-[640px]:flex-wrap @max-[640px]:py-4"
      :page="page"
      :page-count="pageCount"
      :page-size="pageSize"
      :total-text="`共 ${pageCount} 页`"
      :disabled="paginationDisabled"
      :padding-x="8"
      focus-ring
      @update:page="emit('update:page', $event)"
      @update:page-size="emit('update:pageSize', $event)"
    />
  </AppCard>
</template>
