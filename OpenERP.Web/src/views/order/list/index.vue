<!-- 订单列表：筛选 + 列表 + 分页，对接 OpenERP.Api `/api/orders`。 -->
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
        <ElFormItem label="店铺">
          <ElSelect v-model="filters.shopSid" style="width: 130px">
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
          <ElSelect v-model="filters.platform" style="width: 110px">
            <ElOption label="全部平台" value="" />
            <ElOption
              v-for="platform in options.platforms"
              :key="platform"
              :label="platform"
              :value="platform"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="订单状态">
          <ElSelect v-model="filters.status" style="width: 110px">
            <ElOption label="全部状态" value="" />
            <ElOption
              v-for="status in options.statuses"
              :key="status"
              :label="status"
              :value="status"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="平台订单号">
          <ElInput
            v-model="filters.number"
            clearable
            placeholder="平台订单号"
            style="width: 150px"
          />
        </ElFormItem>
        <ElFormItem label="创建日期">
          <ElDatePicker
            v-model="dateRange"
            type="daterange"
            value-format="YYYY-MM-DD"
            range-separator="至"
            start-placeholder="开始日期"
            end-placeholder="结束日期"
            style="width: 220px"
          />
        </ElFormItem>
        <ElFormItem class="search-actions">
          <ElButton type="primary" native-type="submit">搜索</ElButton>
        </ElFormItem>
      </ElForm>
    </ElCard>

    <AppNotice v-if="loadError" tone="error">
      {{ loadError }}
      <template #action><ElButton link @click="load()">重试</ElButton></template>
    </AppNotice>

    <ElCard class="art-table-card" shadow="never">
      <div class="order-toolbar">
        <ArtTableHeader :loading="loading" @refresh="load()">
          <template #left>
            <ElButton size="small" @click="openInNewPage(router, '/order/sync')">
              同步历史订单
            </ElButton>
          </template>
        </ArtTableHeader>
      </div>

      <div class="order-table-wrap">
        <ElTable
          ref="tableRef"
          v-loading="loading"
          :data="result?.items ?? []"
          row-key="id"
          class="order-table"
          @selection-change="onSelectionChange"
        >
          <ElTableColumn type="selection" width="44" reserve-selection fixed="left" />
          <ElTableColumn label="订单" min-width="230">
            <template #default="{ row }">
              <div class="stack">
                <div class="inline">
                  <strong>{{ row.shopName || `店铺 #${row.sid}` }}</strong>
                  <ElTag size="small" effect="plain">{{ row.platform }}</ElTag>
                </div>
                <div
                  v-for="(platformOrder, index) in row.platformOrders"
                  :key="index"
                  class="inline"
                >
                  <component
                    :is="platformOrder.matched && highlight ? 'mark' : 'span'"
                    :class="platformOrder.matched && highlight ? 'matched' : ''"
                  >
                    {{ platformOrder.number || '—' }}
                  </component>
                </div>
                <div v-if="row.buyer" class="muted">买家 {{ row.buyer }}</div>
                <div v-for="(fact, i) in factsOf(row as Order, 'buyer')" :key="i" class="muted">
                  {{ fact.label }} {{ fact.value }}
                </div>
              </div>
            </template>
          </ElTableColumn>
          <ElTableColumn label="商品" min-width="440">
            <template #default="{ row }">
              <ul v-if="row.lines.length" class="line-list">
                <li
                  v-for="(line, index) in visibleLines(row as Order)"
                  :key="index"
                  class="line"
                  data-testid="order-line"
                >
                  <ElImage
                    class="line__image"
                    :src="line.imageUrl || undefined"
                    :preview-src-list="line.imageUrl ? [line.imageUrl] : []"
                    preview-teleported
                    lazy
                    fit="cover"
                    referrerpolicy="no-referrer"
                  >
                    <template #error>
                      <div class="line__image-empty"><ArtSvgIcon icon="ri:image-line" /></div>
                    </template>
                  </ElImage>
                  <div class="stack line__body">
                    <ElTooltip
                      :content="line.name || '未命名商品'"
                      placement="top-start"
                      :show-after="400"
                    >
                      <div class="line__name">{{ line.name || '未命名商品' }}</div>
                    </ElTooltip>
                    <div v-if="specsOf(line.specs).length" class="chips">
                      <span v-for="(spec, i) in specsOf(line.specs)" :key="i" class="chip">
                        <em v-if="spec.label">{{ spec.label }}</em>{{ spec.value }}
                      </span>
                    </div>
                    <div class="muted">
                      <span v-if="line.skuCode">SKU {{ line.skuCode }}</span>
                    </div>
                  </div>
                  <div class="line__qty">
                    <span>×{{ line.quantity }}</span>
                    <span v-if="line.unitPrice != null" class="muted">{{
                      money(line.unitPrice, line.currency)
                    }}</span>
                  </div>
                </li>
              </ul>
              <span v-else class="muted">—</span>
              <ElButton
                v-if="row.lines.length > COLLAPSED_LINES"
                link
                type="primary"
                class="line-toggle"
                @click="toggleLines(row.id)"
              >
                {{ expanded.has(row.id) ? '收起商品' : `展开全部 ${row.lines.length} 个商品` }}
              </ElButton>
            </template>
          </ElTableColumn>
          <ElTableColumn label="金额与支付" min-width="190">
            <template #default="{ row }">
              <div class="stack">
                <strong v-if="row.totalAmount != null">{{
                  money(row.totalAmount, row.totalCurrency)
                }}</strong>
                <span v-else class="muted">—</span>
                <span class="muted">共 {{ row.itemQuantity }} 件 · {{ row.lineCount }} 种商品</span>
                <dl v-if="paymentFacts(row as Order).length" class="facts">
                  <template v-for="(fact, i) in paymentFacts(row as Order)" :key="i">
                    <dt>{{ fact.label }}</dt>
                    <dd>{{ fact.value }}</dd>
                  </template>
                </dl>
              </div>
            </template>
          </ElTableColumn>
          <ElTableColumn label="物流" min-width="220">
            <template #default="{ row }">
              <div class="stack">
                <span v-if="row.logistics">{{ row.logistics }}</span>
                <span v-else class="muted">—</span>
                <dl v-if="factsOf(row as Order, 'logistics').length" class="facts">
                  <template v-for="(fact, i) in factsOf(row as Order, 'logistics')" :key="i">
                    <dt>{{ fact.label }}</dt>
                    <dd>{{ fact.value }}</dd>
                  </template>
                </dl>
              </div>
            </template>
          </ElTableColumn>
          <ElTableColumn label="状态" min-width="190">
            <template #default="{ row }">
              <div class="stack">
                <ElTag
                  v-for="(platformOrder, index) in row.platformOrders"
                  :key="index"
                  size="small"
                  type="info"
                >
                  {{ platformOrder.status || '—' }}
                </ElTag>
                <dl v-if="factsOf(row as Order, 'status').length" class="facts">
                  <template v-for="(fact, i) in factsOf(row as Order, 'status')" :key="i">
                    <dt>{{ fact.label }}</dt>
                    <dd>{{ fact.value }}</dd>
                  </template>
                </dl>
                <span
                  v-if="!row.platformOrders.length && !factsOf(row as Order, 'status').length"
                  class="muted"
                  >—</span
                >
              </div>
            </template>
          </ElTableColumn>
          <ElTableColumn label="时间" width="200">
            <template #default="{ row }">
              <dl class="facts">
                <dt>创建</dt>
                <dd>{{ sourceTime(row.created) }}</dd>
                <dt>付款</dt>
                <dd>{{ sourceTime(row.paid) }}</dd>
                <dt>出库</dt>
                <dd>{{ sourceTime(row.dispatched) }}</dd>
                <template v-for="(fact, i) in factsOf(row as Order, 'time')" :key="i">
                  <dt>{{ fact.label }}</dt>
                  <dd>{{ fact.value }}</dd>
                </template>
              </dl>
            </template>
          </ElTableColumn>
          <ElTableColumn label="操作" width="110" fixed="right">
            <template #default="{ row }">
              <ElButton link type="primary" @click="showDetail(row as Order)">详情</ElButton>
              <ElButton link type="danger" @click="removeOrders([row as Order])">删除</ElButton>
            </template>
          </ElTableColumn>
          <template #empty>
            <ElEmpty :description="isFiltered ? '没有符合条件的订单' : '暂无订单'" />
          </template>
        </ElTable>
      </div>

      <div class="order-pager">
        <ElPagination
          background
          layout="total, sizes, prev, pager, next, jumper"
          :total="result?.total ?? 0"
          :page-size="pageSize"
          :current-page="currentPage"
          :page-sizes="[20, 50, 100]"
          :disabled="!result"
          @size-change="changePageSize"
          @current-change="(page: number) => load(page)"
        />
      </div>
    </ElCard>

    <!-- 勾选后悬浮在页面底部的批量操作条 -->
    <Transition name="bulk-bar">
      <div v-if="selected.length" class="bulk-bar" data-testid="bulk-bar">
        <span>
          已选择 <strong>{{ selected.length }}</strong> 个订单
        </span>
        <ElButton size="small" @click="copyNumbers">复制订单号</ElButton>
        <ElButton
          size="small"
          type="primary"
          :loading="printing"
          data-testid="print-labels"
          @click="printLabels(selected)"
        >
          打印面单
        </ElButton>
        <ElButton size="small" type="danger" :loading="deleting" @click="removeOrders(selected)">
          批量删除
        </ElButton>
        <ElButton size="small" link @click="clearSelection">取消选择</ElButton>
      </div>
    </Transition>
  </div>
</template>

<script setup lang="ts">
  import { ElMessage, ElMessageBox } from 'element-plus'
  import {
    deleteOrders,
    fetchOrders,
    printOrderLabels,
    type Order,
    type OrderPage
  } from '@/api/order'
  import { ApiError } from '@/utils/http'
  import { openInNewPage } from '@/utils/navigation/open-page'
  import { factsOf, money, sourceTime, specsOf } from '../order-format'
  import { useErpHub } from '@/hooks/business/useErpHub'

  defineOptions({ name: 'OrderList' })

  const router = useRouter()
  // 订单同步完成后服务端推送变更，列表静默刷新
  const hub = useErpHub({ shopSync: false, onOrdersChanged: () => void load(currentPage.value) })

  const emptyOptions: OrderPage['options'] = { shops: [], platforms: [], statuses: [] }
  const emptyFilters = { shopSid: '', platform: '', status: '', number: '' }

  const result = ref<OrderPage | null>(null)
  const options = ref(emptyOptions)
  const loading = ref(false)
  const loadError = ref('')
  const pageSize = ref(20)
  const currentPage = ref(1)
  const filters = reactive({ ...emptyFilters })
  const dateRange = ref<[string, string] | null>(null)
  const applied = ref({ ...emptyFilters, createdFrom: '', createdTo: '' })
  let activeRequest: AbortController | undefined

  const isFiltered = computed(() => Object.values(applied.value).some(Boolean))
  // 仅当按订单号或状态筛选时，高亮命中的平台订单
  const highlight = computed(() => Boolean(applied.value.number || applied.value.status))

  // 订单商品默认只展示前几行，其余按订单展开
  const COLLAPSED_LINES = 3
  const expanded = ref(new Set<string>())
  const visibleLines = (order: Order) =>
    expanded.value.has(order.id) ? order.lines : order.lines.slice(0, COLLAPSED_LINES)
  function toggleLines(id: string) {
    const next = new Set(expanded.value)
    if (!next.delete(id)) next.add(id)
    expanded.value = next
  }

  // 金额与支付列：支付类信息加上单价后的费用（如物流费用）
  const paymentFacts = (order: Order) => [...factsOf(order, 'price'), ...factsOf(order, 'payment')]

  // 勾选：跨页保留（reserve-selection），悬浮条显示已选数量
  const tableRef = ref<{ clearSelection: () => void } | null>(null)
  const selected = ref<Order[]>([])
  const deleting = ref(false)
  const onSelectionChange = (rows: Order[]) => {
    selected.value = rows
  }
  const clearSelection = () => tableRef.value?.clearSelection()

  // 打印面单：后端按平台取得 PDF，前端在新窗口打开由浏览器预览和打印
  const printing = ref(false)
  async function printLabels(orders: Order[]) {
    if (printing.value || orders.length === 0) return
    // 先同步打开窗口，避免异步请求后被浏览器拦截弹窗
    const win = window.open('', '_blank')
    printing.value = true
    try {
      const { documents, failures } = await printOrderLabels(orders.map((order) => order.id))
      documents.forEach((document, index) => {
        const bytes = Uint8Array.from(atob(document.content), (char) => char.charCodeAt(0))
        const url = URL.createObjectURL(new Blob([bytes], { type: document.contentType }))
        if (index === 0 && win) win.location.href = url
        else window.open(url, '_blank')
        setTimeout(() => URL.revokeObjectURL(url), 60_000)
      })
      if (documents.length === 0) win?.close()
      if (failures.length) {
        const text = failures.map((f) => `${f.platformOrderNumber}：${f.reason}`).join('\n')
        ElMessageBox.alert(text, `${failures.length} 个订单未取得面单`, { type: 'warning' })
      } else {
        ElMessage.success(`已获取 ${documents.length} 份面单`)
      }
    } catch (error) {
      win?.close()
      ElMessage.error(error instanceof Error ? error.message : '无法获取面单，请稍后重试。')
    } finally {
      printing.value = false
    }
  }

  async function copyNumbers() {
    const numbers = selected.value.flatMap((order) => order.platformOrders.map((po) => po.number))
    try {
      await navigator.clipboard.writeText(numbers.join('\n'))
      ElMessage.success(`已复制 ${numbers.length} 个平台订单号`)
    } catch {
      ElMessage.error('复制失败，请检查浏览器的剪贴板权限。')
    }
  }

  const showDetail = (order: Order) =>
    openInNewPage(router, `/order/detail/${encodeURIComponent(order.id)}`)

  async function removeOrders(orders: Order[]) {
    if (deleting.value || orders.length === 0) return
    try {
      await ElMessageBox.confirm(
        `确定删除选中的 ${orders.length} 个订单吗？订单同步到本地的商品和平台单信息会一并移除。`,
        '删除订单',
        { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' }
      )
    } catch {
      return
    }
    deleting.value = true
    try {
      const { deleted } = await deleteOrders(orders.map((order) => order.id))
      ElMessage.success(`已删除 ${deleted} 个订单`)
      clearSelection()
      await load(currentPage.value)
    } catch (error) {
      ElMessage.error(error instanceof Error ? error.message : '无法删除订单，请稍后重试。')
    } finally {
      deleting.value = false
    }
  }

  async function load(page = currentPage.value) {
    activeRequest?.abort()
    const controller = new AbortController()
    activeRequest = controller
    loading.value = true
    loadError.value = ''
    currentPage.value = page
    try {
      const data = await fetchOrders(
        { page, pageSize: pageSize.value, ...applied.value },
        controller.signal
      )
      if (controller.signal.aborted) return
      result.value = data
      options.value = data.options
      currentPage.value = data.page
    } catch (error) {
      if (controller.signal.aborted) return
      result.value = null
      if (error instanceof ApiError && error.status === 403) {
        options.value = emptyOptions
        loadError.value = '无权查看所选店铺，请调整筛选。'
      } else {
        loadError.value = error instanceof Error ? error.message : '无法加载订单，请稍后重试。'
      }
    } finally {
      if (activeRequest === controller) loading.value = false
    }
  }

  function search() {
    applied.value = {
      ...filters,
      number: filters.number.trim(),
      createdFrom: dateRange.value?.[0] ?? '',
      createdTo: dateRange.value?.[1] ?? ''
    }
    load(1)
  }

  function changePageSize(size: number) {
    pageSize.value = size
    load(1)
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

<style scoped>
  /* 紧凑筛选区：小号控件，标签在上，卡片内边距与间距尽量收紧 */
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

  /*
   * 表格卡片按内容自然增高，随整个页面（#app-main）滚动，内部不出现滚动条。
   * 表头和分页条用 sticky 漂浮：sticky 要求祖先都不裁剪溢出，
   * 所以卡片、卡片体和 el-table 的 overflow 都放开；卡片内不留左右内边距。
   */
  .page-stack > .art-table-card {
    flex: none;
    overflow: visible;
  }

  .art-table-card :deep(.el-card__body) {
    height: auto;
    padding-inline: 0;
    overflow: visible;
  }

  .order-table,
  .order-table :deep(.el-table__inner-wrapper) {
    overflow: visible;
  }

  /* 表头漂浮在内容之上，带阴影与内容区分 */
  .order-table :deep(.el-table__header-wrapper) {
    position: sticky;
    inset-block-start: 0;
    z-index: 20;
    background: var(--el-bg-color);
    box-shadow: 0 2px 8px rgb(0 0 0 / 8%);
  }

  /* 工具栏与表格之间有分隔边框，图标按钮在栏内垂直居中 */
  .order-toolbar {
    padding: 8px 16px;
    border-block-end: 1px solid var(--el-border-color-lighter);
  }

  /* 表格内文字统一：同一字体、13px，层级只靠字重与颜色区分 */
  .order-table {
    --el-table-header-bg-color: var(--el-fill-color-light);
    --el-table-row-hover-bg-color: var(--el-fill-color-lighter);

    font-size: 13px;
    line-height: 20px;
    color: var(--el-text-color-regular);
  }

  .order-table :deep(th.el-table__cell) {
    font-size: 13px;
    font-weight: 600;
    color: var(--el-text-color-secondary);
  }

  .order-table :deep(td.el-table__cell) {
    padding-block: 12px;
    vertical-align: top;
  }

  .order-table :deep(.cell),
  .order-table :deep(.el-tag) {
    font-size: 13px;
  }

  .order-table strong {
    font-weight: 600;
    color: var(--el-text-color-primary);
  }

  .stack {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }

  .inline {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    align-items: center;
  }

  .muted {
    display: flex;
    flex-wrap: wrap;
    gap: 0 12px;
    color: var(--el-text-color-secondary);
  }

  .matched {
    padding: 0 4px;
    margin-left: -4px;
    background: var(--el-color-warning-light-8);
    border-radius: 3px;
  }

  /* 商品列：无外框，仅用分隔线区分行 */
  .line-list {
    display: flex;
    flex-direction: column;
    padding: 0;
    margin-block: 0;
    list-style: none;
  }

  .line {
    display: flex;
    gap: 12px;
    align-items: flex-start;
    padding-block: 8px;
    border-top: 1px solid var(--el-border-color-extra-light);
  }

  .line:first-child {
    padding-top: 0;
    border-top: 0;
  }

  .line:last-child {
    padding-bottom: 0;
  }

  .line__image {
    flex: none;
    width: 56px;
    height: 56px;
    overflow: hidden;
    background: var(--el-fill-color);
    border-radius: 6px;
  }

  .line__image-empty {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 100%;
    height: 100%;
    font-size: 20px;
    color: var(--el-text-color-placeholder);
  }

  .line__body {
    flex: 1;
    min-width: 0;
  }

  .line__name {
    display: -webkit-box;
    overflow: hidden;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    color: var(--el-text-color-primary);
  }

  .chips {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }

  .chip {
    padding: 0 8px;
    color: var(--el-color-primary);
    background: var(--el-color-primary-light-9);
    border-radius: 4px;
  }

  .chip em {
    margin-inline-end: 4px;
    font-style: normal;
    color: var(--el-text-color-secondary);
  }

  .line__qty {
    display: flex;
    flex: none;
    flex-direction: column;
    align-items: flex-end;
    min-width: 72px;
  }

  .line-toggle {
    padding-top: 8px;
  }

  /* 名称 / 取值成对的信息列表 */
  .facts {
    display: grid;
    grid-template-columns: auto 1fr;
    gap: 4px 10px;
    margin-block: 0;
  }

  .facts dt {
    color: var(--el-text-color-secondary);
    white-space: nowrap;
  }

  .facts dd {
    min-width: 0;
    margin-inline: 0;
    overflow-wrap: anywhere;
  }

  /* 底部悬浮的批量操作条 */
  .bulk-bar {
    position: fixed;
    inset-block-end: 24px;
    inset-inline-start: 50%;
    z-index: 2000;
    display: flex;
    gap: 12px;
    align-items: center;
    padding: 10px 20px;
    color: var(--el-text-color-primary);
    background: var(--el-bg-color-overlay);
    border: 1px solid var(--el-border-color-lighter);
    border-radius: 999px;
    box-shadow: var(--el-box-shadow);
    transform: translateX(-50%);
  }

  .bulk-bar strong {
    color: var(--el-color-primary);
  }

  .bulk-bar-enter-active,
  .bulk-bar-leave-active {
    transition:
      opacity 0.2s,
      transform 0.2s;
  }

  .bulk-bar-enter-from,
  .bulk-bar-leave-to {
    opacity: 0;
    transform: translate(-50%, 16px);
  }

  /* 分页条漂浮在视口底部：圆角、阴影，不占位置也不随内容滚走 */
  .order-pager {
    position: sticky;
    inset-block-end: 0;
    z-index: 20;
    display: flex;
    flex-shrink: 0;
    justify-content: flex-end;
    padding: 10px 16px;
    background: var(--el-bg-color);
    box-shadow: 0 -2px 8px rgb(0 0 0 / 8%);
  }
</style>
