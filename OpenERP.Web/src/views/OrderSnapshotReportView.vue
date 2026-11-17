<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import AppIcon from '../components/navigation/AppIcon.vue'
import { buildPageList } from '../components/controls/pager'
import { platformLabel, type ReportOrder } from '../reports/orderSnapshot'
import { useOrderSnapshotReport } from '../reports/useOrderSnapshotReport'

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

type PagerItem = {
  key: string
  label: string
  page: number | null
  current: boolean
}

const pageSizeOptions = [20, 50, 100]
const jumpPage = ref(String(currentPage.value))

const selectedShopLabel = computed(() =>
  selectedShopKeys.value.length ? `已选 ${selectedShopKeys.value.length} 家店铺` : '全部店铺',
)

const localGeneratedAt = computed(() => {
  const generatedAt = report.value?.generatedAtUtc
  return generatedAt ? new Date(generatedAt).toLocaleString('zh-CN') : ''
})

const scopeText = '按本地来源订单计数；件数为当前商品行数量合计，包含禁用/追加行。不判断付款或另行扣退款；同步未返回的旧记录可能仍保留。'

const pageCount = computed(() => Math.max(report.value?.pageCount ?? 1, 1))
const paginationDisabled = computed(() => status.value === 'loading')
const pagerItems = computed<PagerItem[]>(() =>
  buildPageList(currentPage.value, pageCount.value).map((item, index) =>
    item === 'ellipsis'
      ? { key: `ellipsis-${index}`, label: '…', page: null, current: false }
      : { key: `page-${item}`, label: String(item), page: item, current: item === currentPage.value },
  ),
)

watch(currentPage, (page) => {
  jumpPage.value = String(page)
})

function formatQuantity(quantity: number): string {
  return new Intl.NumberFormat('zh-CN').format(quantity)
}

function orderIdentity(order: ReportOrder): string {
  return JSON.stringify([order.shop.key, order.id])
}

function goToPage(item: PagerItem): void {
  if (item.page === null) return
  changePage(item.page)
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
  <section class="page page-order-snapshot-report active" aria-labelledby="order-snapshot-report-title">
    <h1 id="order-snapshot-report-title" class="visually-hidden">商品量报表</h1>

    <div class="list-searchbar report-searchbar">
      <span class="field-box report-select">
        <select v-model="platform" aria-label="平台筛选" @change="updateFilters">
          <option value="">全部平台</option>
          <option v-for="item in availablePlatforms" :key="item" :value="item">{{ platformLabel(item) }}</option>
        </select>
        <AppIcon name="i-chev-down" />
      </span>

      <details class="report-shop-filter">
        <summary class="control report-shop-trigger" aria-label="店铺筛选">
          <span>{{ selectedShopLabel }}</span>
          <AppIcon name="i-chev-down" icon-class="caret" />
        </summary>
        <div class="report-shop-options">
          <label v-for="shop in availableShops" :key="shop.key" class="report-shop-option">
            <input
              v-model="selectedShopKeys"
              class="checkbox"
              type="checkbox"
              :value="shop.key"
              :disabled="!selectedShopKeys.includes(shop.key) && selectedShopKeys.length >= 100"
              @change="updateFilters"
            >
            <span>{{ platformLabel(shop.platform) }} · {{ shop.label }}</span>
          </label>
          <p v-if="availableShops.length === 0" class="report-shop-empty">暂无可选店铺</p>
        </div>
      </details>

      <button class="btn soft" type="button" :disabled="status === 'loading'" @click="resetFilters">清除筛选</button>

      <span class="report-generated-at">
        <template v-if="report">查询时间：<time :datetime="report.generatedAtUtc">{{ localGeneratedAt }}</time></template>
        <template v-else>查询本地来源订单的当前商品行快照</template>
      </span>
    </div>

    <div class="list-toolbar report-toolbar">
      <div class="report-toolbar-left">
        <div class="report-metric">
          <span class="report-metric-label">本地订单数</span>
          <strong class="report-metric-value" aria-label="订单总数">{{ report ? formatQuantity(report.summary.orderCount) : '—' }}</strong>
        </div>
        <div class="report-metric">
          <span class="report-metric-label">商品件数</span>
          <strong class="report-metric-value" aria-label="商品件数">{{ report ? formatQuantity(report.summary.itemQuantity) : '—' }}</strong>
        </div>
        <p class="notice report-scope" role="note" aria-label="报表统计口径" :title="`统计口径：${scopeText}`">
          <AppIcon name="i-info" icon-class="ic-12" />
          <span><strong class="report-scope-title">统计口径</strong> {{ scopeText }}</span>
        </p>
      </div>
      <div class="report-toolbar-right">
        <span v-if="status === 'loading'" class="tag gray" role="status">正在加载商品量报表…</span>
        <span v-else-if="status === 'success'" class="tag green" role="status">报表已更新</span>
        <button
          class="btn-icon"
          type="button"
          title="刷新"
          aria-label="刷新"
          :disabled="status === 'loading'"
          @click="load(currentPage, pageSize)"
        >
          <AppIcon name="i-refresh" />
        </button>
      </div>
    </div>

    <div class="report-tables">
      <div v-if="status === 'error'" class="notice red report-error" role="alert">
        <AppIcon name="i-warning" icon-class="ic-16" />
        <div class="report-error-body">
          <strong class="notice-title">{{ errorMessage }}</strong>
          <p class="notice-desc">请检查连接后重试。</p>
        </div>
        <button class="btn" type="button" aria-label="重试" @click="load(currentPage, pageSize)">重试</button>
      </div>

      <div class="table-box report-panel report-groups-panel">
        <header class="report-panel-head">
          <h2 id="report-groups-title">分店汇总</h2>
          <p>统计当前筛选下的全部本地来源订单</p>
        </header>
        <div class="table-area">
          <div class="table-scroll is-y" role="region" tabindex="0" aria-label="分店汇总表格">
            <table class="grid-table report-groups-table">
              <caption class="visually-hidden">分店订单与商品件数汇总</caption>
              <colgroup>
                <col style="width:16%">
                <col style="width:28%">
                <col style="width:28%">
                <col style="width:28%">
              </colgroup>
              <thead>
                <tr>
                  <th scope="col">平台</th>
                  <th scope="col">店铺</th>
                  <th scope="col" class="num-cell">本地订单数</th>
                  <th scope="col" class="num-cell">商品件数</th>
                </tr>
              </thead>
              <tbody v-if="report?.groups.length">
                <tr v-for="group in report.groups" :key="group.shop.key">
                  <td>{{ platformLabel(group.shop.platform) }}</td>
                  <td>{{ group.shop.label }}</td>
                  <td class="num-cell">{{ formatQuantity(group.orderCount) }}</td>
                  <td class="num-cell">{{ formatQuantity(group.itemQuantity) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div v-if="!report?.groups.length" class="empty-overlay">
            <span>{{ status === 'loading' ? '正在加载…' : '暂无分店汇总' }}</span>
          </div>
        </div>
      </div>

      <div class="table-box report-panel report-orders-panel">
        <header class="report-panel-head">
          <h2 id="report-orders-title">订单明细</h2>
          <p>一行对应一条本地来源订单；展开查看当前商品行和平台订单参考号。</p>
        </header>
        <div class="table-area">
          <div class="table-scroll is-y" role="region" tabindex="0" aria-label="订单明细表格">
            <table class="grid-table is-wrap report-orders-table">
              <caption class="visually-hidden">本地来源订单分页明细</caption>
              <colgroup>
                <col style="width:10%">
                <col style="width:14%">
                <col style="width:16%">
                <col style="width:22%">
                <col style="width:10%">
                <col style="width:8%">
                <col style="width:9%">
                <col style="width:11%">
              </colgroup>
              <thead>
                <tr>
                  <th scope="col">平台</th>
                  <th scope="col">店铺</th>
                  <th scope="col">本地订单号</th>
                  <th scope="col">平台订单参考号</th>
                  <th scope="col">本地订单状态</th>
                  <th scope="col" class="num-cell">商品行</th>
                  <th scope="col" class="num-cell">商品件数</th>
                  <th scope="col">明细</th>
                </tr>
              </thead>
              <tbody v-if="report?.orders.length">
                <template v-for="order in report.orders" :key="orderIdentity(order)">
                  <tr>
                    <td>{{ platformLabel(order.shop.platform) }}</td>
                    <td>{{ order.shop.label }}</td>
                    <td>{{ order.number }}</td>
                    <td>
                      <span v-if="order.platformOrders.length">
                        {{ order.platformOrders.map((platformOrder) => platformOrder.number).join('、') }}
                      </span>
                      <span v-else>—</span>
                    </td>
                    <td>{{ order.status || '—' }}</td>
                    <td class="num-cell">{{ formatQuantity(order.items.length) }}</td>
                    <td class="num-cell">{{ formatQuantity(order.itemQuantity) }}</td>
                    <td>
                      <button
                        class="link-btn"
                        type="button"
                        :aria-label="`${expandedOrderIds.has(orderIdentity(order)) ? '收起' : '展开'}订单 ${order.number} 商品明细`"
                        :aria-expanded="expandedOrderIds.has(orderIdentity(order))"
                        @click="toggleOrder(orderIdentity(order))"
                      >
                        {{ expandedOrderIds.has(orderIdentity(order)) ? '收起' : '展开' }}
                      </button>
                    </td>
                  </tr>
                  <tr v-if="expandedOrderIds.has(orderIdentity(order))" class="report-order-details">
                    <td colspan="8">
                      <div class="report-detail-sections">
                        <section>
                          <h3>平台订单参考号（不分配商品行）</h3>
                          <ul v-if="order.platformOrders.length">
                            <li v-for="platformOrder in order.platformOrders" :key="platformOrder.number">
                              <strong>{{ platformOrder.number }}</strong>
                              <span v-if="platformOrder.statusCode">状态码：{{ platformOrder.statusCode }}</span>
                              <span v-if="platformOrder.statusText">状态：{{ platformOrder.statusText }}</span>
                            </li>
                          </ul>
                          <p v-else>无平台订单参考号</p>
                        </section>
                        <section>
                          <h3>当前商品行</h3>
                          <ul v-if="order.items.length">
                            <li v-for="item in order.items" :key="item.lineId">
                              <span>{{ item.skuCode || item.skuId || item.productId || '未提供商品编码' }}</span>
                              <strong>{{ formatQuantity(item.quantity) }} 件</strong>
                              <span :class="item.disabled === true ? 'tag gray' : item.disabled === false ? 'tag green' : 'tag amber'">
                                {{ item.disabled === true ? '禁用' : item.disabled === false ? '未禁用' : '禁用状态未知' }}
                              </span>
                              <span :class="item.appended === true ? 'tag amber' : item.appended === false ? 'tag gray' : 'tag amber'">
                                {{ item.appended === true ? '追加' : item.appended === false ? '未追加' : '追加状态未知' }}
                              </span>
                            </li>
                          </ul>
                          <p v-else>无商品行</p>
                        </section>
                        <p class="report-order-total">本地订单件数 {{ formatQuantity(order.itemQuantity) }}</p>
                      </div>
                    </td>
                  </tr>
                </template>
              </tbody>
            </table>
          </div>
          <div v-if="!report?.orders.length" class="empty-overlay">
            <span>
              {{ status === 'loading' ? '正在加载…' : status === 'error' ? '报表加载失败' : '暂无符合条件的本地订单' }}
            </span>
          </div>
        </div>
        <footer class="table-pagination report-pagination">
          <span>共 {{ pageCount }} 页</span>
          <button
            class="page-btn"
            type="button"
            aria-label="上一页"
            :disabled="paginationDisabled || currentPage <= 1"
            @click="changePage(currentPage - 1)"
          >
            <AppIcon name="i-chev-left" icon-class="ic-12" />
          </button>
          <template v-for="item in pagerItems" :key="item.key">
            <span v-if="item.page === null" class="page-ellipsis">{{ item.label }}</span>
            <span v-else-if="item.current" class="page-num" aria-current="page">{{ item.label }}</span>
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
            @click="changePage(currentPage + 1)"
          >
            <AppIcon name="i-chev-right" icon-class="ic-12" />
          </button>
          <label class="page-size" :class="{ 'is-disabled': paginationDisabled }">
            <select
              :value="pageSize"
              :disabled="paginationDisabled"
              aria-label="每页条数"
              @change="changePageSize"
            >
              <option v-for="size in pageSizeOptions" :key="size" :value="size">{{ size }}条/页</option>
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
            >页
          </span>
        </footer>
      </div>
    </div>
  </section>
</template>

<style scoped>
.page-order-snapshot-report {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: var(--surface);
}

.report-searchbar {
  gap: 6px;
  overflow: visible;
}

.report-select {
  width: 160px;
}

.report-generated-at {
  overflow: hidden;
  margin-left: auto;
  color: var(--muted);
  font-size: 12px;
  line-height: 18px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.report-shop-filter {
  position: relative;
  flex: none;
  min-width: 180px;
  color: var(--text);
  font-size: 12px;
}

.report-shop-trigger {
  width: 180px;
  justify-content: space-between;
  gap: 8px;
  color: var(--text);
  list-style: none;
}

.report-shop-trigger::-webkit-details-marker {
  display: none;
}

.report-shop-trigger .caret {
  margin-left: auto;
  transition: transform 0.15s;
}

.report-shop-filter[open] .report-shop-trigger .caret {
  transform: rotate(180deg);
}

.report-shop-options {
  position: absolute;
  z-index: 20;
  top: 34px;
  left: 0;
  display: flex;
  width: min(340px, calc(100vw - 48px));
  max-height: 280px;
  flex-direction: column;
  gap: 2px;
  overflow: auto;
  padding: 8px;
  border: 1px solid var(--line);
  border-radius: var(--radius-card);
  background: var(--surface);
  box-shadow: 0 4px 12px rgb(16 24 40 / 12%);
}

.report-shop-option {
  display: flex;
  min-height: 28px;
  align-items: center;
  gap: 8px;
  padding: 2px 6px;
  border-radius: var(--radius);
  color: var(--body);
  cursor: pointer;
}

.report-shop-option:hover {
  background: var(--primary-faint);
}

.report-shop-empty {
  padding: 6px;
  color: var(--muted);
}

.report-toolbar {
  align-items: center;
  gap: 16px;
  padding: 0 16px;
}

.report-toolbar-left {
  display: flex;
  min-width: 0;
  flex: 1;
  align-items: center;
  gap: 24px;
}

.report-toolbar-right {
  display: flex;
  flex: none;
  align-items: center;
  gap: 8px;
}

.report-metric {
  display: flex;
  flex: none;
  flex-direction: column;
  justify-content: center;
  gap: 0;
}

.report-metric-label {
  color: var(--muted);
  font-size: 12px;
  line-height: 16px;
}

.report-metric-value {
  color: var(--text);
  font-size: 20px;
  font-variant-numeric: tabular-nums;
  font-weight: 500;
  line-height: 24px;
}

.report-scope {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 6px;
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--body);
  font-size: 12px;
  line-height: 18px;
}

.report-scope .ic {
  width: 14px;
  height: 14px;
  flex: none;
  color: var(--muted);
}

.report-scope > span {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.report-scope-title {
  margin-right: 2px;
  color: var(--text);
  font-weight: 600;
}

.report-tables {
  display: flex;
  min-height: 0;
  flex: 1;
  flex-direction: column;
  gap: 10px;
  padding: 0 12px 12px;
  background: var(--surface);
}

.report-error {
  flex: none;
  margin-top: 12px;
}

.report-error-body {
  min-width: 0;
  flex: 1;
}

.report-error .btn {
  flex: none;
  margin-left: auto;
}

.report-panel {
  height: auto;
  min-width: 0;
  display: flex;
  flex-direction: column;
  border: 1px solid var(--line);
  border-radius: var(--radius);
  background: var(--surface);
}

.report-panel-head {
  display: flex;
  min-height: 40px;
  flex: none;
  align-items: center;
  gap: 8px;
  padding: 0 12px;
  background: var(--surface);
}

.report-panel-head h2 {
  flex: none;
  color: var(--text);
  font-size: 14px;
  font-weight: 700;
  line-height: 20px;
}

.report-panel-head p {
  min-width: 0;
  overflow: hidden;
  color: var(--muted);
  font-size: 12px;
  line-height: 18px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.report-groups-panel {
  min-height: 120px;
  flex: 0 1 226px;
}

.report-orders-panel {
  min-height: 150px;
  flex: 1 1 0;
}

.report-groups-table,
.report-orders-table {
  width: 100%;
}

.report-pagination {
  height: 30px;
  padding: 0 8px;
}

.report-detail-sections {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px 22px;
}

.report-detail-sections h3 {
  margin-bottom: 8px;
  color: var(--text);
  font-size: 12px;
  font-weight: 600;
  line-height: 18px;
}

.report-detail-sections ul {
  display: grid;
  gap: 6px;
}

.report-detail-sections li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px 12px;
  color: var(--body);
}

.report-detail-sections p {
  color: var(--muted);
}

.report-detail-sections .report-order-total {
  grid-column: 1 / -1;
  color: var(--text);
  font-weight: 600;
}

.report-orders-table tbody tr.report-order-details > td {
  background: var(--page-soft);
}

.report-searchbar .control:focus-visible,
.report-searchbar .btn:focus-visible,
.report-searchbar select:focus-visible,
.report-toolbar .btn-icon:focus-visible,
.report-shop-option .checkbox:focus-visible,
.report-error .btn:focus-visible,
.report-tables .table-scroll:focus-visible,
.report-tables .page-btn:focus-visible,
.report-tables .page-size select:focus-visible,
.report-tables .page-jump input:focus-visible {
  outline: 2px solid var(--primary);
  outline-offset: 2px;
}

.report-tables .table-scroll:focus-visible {
  outline-offset: -2px;
}

@media (max-height: 820px) {
  .report-toolbar-left {
    gap: 18px;
  }

  .report-metric-value {
    font-size: 18px;
    line-height: 22px;
  }

  .report-groups-panel {
    flex-basis: 222px;
  }

  .report-panel-head {
    min-height: 36px;
  }
}

@media (max-height: 440px) {
  .page-order-snapshot-report {
    overflow-y: auto;
  }

  .report-tables {
    flex: 0 0 auto;
    min-height: 300px;
  }
}
</style>
