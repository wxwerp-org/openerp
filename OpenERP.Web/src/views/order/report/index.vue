<!-- 订单报表：按创建日期、店铺、平台筛选，从总览、趋势、店铺/平台/状态/物流维度和热销商品查看本地订单，对接 `/api/orders/report`。 -->
<template>
  <div class="page-stack">
    <ElCard class="search-card" shadow="never">
      <ElForm
        :model="filters"
        inline
        size="small"
        label-position="top"
        class="search-form"
        @submit.prevent="search"
      >
        <ElFormItem label="创建日期">
          <ElDatePicker
            v-model="dateRange"
            type="daterange"
            value-format="YYYY-MM-DD"
            range-separator="至"
            start-placeholder="开始日期"
            end-placeholder="结束日期"
            :shortcuts="shortcuts"
            :clearable="false"
            style="width: 240px"
          />
        </ElFormItem>
        <ElFormItem label="店铺">
          <ElSelect v-model="filters.shopSid" style="width: 140px">
            <ElOption label="全部店铺" value="" />
            <ElOption
              v-for="shop in options.shops"
              :key="shop.sid"
              :label="shop.name || `店铺 #${shop.sid}`"
              :value="String(shop.sid)"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="平台">
          <ElSelect v-model="filters.platform" style="width: 120px">
            <ElOption label="全部平台" value="" />
            <ElOption v-for="p in options.platforms" :key="p" :label="p" :value="p" />
          </ElSelect>
        </ElFormItem>
        <ElFormItem class="search-actions">
          <ElButton type="primary" native-type="submit" :loading="loading">查询</ElButton>
        </ElFormItem>
      </ElForm>
    </ElCard>

    <AppNotice v-if="loadError" tone="error">
      {{ loadError }}
      <template #action><ElButton link @click="load()">重试</ElButton></template>
    </AppNotice>

    <div v-loading="loading" class="report">
      <div class="kpis">
        <ElCard v-for="kpi in kpis" :key="kpi.label" shadow="never" class="kpi">
          <div class="kpi__label">{{ kpi.label }}</div>
          <div class="kpi__value">{{ kpi.value }}</div>
          <div class="kpi__hint">{{ kpi.hint }}</div>
        </ElCard>
        <ElCard shadow="never" class="kpi kpi--amount">
          <div class="kpi__label">订单金额（按币种，不换算）</div>
          <div v-if="report?.summary.amounts.length" class="amounts">
            <div v-for="a in report.summary.amounts" :key="a.currency" class="amounts__row">
              <strong>{{ money(a.total, a.currency) }}</strong>
              <span class="muted"
                >客单价 {{ money(a.total / a.orders, a.currency) }} · {{ a.orders }} 单</span
              >
            </div>
          </div>
          <div v-else class="kpi__value">—</div>
        </ElCard>
      </div>

      <ElCard shadow="never">
        <template #header>
          <div class="card-head">
            <span>订单趋势（按创建日期）</span>
            <ElRadioGroup v-model="metric" size="small">
              <ElRadioButton value="orders">订单数</ElRadioButton>
              <ElRadioButton value="itemQuantity">商品件数</ElRadioButton>
            </ElRadioGroup>
          </div>
        </template>
        <ArtLineChart
          height="260px"
          :data="trendData"
          :xAxisData="trendDays"
          :isEmpty="!trendDays.length"
          showAreaColor
        />
      </ElCard>

      <ElCard shadow="never">
        <ElTabs v-model="dimension">
          <ElTabPane v-for="d in dimensions" :key="d.key" :label="d.label" :name="d.key" />
        </ElTabs>
        <ElTable :data="rows" size="small" class="report-table">
          <ElTableColumn
            :label="currentDimension.label"
            prop="label"
            min-width="160"
            show-overflow-tooltip
          />
          <ElTableColumn label="订单数" prop="orders" width="90" align="right" sortable />
          <ElTableColumn label="占比" min-width="150">
            <template #default="{ row }">
              <ElProgress :percentage="share(row.orders)" :stroke-width="8" />
            </template>
          </ElTableColumn>
          <ElTableColumn label="商品件数" prop="itemQuantity" width="100" align="right" sortable />
          <ElTableColumn label="订单金额" min-width="200">
            <template #default="{ row }">
              <div v-for="a in row.amounts" :key="a.currency">{{ money(a.total, a.currency) }}</div>
              <span v-if="!row.amounts.length" class="muted">—</span>
            </template>
          </ElTableColumn>
          <template #empty><ElEmpty description="暂无数据" :image-size="60" /></template>
        </ElTable>
      </ElCard>

      <ElCard shadow="never">
        <template #header>热销商品 TOP 10（按销量）</template>
        <ElTable :data="report?.products ?? []" size="small" class="report-table">
          <ElTableColumn label="#" type="index" width="50" />
          <ElTableColumn label="商品" min-width="320">
            <template #default="{ row }">
              <div class="product">
                <ElImage
                  class="product__image"
                  :src="row.imageUrl || undefined"
                  fit="cover"
                  lazy
                  referrerpolicy="no-referrer"
                >
                  <template #error>
                    <div class="product__image-empty"><ArtSvgIcon icon="ri:image-line" /></div>
                  </template>
                </ElImage>
                <div class="product__body">
                  <div class="product__name">{{ row.name || '未命名商品' }}</div>
                  <div class="muted">
                    <span v-if="row.skuCode">SKU {{ row.skuCode }}</span>
                    <span v-if="row.productId">商品 {{ row.productId }}</span>
                  </div>
                </div>
              </div>
            </template>
          </ElTableColumn>
          <ElTableColumn label="销量（件）" prop="quantity" width="110" align="right" sortable />
          <ElTableColumn label="涉及订单" prop="orders" width="100" align="right" />
          <template #empty><ElEmpty description="暂无数据" :image-size="60" /></template>
        </ElTable>
      </ElCard>
    </div>
  </div>
</template>

<script setup lang="ts">
  import { fetchOrderReport, type OrderReport, type ReportBucket } from '@/api/order'
  import { ApiError } from '@/utils/http'
  import { money } from '../order-format'

  defineOptions({ name: 'OrderReport' })

  const day = (date: Date) =>
    `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
  const lastDays = (n: number): [string, string] => {
    const end = new Date()
    const start = new Date()
    start.setDate(end.getDate() - (n - 1))
    return [day(start), day(end)]
  }
  const shortcuts = [
    { text: '近7天', value: () => lastDays(7).map((d) => new Date(`${d}T00:00:00`)) },
    { text: '近30天', value: () => lastDays(30).map((d) => new Date(`${d}T00:00:00`)) },
    { text: '近90天', value: () => lastDays(90).map((d) => new Date(`${d}T00:00:00`)) }
  ]

  const emptyOptions: OrderReport['options'] = { shops: [], platforms: [] }
  const filters = reactive({ shopSid: '', platform: '' })
  const dateRange = ref<[string, string]>(lastDays(30))
  const report = ref<OrderReport | null>(null)
  const options = ref(emptyOptions)
  const loading = ref(false)
  const loadError = ref('')
  let activeRequest: AbortController | undefined

  async function load() {
    activeRequest?.abort()
    const controller = new AbortController()
    activeRequest = controller
    loading.value = true
    loadError.value = ''
    try {
      const data = await fetchOrderReport(
        {
          shopSid: filters.shopSid,
          platform: filters.platform,
          createdFrom: dateRange.value[0],
          createdTo: dateRange.value[1]
        },
        controller.signal
      )
      if (controller.signal.aborted) return
      report.value = data
      options.value = data.options
    } catch (error) {
      if (controller.signal.aborted) return
      report.value = null
      loadError.value =
        error instanceof ApiError && error.status === 403
          ? '无权查看所选店铺，请调整筛选。'
          : error instanceof Error
            ? error.message
            : '无法加载订单报表，请稍后重试。'
    } finally {
      if (activeRequest === controller) loading.value = false
    }
  }

  const search = () => void load()

  const percent = (part: number, total: number) =>
    total ? Math.round((part / total) * 1000) / 10 : 0
  const kpis = computed(() => {
    const s = report.value?.summary
    const orders = s?.orders ?? 0
    return [
      { label: '订单数', value: orders, hint: '所选创建日期内' },
      {
        label: '商品件数',
        value: s?.itemQuantity ?? 0,
        hint: `平均每单 ${orders ? ((s?.itemQuantity ?? 0) / orders).toFixed(1) : '0'} 件`
      },
      {
        label: '已付款',
        value: `${percent(s?.paid ?? 0, orders)}%`,
        hint: `${s?.paid ?? 0} / ${orders} 单`
      },
      {
        label: '已出库',
        value: `${percent(s?.dispatched ?? 0, orders)}%`,
        hint: `${s?.dispatched ?? 0} / ${orders} 单`
      }
    ]
  })

  // 趋势：缺失的日期补 0，避免折线跨过无订单的日子
  const metric = ref<'orders' | 'itemQuantity'>('orders')
  const trendDays = computed(() => {
    const daily = report.value?.daily ?? []
    if (!daily.length) return []
    const days: string[] = []
    const end = new Date(`${daily[daily.length - 1].key}T00:00:00`)
    for (let d = new Date(`${daily[0].key}T00:00:00`); d <= end; d.setDate(d.getDate() + 1))
      days.push(day(d))
    return days
  })
  const trendData = computed(() => {
    const byDay = new Map((report.value?.daily ?? []).map((b) => [b.key, b[metric.value]]))
    return trendDays.value.map((d) => byDay.get(d) ?? 0)
  })

  // 维度表：店铺 / 平台 / 订单状态 / 物流渠道
  const dimensions = [
    { key: 'shops', label: '店铺' },
    { key: 'platforms', label: '平台' },
    { key: 'statuses', label: '订单状态' },
    { key: 'logistics', label: '物流渠道' }
  ] as const
  const dimension = ref<(typeof dimensions)[number]['key']>('shops')
  const currentDimension = computed(() => dimensions.find((d) => d.key === dimension.value)!)
  const rows = computed<ReportBucket[]>(() => report.value?.[dimension.value] ?? [])
  const share = (orders: number) =>
    Math.min(100, percent(orders, report.value?.summary.orders ?? 0))

  onMounted(() => void load())
  onUnmounted(() => activeRequest?.abort())
</script>

<style scoped>
  .search-card :deep(.el-card__body) {
    padding: 8px 12px;
  }

  .search-form {
    display: flex;
    flex-wrap: wrap;
    gap: 4px 10px;
  }

  .search-form :deep(.el-form-item) {
    margin-block: 0;
    margin-inline: 0;
  }

  .search-form :deep(.el-form-item__label) {
    height: auto;
    padding: 0;
    margin-block-end: 2px;
    font-size: 12px;
    line-height: 16px;
    color: var(--el-text-color-secondary);
  }

  .search-form :deep(.search-actions) {
    align-self: flex-end;
  }

  .report {
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .kpis {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
    gap: 12px;
  }

  .kpi--amount {
    grid-column: span 2;
  }

  .kpi__label {
    font-size: 13px;
    color: var(--el-text-color-secondary);
  }

  .kpi__value {
    margin-block: 4px;
    font-size: 26px;
    font-weight: 600;
    color: var(--el-text-color-primary);
  }

  .kpi__hint,
  .muted {
    font-size: 12px;
    color: var(--el-text-color-secondary);
  }

  .amounts {
    display: flex;
    flex-direction: column;
    gap: 4px;
    margin-top: 6px;
  }

  .amounts__row {
    display: flex;
    flex-wrap: wrap;
    gap: 4px 12px;
    align-items: baseline;
  }

  .amounts__row strong {
    font-size: 18px;
  }

  .card-head {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .report-table {
    --el-table-header-bg-color: var(--el-fill-color-light);
  }

  .product {
    display: flex;
    gap: 10px;
    align-items: center;
  }

  .product__image {
    flex: none;
    width: 44px;
    height: 44px;
    overflow: hidden;
    background: var(--el-fill-color);
    border-radius: 6px;
  }

  .product__image-empty {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 100%;
    height: 100%;
    color: var(--el-text-color-placeholder);
  }

  .product__body {
    display: flex;
    flex-direction: column;
    min-width: 0;
  }

  .product__name {
    display: -webkit-box;
    overflow: hidden;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
  }

  .product__body .muted {
    display: flex;
    gap: 12px;
  }
</style>
