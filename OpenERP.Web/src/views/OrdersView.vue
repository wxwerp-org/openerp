<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { apiRequest } from '../account/session'
import OrderSyncDialog from '../forms/OrderSyncDialog.vue'
import { useErpHub } from '../composables/useErpHub'
import { platformLabel } from '../reports/orderSnapshot'
import AppButton from '../ui/AppButton.vue'
import AppIcon from '../ui/AppIcon.vue'
import AppIconButton from '../ui/AppIconButton.vue'
import AppTable from '../ui/AppTable.vue'
import AppTd from '../ui/AppTd.vue'
import AppTh from '../ui/AppTh.vue'

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

const syncOpen = ref(false)
const hub = useErpHub({
  shopSync: false,
  onOrdersChanged: () => void load(currentPage.value, true),
})
const defaultPageSize = 20
const currentPage = ref(1)
const result = ref<OrderPage | null>(null)
const loadError = ref('')
const emptyOptions: OrderPage['options'] = { shops: [], platforms: [], statuses: [] }
const options = ref<OrderPage['options']>(emptyOptions)
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
  loadError.value = ''
  currentPage.value = page

  const query = new URLSearchParams({ page: String(page), pageSize: String(defaultPageSize) })
  for (const [name, value] of Object.entries(appliedFilters.value)) {
    if (value) query.set(name, value)
  }

  try {
    const response = await apiRequest(`/api/orders?${query}`, {
      signal: request.signal,
    })
    if (request.signal.aborted) return
    if (response.status === 403) {
      result.value = null
      options.value = emptyOptions
      loadError.value = '无权查看所选店铺，请重置筛选。'
      return
    }
    if (!response.ok) throw new Error()
    const data: OrderPage = await response.json()
    if (!request.signal.aborted) {
      result.value = data
      options.value = data.options
      currentPage.value = data.page
    }
  } catch {
    if (!request.signal.aborted) loadError.value = '无法加载订单，请稍后重试。'
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

const focusRing =
  'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary'
const filterField = 'grid gap-4 text-12/18 text-body'
const filterControl =
  'inline-flex h-[var(--control-h)] shrink-0 items-center rounded-4 border border-input bg-white px-10 py-0 text-12/28 text-ink hover:border-input-hover'
const filterInput =
  'inline-flex h-[var(--control-h)] shrink-0 items-center rounded-4 border border-input bg-white px-10 py-0 text-12/18 text-ink hover:border-input-hover'
const pageButton =
  'inline-flex h-28 w-28 shrink-0 items-center justify-center rounded-4 border border-transparent bg-white text-body hover:bg-page-btn-hover hover:text-primary disabled:cursor-default disabled:text-btn-disabled-text disabled:hover:bg-transparent disabled:hover:text-btn-disabled-text'
const statusTag =
  'inline-flex items-center justify-center whitespace-normal rounded-2 border border-line bg-page-soft px-5 text-11/16 text-body'
const platformEntry = 'flex flex-wrap items-baseline gap-8'
const platformEntryMarked =
  'flex flex-wrap items-baseline gap-8 -ml-4 rounded-3 bg-order-mark-bg px-4 text-order-mark-text'
const message = 'grid justify-items-center gap-8 px-16 py-24 text-body text-center'
const pager =
  'flex min-h-40 flex-none items-center justify-end gap-8 whitespace-nowrap border-t border-line bg-white px-12 text-12/18 text-body'
</script>

<template>
  <section id="page-orders" class="flex h-full w-full min-h-0 flex-col overflow-auto bg-page p-16">
    <div class="flex min-h-320 flex-1 flex-col overflow-hidden rounded-6 bg-white">
      <header
        data-testid="orders-toolbar"
        class="flex h-54 flex-none items-center gap-6 bg-white px-16"
      >
        <h1 class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
          订单列表
        </h1>
        <div class="ml-auto flex shrink-0 items-center gap-6">
          <AppIconButton label="刷新" tone="toolbar" :class="focusRing" @click="load()">
            <AppIcon name="i-refresh" />
          </AppIconButton>
          <AppButton
            :class="focusRing"
            aria-haspopup="dialog"
            aria-controls="order-sync-panel"
            @click="syncOpen = true"
          >
            同步历史订单
          </AppButton>
        </div>
      </header>
      <form
        class="flex flex-none flex-wrap items-end gap-10 overflow-visible border-b border-line bg-white px-16 pt-4 pb-12"
        aria-label="筛选订单"
        @submit.prevent="search"
      >
        <label :class="filterField">
          店铺
          <select
            v-model="filters.shopSid"
            :class="[filterControl, 'w-132', focusRing]"
            name="shopSid"
          >
            <option value="">全部店铺</option>
            <option v-for="shop in options.shops" :key="shop.sid" :value="String(shop.sid)">
              {{ shop.name || `店铺 #${shop.sid}` }}
            </option>
          </select>
        </label>
        <label :class="filterField">
          平台
          <select
            v-model="filters.platform"
            :class="[filterControl, 'w-132', focusRing]"
            name="platform"
          >
            <option value="">全部平台</option>
            <option v-for="platform in options.platforms" :key="platform" :value="platform">
              {{ platformLabel(platform) }}
            </option>
          </select>
        </label>
        <label :class="filterField">
          订单状态
          <select
            v-model="filters.status"
            :class="[filterControl, 'w-132', focusRing]"
            name="status"
          >
            <option value="">全部状态</option>
            <option v-for="status in options.statuses" :key="status" :value="status">
              {{ status }}
            </option>
          </select>
        </label>
        <label :class="filterField">
          平台订单号
          <input
            v-model="filters.number"
            :class="[filterInput, 'w-172', focusRing]"
            name="number"
            type="search"
          />
        </label>
        <label :class="filterField">
          创建开始日期
          <input
            v-model="filters.createdFrom"
            :class="[filterInput, 'w-132', focusRing]"
            name="createdFrom"
            type="date"
            :max="filters.createdTo || undefined"
          />
        </label>
        <label :class="filterField">
          创建结束日期
          <input
            v-model="filters.createdTo"
            :class="[filterInput, 'w-132', focusRing]"
            name="createdTo"
            type="date"
            :min="filters.createdFrom || undefined"
          />
        </label>
        <AppButton type="submit" variant="primary" :class="focusRing">搜索</AppButton>
        <AppButton variant="soft" :class="focusRing" aria-label="重置筛选" @click="reset">
          重置
        </AppButton>
      </form>
      <AppTable
        caption="订单列表"
        label="订单列表"
        variant="card"
        wrap
        scroll-y
        focus-ring
        width="100%"
        :min-width="800"
      >
        <colgroup>
          <col style="width: 16%" />
          <col style="width: 12%" />
          <col />
          <col style="width: 148px" />
          <col style="width: 148px" />
          <col style="width: 78px" />
          <col style="width: 78px" />
        </colgroup>
        <thead>
          <tr>
            <AppTh surface="page-soft">店铺</AppTh>
            <AppTh surface="page-soft">平台</AppTh>
            <AppTh surface="page-soft">平台订单号 / 状态</AppTh>
            <AppTh surface="page-soft">创建时间</AppTh>
            <AppTh surface="page-soft">出库时间</AppTh>
            <AppTh surface="page-soft" numeric>商品行数</AppTh>
            <AppTh surface="page-soft" numeric>商品件数</AppTh>
          </tr>
        </thead>
        <tbody>
          <tr v-for="order in result?.items" :key="order.id">
            <AppTd class="tabular-nums">{{ order.shopName || `店铺 #${order.sid}` }}</AppTd>
            <AppTd class="tabular-nums">{{ platformLabel(order.platform) }}</AppTd>
            <AppTd class="tabular-nums">
              <ul v-if="order.platformOrders.length" class="grid justify-items-start gap-4">
                <li v-for="(platformOrder, index) in order.platformOrders" :key="index">
                  <component
                    :is="
                      platformOrder.matched && (appliedFilters.number || appliedFilters.status)
                        ? 'mark'
                        : 'span'
                    "
                    :class="
                      platformOrder.matched && (appliedFilters.number || appliedFilters.status)
                        ? platformEntryMarked
                        : platformEntry
                    "
                  >
                    <span>{{ platformOrder.number || '—' }}</span>
                    <span :class="statusTag">{{ platformOrder.status || '—' }}</span>
                  </component>
                </li>
              </ul>
              <span v-else>—</span>
            </AppTd>
            <AppTd class="tabular-nums">{{ sourceTime(order.created) }}</AppTd>
            <AppTd class="tabular-nums">{{ sourceTime(order.dispatched) }}</AppTd>
            <AppTd numeric>{{ order.lineCount }}</AppTd>
            <AppTd numeric>{{ order.itemQuantity }}</AppTd>
          </tr>
        </tbody>
        <template #footer>
          <nav :class="pager" aria-label="订单分页">
            <span v-if="result">共{{ result.total }}条</span>
            <button
              :class="[pageButton, focusRing]"
              type="button"
              aria-label="上一页"
              :disabled="!result || currentPage <= 1"
              @click="load(currentPage - 1)"
            >
              <AppIcon name="i-chev-left" :size="12" />
            </button>
            <span
              aria-current="page"
              class="inline-flex h-28 min-w-28 items-center justify-center rounded-4 border border-page-num-border bg-nav-active px-6 text-primary tabular-nums"
            >
              {{ currentPage }}
            </span>
            <span>/ {{ pageCount }} 页</span>
            <button
              :class="[pageButton, focusRing]"
              type="button"
              aria-label="下一页"
              :disabled="!result || currentPage >= pageCount"
              @click="load(currentPage + 1)"
            >
              <AppIcon name="i-chev-right" :size="12" />
            </button>
            <span>{{ defaultPageSize }}条/页</span>
          </nav>
        </template>
        <template #scroll-end>
          <div v-if="loadError" data-testid="orders-message" :class="message">
            <p role="alert">{{ loadError }}</p>
            <AppButton :class="focusRing" aria-label="重新加载订单" @click="load()">重试</AppButton>
          </div>
          <div
            v-else-if="!result || !result.items.length"
            data-testid="orders-message"
            :class="message"
          >
            <p role="status">
              {{
                !result ? '正在加载订单…' : isFiltered ? '未找到符合筛选条件的订单' : '暂无同步订单'
              }}
            </p>
            <AppButton
              v-if="result && !isFiltered"
              :class="focusRing"
              aria-haspopup="dialog"
              aria-controls="order-sync-panel"
              @click="syncOpen = true"
            >
              同步历史订单
            </AppButton>
          </div>
        </template>
      </AppTable>
    </div>
  </section>
  <OrderSyncDialog :open="syncOpen" :hub="hub" @close="syncOpen = false" />
</template>
