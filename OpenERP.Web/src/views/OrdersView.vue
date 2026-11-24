<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { apiRequest } from '../account/session'
import OrderSyncDialog from '../forms/OrderSyncDialog.vue'
import { useErpHub } from '../composables/useErpHub'
import { platformLabel } from '../reports/orderSnapshot'
import AppButton from '../ui/AppButton.vue'
import AppIcon from '../ui/AppIcon.vue'
import AppIconButton from '../ui/AppIconButton.vue'
import AppInput from '../ui/AppInput.vue'
import AppListPage from '../ui/AppListPage.vue'
import AppPagination from '../ui/AppPagination.vue'
import AppSelect from '../ui/AppSelect.vue'
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
const pageSize = ref(20)
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

  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize.value) })
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

function changePageSize(size: number): void {
  pageSize.value = size
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
const filterHint = 'flex h-[var(--control-h)] flex-none items-center text-12/18 text-body'
const shopOptions = computed(() => [
  { value: '', label: '全部店铺' },
  ...options.value.shops.map((shop) => ({
    value: String(shop.sid),
    label: shop.name || `店铺 #${shop.sid}`,
  })),
])
const platformOptions = computed(() => [
  { value: '', label: '全部平台' },
  ...options.value.platforms.map((platform) => ({
    value: platform,
    label: platformLabel(platform),
  })),
])
const statusOptions = computed(() => [
  { value: '', label: '全部状态' },
  ...options.value.statuses.map((status) => ({ value: status, label: status })),
])
const statusTag =
  'inline-flex items-center justify-center whitespace-normal rounded-2 border border-line bg-page-soft px-5 text-11/16 text-body'
const platformEntry = 'flex flex-wrap items-baseline gap-8'
const platformEntryMarked =
  'flex flex-wrap items-baseline gap-8 -ml-4 rounded-3 bg-order-mark-bg px-4 text-order-mark-text'
const message = 'grid justify-items-center gap-8 px-16 py-24 text-body text-center'
</script>

<template>
  <AppListPage
    id="page-orders"
    title="订单列表"
    filters-label="筛选订单"
    filters-as="form"
    @search="search"
  >
    <template #filters>
      <AppSelect
        v-model="filters.shopSid"
        size="sm"
        focus-ring
        class="w-132"
        name="shopSid"
        aria-label="店铺"
        :options="shopOptions"
      />
      <AppSelect
        v-model="filters.platform"
        size="sm"
        focus-ring
        class="w-132"
        name="platform"
        aria-label="平台"
        :options="platformOptions"
      />
      <AppSelect
        v-model="filters.status"
        size="sm"
        focus-ring
        class="w-132"
        name="status"
        aria-label="订单状态"
        :options="statusOptions"
      />
      <AppInput
        v-model="filters.number"
        size="sm"
        focus-ring
        class="w-172"
        name="number"
        type="search"
        placeholder="平台订单号"
        aria-label="平台订单号"
      />
      <span :class="filterHint">创建日期</span>
      <AppInput
        v-model="filters.createdFrom"
        size="sm"
        focus-ring
        class="w-132"
        name="createdFrom"
        type="date"
        aria-label="创建开始日期"
        :max="filters.createdTo || undefined"
      />
      <span :class="filterHint">至</span>
      <AppInput
        v-model="filters.createdTo"
        size="sm"
        focus-ring
        class="w-132"
        name="createdTo"
        type="date"
        aria-label="创建结束日期"
        :min="filters.createdFrom || undefined"
      />
      <AppButton type="submit" variant="primary" :class="focusRing">搜索</AppButton>
      <AppButton variant="soft" :class="focusRing" aria-label="重置筛选" @click="reset">
        重置
      </AppButton>
    </template>
    <template #toolbar>
      <AppButton
        :class="focusRing"
        aria-haspopup="dialog"
        aria-controls="order-sync-panel"
        @click="syncOpen = true"
      >
        同步历史订单
      </AppButton>
    </template>
    <template #toolbar-right>
      <AppIconButton label="刷新" tone="toolbar" :class="focusRing" @click="load()">
        <AppIcon name="i-refresh" />
      </AppIconButton>
    </template>
    <AppTable
      caption="订单列表"
      label="订单列表"
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
          <AppTh>店铺</AppTh>
          <AppTh>平台</AppTh>
          <AppTh>平台订单号 / 状态</AppTh>
          <AppTh>创建时间</AppTh>
          <AppTh>出库时间</AppTh>
          <AppTh numeric>商品行数</AppTh>
          <AppTh numeric>商品件数</AppTh>
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
        <AppPagination
          :page="currentPage"
          :page-count="pageCount"
          :page-size="pageSize"
          :total-text="result ? `共 ${result.total} 条` : undefined"
          :disabled="!result"
          label="订单分页"
          focus-ring
          class="border-t border-line"
          @update:page="load"
          @update:page-size="changePageSize"
        />
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
  </AppListPage>
  <OrderSyncDialog :open="syncOpen" :hub="hub" @close="syncOpen = false" />
</template>
