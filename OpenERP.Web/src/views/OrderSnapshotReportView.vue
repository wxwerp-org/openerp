<script setup lang="ts">
import { computed } from 'vue'
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

const selectedShopLabel = computed(() =>
  selectedShopKeys.value.length ? `已选 ${selectedShopKeys.value.length} 家店铺` : '全部店铺',
)

const localGeneratedAt = computed(() => {
  const generatedAt = report.value?.generatedAtUtc
  return generatedAt ? new Date(generatedAt).toLocaleString('zh-CN') : ''
})

function formatQuantity(quantity: number): string {
  return new Intl.NumberFormat('zh-CN').format(quantity)
}

function orderIdentity(order: ReportOrder): string {
  return JSON.stringify([order.shop.key, order.id])
}

function changePageSize(event: Event): void {
  const size = Number((event.target as HTMLSelectElement).value)
  if (Number.isInteger(size) && size >= 1 && size <= 100) void load(1, size)
}
</script>

<template>
  <section class="page page-order-snapshot-report active" aria-labelledby="order-snapshot-report-title">
    <header class="report-head report-card">
      <div>
        <h1 id="order-snapshot-report-title">商品量报表</h1>
        <p v-if="report" class="report-generated-at">
          查询时间：<time :datetime="report.generatedAtUtc">{{ localGeneratedAt }}</time>
        </p>
        <p v-else class="report-generated-at">查询本地来源订单的当前商品行快照</p>
      </div>
      <button class="btn" type="button" aria-label="刷新" :disabled="status === 'loading'" @click="load(currentPage, pageSize)">
        刷新
      </button>
    </header>

    <div class="report-scope" aria-label="报表统计口径">
      <strong>统计口径</strong>
      <span>按本地来源订单计数；件数为当前商品行数量合计，包含禁用/追加行。</span>
      <span>不判断付款或另行扣退款；同步未返回的旧记录可能仍保留。</span>
    </div>

    <section class="report-card report-filters" aria-label="报表筛选">
      <label class="report-filter">
        <span>平台</span>
        <select v-model="platform" aria-label="平台筛选" @change="updateFilters">
          <option value="">全部平台</option>
          <option v-for="item in availablePlatforms" :key="item" :value="item">{{ platformLabel(item) }}</option>
        </select>
      </label>

      <details class="report-shop-filter">
        <summary aria-label="店铺筛选">{{ selectedShopLabel }}</summary>
        <div class="report-shop-options">
          <label v-for="shop in availableShops" :key="shop.key">
            <input
              v-model="selectedShopKeys"
              type="checkbox"
              :value="shop.key"
              :disabled="!selectedShopKeys.includes(shop.key) && selectedShopKeys.length >= 100"
              @change="updateFilters"
            >
            <span>{{ platformLabel(shop.platform) }} · {{ shop.label }}</span>
          </label>
          <p v-if="availableShops.length === 0">暂无可选店铺</p>
        </div>
      </details>

      <button class="btn soft" type="button" :disabled="status === 'loading'" @click="resetFilters">清除筛选</button>
      <span v-if="status === 'loading'" class="report-load-state" role="status">正在加载商品量报表…</span>
      <span v-else-if="status === 'success'" class="report-load-state" role="status">报表已更新</span>
    </section>

    <div v-if="status === 'error'" class="notice red report-error" role="alert">
      <div>
        <strong class="notice-title">{{ errorMessage }}</strong>
        <p class="notice-desc">请检查连接后重试。</p>
      </div>
      <button class="btn" type="button" aria-label="重试" @click="load(currentPage, pageSize)">重试</button>
    </div>

    <section class="report-totals" aria-label="报表汇总">
      <article class="report-total report-card">
        <span>本地订单数</span>
        <strong aria-label="订单总数">{{ report ? formatQuantity(report.summary.orderCount) : '—' }}</strong>
      </article>
      <article class="report-total report-card">
        <span>商品件数</span>
        <strong aria-label="商品件数">{{ report ? formatQuantity(report.summary.itemQuantity) : '—' }}</strong>
      </article>
    </section>

    <section class="report-card report-table-card" aria-labelledby="report-groups-title">
      <header class="report-section-head">
        <div>
          <h2 id="report-groups-title">分店汇总</h2>
          <p>统计当前筛选下的全部本地来源订单</p>
        </div>
      </header>
      <div class="report-table-scroll">
        <table class="grid-table is-wrap report-groups-table">
          <caption class="visually-hidden">分店订单与商品件数汇总</caption>
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
          <tbody v-else>
            <tr><td class="report-empty-row" colspan="4">{{ status === 'loading' ? '正在加载…' : '暂无分店汇总' }}</td></tr>
          </tbody>
        </table>
      </div>
    </section>

    <section class="report-card report-table-card" aria-labelledby="report-orders-title">
      <header class="report-section-head">
        <div>
          <h2 id="report-orders-title">订单明细</h2>
          <p>一行对应一条本地来源订单；展开查看当前商品行和平台订单参考号。</p>
        </div>
      </header>
      <div class="report-table-scroll">
        <table class="grid-table is-wrap report-orders-table">
          <caption class="visually-hidden">本地来源订单分页明细</caption>
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
          <tbody v-else>
            <tr>
              <td class="report-empty-row" colspan="8">
                {{ status === 'loading' ? '正在加载…' : status === 'error' ? '报表加载失败' : '暂无符合条件的本地订单' }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <footer class="report-pagination">
        <span>第 {{ report?.page ?? currentPage }} 页，共 {{ report?.pageCount ?? 1 }} 页</span>
        <button class="page-btn" type="button" aria-label="上一页" :disabled="status === 'loading' || !report || currentPage <= 1" @click="changePage(currentPage - 1)">‹</button>
        <span class="page-num">{{ report?.page ?? currentPage }}</span>
        <button class="page-btn" type="button" aria-label="下一页" :disabled="status === 'loading' || !report || currentPage >= report.pageCount" @click="changePage(currentPage + 1)">›</button>
        <label class="report-page-size">
          <span>每页条数</span>
          <select aria-label="每页条数" :value="pageSize" :disabled="status === 'loading'" @change="changePageSize">
            <option value="20">20</option>
            <option value="50">50</option>
            <option value="100">100</option>
          </select>
        </label>
      </footer>
    </section>
  </section>
</template>

<style scoped>
.page-order-snapshot-report {
  display: flex;
  flex-direction: column;
  gap: 12px;
  overflow-y: auto;
  padding: 16px;
  background: var(--page);
}

.report-card {
  min-width: 0;
  border: 1px solid var(--line);
  border-radius: 6px;
  background: #fff;
}

.report-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 14px 18px;
}

.report-head h1 {
  color: var(--text);
  font-size: 20px;
  font-weight: 600;
  line-height: 28px;
}

.report-generated-at,
.report-section-head p {
  margin-top: 4px;
  color: var(--muted);
  font-size: 12px;
  line-height: 18px;
}

.report-scope {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 12px;
  padding: 10px 14px;
  border: 1px solid #d7e4ff;
  border-radius: 4px;
  background: #f5f8ff;
  color: var(--body);
  font-size: 12px;
  line-height: 18px;
}

.report-scope strong {
  color: var(--text);
}

.report-filters {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  padding: 12px 14px;
}

.report-filter {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--body);
  font-size: 12px;
}

.report-filter select,
.report-page-size select {
  min-width: 160px;
  height: 30px;
  padding: 0 8px;
  border: 1px solid var(--input-border);
  border-radius: 4px;
  background: #fff;
  color: var(--text);
  font-size: 12px;
}

.report-shop-filter {
  position: relative;
  min-width: 180px;
  color: var(--text);
  font-size: 12px;
}

.report-shop-filter summary {
  display: flex;
  height: 30px;
  align-items: center;
  padding: 0 10px;
  border: 1px solid var(--input-border);
  border-radius: 4px;
  background: #fff;
  cursor: pointer;
  list-style: none;
}

.report-shop-filter summary::-webkit-details-marker {
  display: none;
}

.report-shop-filter summary::after {
  margin-left: auto;
  color: var(--muted);
  content: '▾';
}

.report-shop-options {
  position: absolute;
  z-index: 10;
  top: 34px;
  left: 0;
  display: flex;
  width: min(340px, calc(100vw - 48px));
  max-height: 280px;
  flex-direction: column;
  gap: 8px;
  overflow: auto;
  padding: 10px;
  border: 1px solid var(--line);
  border-radius: 4px;
  background: #fff;
  box-shadow: 0 4px 12px rgb(30 45 70 / 12%);
}

.report-shop-options label {
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 24px;
}

.report-load-state {
  margin-left: auto;
  color: var(--muted);
  font-size: 12px;
}

.report-error {
  align-items: center;
  justify-content: space-between;
}

.report-totals {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.report-total {
  display: flex;
  min-height: 86px;
  flex-direction: column;
  justify-content: center;
  gap: 6px;
  padding: 14px 18px;
  color: var(--muted);
  font-size: 12px;
}

.report-total strong {
  color: var(--text);
  font-size: 24px;
  font-variant-numeric: tabular-nums;
  font-weight: 600;
}

.report-table-card {
  display: flex;
  min-height: 0;
  flex-direction: column;
  overflow: hidden;
}

.report-section-head {
  display: flex;
  min-height: 56px;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  border-bottom: 1px solid var(--line-soft);
}

.report-section-head h2 {
  color: var(--text);
  font-size: 14px;
  font-weight: 600;
  line-height: 20px;
}

.report-table-scroll {
  min-width: 0;
  overflow: auto;
}

.report-groups-table {
  width: 100%;
  min-width: 560px;
}

.report-orders-table {
  width: 100%;
  min-width: 960px;
}

.report-empty-row {
  height: 84px !important;
  color: var(--muted) !important;
  text-align: center;
}

.report-order-details > td {
  background: #f8faff !important;
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

.report-order-total {
  grid-column: 1 / -1;
  color: var(--text) !important;
  font-weight: 600;
}

.report-pagination {
  display: flex;
  min-height: 44px;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  padding: 6px 12px;
  color: var(--body);
  font-size: 12px;
}

.report-page-size {
  display: flex;
  align-items: center;
  gap: 6px;
}

.report-page-size select {
  min-width: 72px;
  height: 28px;
}

@media (max-width: 720px) {
  .page-order-snapshot-report {
    padding: 10px;
  }

  .report-totals {
    grid-template-columns: 1fr;
  }

  .report-total {
    min-height: 72px;
  }

  .report-load-state {
    width: 100%;
    margin-left: 0;
  }

  .report-detail-sections {
    grid-template-columns: 1fr;
  }

  .report-order-total {
    grid-column: auto;
  }
}
</style>
