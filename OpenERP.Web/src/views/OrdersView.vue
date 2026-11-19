<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { apiRequest } from '../account/session'
import OrderNoticeDialog from '../forms/OrderNoticeDialog.vue'
import OrderSyncDialog from '../forms/OrderSyncDialog.vue'
import { useErpHub } from '../composables/useErpHub'
import { platformLabel } from '../reports/orderSnapshot'

type Order = {
  id: string
  sid: number
  shopName: string | null
  platform: string
  platformOrders: { number: string; status: string; matched: boolean }[]
  created: string | null
  dispatched: string | null
  lineCount: number
  itemQuantity: number
}

type OrderPage = {
  items: Order[]
  total: number
  page: number
  pageSize: number
  options: {
    shops: { sid: number; name: string | null }[]
    platforms: string[]
    statuses: string[]
  }
}

const noticeOpen = ref(false)
const syncOpen = ref(false)
const hub = useErpHub({
  shopSync: false,
  onOrdersChanged: () => void load(currentPage.value, true),
})
const defaultPageSize = 20
const currentPage = ref(1)
const result = ref<OrderPage | null>(null)
const loadFailed = ref(false)
const options = ref<OrderPage['options']>({ shops: [], platforms: [], statuses: [] })
const emptyFilters = {
  shopSid: '',
  platform: '',
  status: '',
  number: '',
  createdFrom: '',
  createdTo: '',
}
const filters = reactive({ ...emptyFilters })
const appliedFilters = ref({ ...filters })
const isFiltered = computed(() => Object.values(appliedFilters.value).some(Boolean))
const pageCount = computed(() =>
  result.value ? Math.max(1, Math.ceil(result.value.total / result.value.pageSize)) : 1,
)
let activeRequest: AbortController | undefined

function sourceTime(value: string | null): string {
  // 保留来源记录的日历日期和墙钟时间，与创建日期筛选的口径一致。
  return value ? value.slice(0, 19).replace('T', ' ') : '—'
}

async function load(page = currentPage.value, keepOrders = false): Promise<void> {
  activeRequest?.abort()
  const request = new AbortController()
  activeRequest = request
  if (!keepOrders) result.value = null
  loadFailed.value = false
  currentPage.value = page

  const query = new URLSearchParams({ page: String(page), pageSize: String(defaultPageSize) })
  for (const [name, value] of Object.entries(appliedFilters.value)) {
    if (value) query.set(name, value)
  }

  try {
    const response = await apiRequest(`/api/orders?${query}`, {
      signal: request.signal,
    })
    if (!response.ok) throw new Error()
    const data: OrderPage = await response.json()
    if (!request.signal.aborted) {
      result.value = data
      options.value = data.options
      currentPage.value = data.page
    }
  } catch {
    if (!request.signal.aborted) loadFailed.value = true
  }
}

function search(): void {
  appliedFilters.value = { ...filters, number: filters.number.trim() }
  void load(1)
}

function reset(): void {
  Object.assign(filters, emptyFilters)
  search()
}

onMounted(() => {
  void load()
  void hub.connect()
})
onUnmounted(() => {
  activeRequest?.abort()
  void hub.disconnect()
})
</script>

<template>
  <section id="page-orders" class="page page-orders active">
    <div class="order-card">
      <header class="card-head list-toolbar order-toolbar">
        <h1 class="card-title">订单列表</h1>
        <div class="toolbar-right">
          <button type="button" class="btn-icon" title="刷新" aria-label="刷新" @click="load()">
            <svg aria-hidden="true" class="ic"><use href="#i-refresh" /></svg>
          </button>
          <button
            class="btn"
            type="button"
            aria-haspopup="dialog"
            aria-controls="order-sync-panel"
            @click="syncOpen = true"
          >
            同步历史订单
          </button>
          <button id="openNotice" class="btn notice-entry" type="button" @click="noticeOpen = true">
            <svg aria-hidden="true" class="ic"><use href="#i-bell" /></svg>
            订单公告
          </button>
        </div>
      </header>
      <form class="list-searchbar order-filter" aria-label="筛选订单" @submit.prevent="search">
        <label>
          店铺
          <select v-model="filters.shopSid" class="control" name="shopSid">
            <option value="">全部店铺</option>
            <option v-for="shop in options.shops" :key="shop.sid" :value="String(shop.sid)">
              {{ shop.name || `店铺 #${shop.sid}` }}
            </option>
          </select>
        </label>
        <label>
          平台
          <select v-model="filters.platform" class="control" name="platform">
            <option value="">全部平台</option>
            <option v-for="platform in options.platforms" :key="platform" :value="platform">
              {{ platformLabel(platform) }}
            </option>
          </select>
        </label>
        <label>
          订单状态
          <select v-model="filters.status" class="control" name="status">
            <option value="">全部状态</option>
            <option v-for="status in options.statuses" :key="status" :value="status">
              {{ status }}
            </option>
          </select>
        </label>
        <label>
          平台订单号
          <input v-model="filters.number" class="control" name="number" type="search" />
        </label>
        <label>
          创建开始日期
          <input
            v-model="filters.createdFrom"
            class="control"
            name="createdFrom"
            type="date"
            :max="filters.createdTo || undefined"
          />
        </label>
        <label>
          创建结束日期
          <input
            v-model="filters.createdTo"
            class="control"
            name="createdTo"
            type="date"
            :min="filters.createdFrom || undefined"
          />
        </label>
        <button class="btn primary" type="submit">搜索</button>
        <button class="btn soft" type="button" aria-label="重置筛选" @click="reset">重置</button>
      </form>
      <div class="table-region">
        <div class="table-box">
          <div class="table-area">
            <div class="table-scroll is-y" tabindex="0" role="region" aria-label="订单列表">
              <table class="grid-table is-wrap order-table">
                <caption class="visually-hidden">
                  订单列表
                </caption>
                <colgroup>
                  <col class="order-shop-column" />
                  <col class="order-platform-column" />
                  <col />
                  <col class="order-time-column" />
                  <col class="order-time-column" />
                  <col class="order-count-column" />
                  <col class="order-count-column" />
                </colgroup>
                <thead>
                  <tr>
                    <th scope="col">店铺</th>
                    <th scope="col">平台</th>
                    <th scope="col">平台订单号 / 状态</th>
                    <th scope="col">创建时间</th>
                    <th scope="col">出库时间</th>
                    <th scope="col" class="num-cell">商品行数</th>
                    <th scope="col" class="num-cell">商品件数</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="order in result?.items" :key="order.id">
                    <td>{{ order.shopName || `店铺 #${order.sid}` }}</td>
                    <td>{{ platformLabel(order.platform) }}</td>
                    <td>
                      <ul v-if="order.platformOrders.length" class="order-platform-list">
                        <li v-for="(platformOrder, index) in order.platformOrders" :key="index">
                          <component
                            :is="
                              platformOrder.matched &&
                              (appliedFilters.number || appliedFilters.status)
                                ? 'mark'
                                : 'span'
                            "
                            class="order-platform-entry"
                          >
                            <span>{{ platformOrder.number || '—' }}</span>
                            <span class="tag order-status">{{ platformOrder.status || '—' }}</span>
                          </component>
                        </li>
                      </ul>
                      <span v-else>—</span>
                    </td>
                    <td>{{ sourceTime(order.created) }}</td>
                    <td>{{ sourceTime(order.dispatched) }}</td>
                    <td class="num-cell">{{ order.lineCount }}</td>
                    <td class="num-cell">{{ order.itemQuantity }}</td>
                  </tr>
                </tbody>
              </table>
              <div v-if="loadFailed" class="order-message">
                <p role="alert">无法加载订单，请稍后重试。</p>
                <button class="btn" type="button" aria-label="重新加载订单" @click="load()">
                  重试
                </button>
              </div>
              <div v-else-if="!result || !result.items.length" class="order-message">
                <p role="status">
                  {{
                    !result
                      ? '正在加载订单…'
                      : isFiltered
                        ? '未找到符合筛选条件的订单'
                        : '暂无同步订单'
                  }}
                </p>
                <button
                  v-if="result && !isFiltered"
                  class="btn"
                  type="button"
                  aria-haspopup="dialog"
                  aria-controls="order-sync-panel"
                  @click="syncOpen = true"
                >
                  同步历史订单
                </button>
              </div>
            </div>
          </div>
          <nav class="table-pagination" aria-label="订单分页">
            <span v-if="result">共{{ result.total }}条</span>
            <button
              class="page-btn"
              type="button"
              aria-label="上一页"
              :disabled="!result || currentPage <= 1"
              @click="load(currentPage - 1)"
            >
              <svg aria-hidden="true" class="ic ic-12"><use href="#i-chev-left" /></svg>
            </button>
            <span class="page-num" aria-current="page">{{ currentPage }}</span>
            <span>/ {{ pageCount }} 页</span>
            <button
              class="page-btn"
              type="button"
              aria-label="下一页"
              :disabled="!result || currentPage >= pageCount"
              @click="load(currentPage + 1)"
            >
              <svg aria-hidden="true" class="ic ic-12"><use href="#i-chev-right" /></svg>
            </button>
            <span>{{ defaultPageSize }}条/页</span>
          </nav>
        </div>
      </div>
    </div>
  </section>
  <OrderSyncDialog :open="syncOpen" :hub="hub" @close="syncOpen = false" />
  <OrderNoticeDialog :open="noticeOpen" @close="noticeOpen = false" />
</template>
