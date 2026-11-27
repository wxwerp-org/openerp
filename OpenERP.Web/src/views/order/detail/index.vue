<!-- 订单详情：摘要、商品明细、基本信息、金额与支付、物流、状态与时间线；对接 `/api/orders/detail`。 -->
<template>
  <div class="page-stack">
    <AppNotice v-if="loadError" tone="error">
      {{ loadError }}
      <template #action><ElButton link @click="load()">重试</ElButton></template>
    </AppNotice>

    <ElCard v-if="order" class="head-card" shadow="never">
      <div class="head">
        <div class="head__main">
          <div class="head__title">
            <ElButton size="small" @click="goBack">
              <ArtSvgIcon icon="ri:arrow-left-line" />返回订单列表
            </ElButton>
            <h2>{{ primaryNumber }}</h2>
            <ElButton size="small" link type="primary" @click="copy(primaryNumber)">复制</ElButton>
          </div>
          <div class="head__tags">
            <ElTag effect="plain">{{ order.platform }}</ElTag>
            <ElTag v-for="(po, i) in order.platformOrders" :key="i" type="info">{{
              po.status || '—'
            }}</ElTag>
            <span class="muted">{{ order.shopName || `店铺 #${order.sid}` }}</span>
          </div>
        </div>
        <ElButton type="danger" plain size="small" :loading="deleting" @click="remove">
          删除订单
        </ElButton>
      </div>
    </ElCard>

    <div v-if="order" class="page-grid" style="--cols: 4">
      <AppStat
        label="订单金额"
        :value="order.totalAmount != null ? money(order.totalAmount, order.totalCurrency) : '—'"
      />
      <AppStat
        label="商品"
        :value="`${order.itemQuantity} 件`"
        :hint="`${order.lineCount} 种商品`"
      />
      <AppStat label="买家" :value="order.buyer || '—'" />
      <AppStat label="物流渠道" :value="order.logistics || '—'" />
    </div>

    <div v-if="order" class="page-row detail-body">
      <div class="page-stack detail-main">
        <ElCard shadow="never">
          <template #header>
            <span class="card-title">商品明细（{{ order.lines.length }}）</span>
          </template>
          <ElTable :data="order.lines" class="detail-table">
            <ElTableColumn label="商品" min-width="360">
              <template #default="{ row }">
                <div class="product">
                  <ElImage
                    class="product__image"
                    :src="row.imageUrl || undefined"
                    :preview-src-list="row.imageUrl ? [row.imageUrl] : []"
                    preview-teleported
                    fit="cover"
                    referrerpolicy="no-referrer"
                  >
                    <template #error>
                      <div class="product__image-empty"><ArtSvgIcon icon="ri:image-line" /></div>
                    </template>
                  </ElImage>
                  <div class="stack product__body">
                    <AppClamp class="product__name">{{ row.name || '未命名商品' }}</AppClamp>
                    <div v-if="specsOf(row.specs).length" class="chips">
                      <span v-for="(spec, i) in specsOf(row.specs)" :key="i" class="chip">
                        <em v-if="spec.label">{{ spec.label }}</em>{{ spec.value }}
                      </span>
                    </div>
                    <div class="muted">
                      <span v-if="row.skuCode">SKU {{ row.skuCode }}</span>
                      <span v-if="row.productId">商品 {{ row.productId }}</span>
                    </div>
                    <!-- 物流与状态等补充信息：行内排列，超过三行折叠；单价类费用放价格列，原始内容放「更多信息」 -->
                    <AppClamp v-if="row.logistics || inlineDetails(row as OrderLine).length">
                      <span v-if="row.logistics" class="fact">
                        <em>物流</em>{{ row.logistics }}
                      </span>
                      <span
                        v-for="(item, i) in inlineDetails(row as OrderLine)"
                        :key="i"
                        class="fact"
                      >
                        <em>{{ item.label }}</em>{{ item.value }}
                      </span>
                    </AppClamp>
                    <template v-if="detailsOf(row.details, 'more').length">
                      <ElButton
                        link
                        type="primary"
                        size="small"
                        class="more-toggle"
                        @click="toggleMore(rowKey(row as OrderLine))"
                      >
                        {{ openMore.has(rowKey(row as OrderLine)) ? '收起更多信息' : '更多信息' }}
                      </ElButton>
                      <dl v-if="openMore.has(rowKey(row as OrderLine))" class="facts more">
                        <template v-for="(item, i) in detailsOf(row.details, 'more')" :key="i">
                          <dt>{{ item.label }}</dt>
                          <dd>{{ item.value }}</dd>
                        </template>
                      </dl>
                    </template>
                  </div>
                </div>
              </template>
            </ElTableColumn>
            <ElTableColumn label="单价" width="120" align="right">
              <template #default="{ row }">
                <div class="stack price">
                  <span>{{
                    row.unitPrice != null ? money(row.unitPrice, row.currency) : '—'
                  }}</span>
                  <span v-for="(item, i) in detailsOf(row.details, 'price')" :key="i" class="muted">
                    {{ item.label }}：{{ item.value }}
                  </span>
                </div>
              </template>
            </ElTableColumn>
            <ElTableColumn label="数量" width="80" align="right">
              <template #default="{ row }">×{{ row.quantity }}</template>
            </ElTableColumn>
            <ElTableColumn label="小计" width="130" align="right">
              <template #default="{ row }">
                <strong>{{
                  row.unitPrice != null ? money(row.unitPrice * row.quantity, row.currency) : '—'
                }}</strong>
              </template>
            </ElTableColumn>
          </ElTable>
        </ElCard>
      </div>

      <div class="page-stack detail-side">
        <ElCard shadow="never">
          <template #header><span class="card-title">基本信息</span></template>
          <dl class="facts">
            <dt>店铺</dt>
            <dd>{{ order.shopName || `店铺 #${order.sid}` }}</dd>
            <dt>平台</dt>
            <dd>{{ order.platform }}</dd>
            <dt>平台订单号</dt>
            <dd>
              <div v-for="(po, i) in order.platformOrders" :key="i">{{ po.number }}</div>
            </dd>
            <dt>买家</dt>
            <dd>{{ order.buyer || '—' }}</dd>
            <template v-for="(fact, i) in factsOf(order, 'buyer')" :key="i">
              <dt>{{ fact.label }}</dt>
              <dd>
                <AppClamp>{{ fact.value }}</AppClamp>
              </dd>
            </template>
          </dl>
        </ElCard>

        <ElCard v-for="section in sections" :key="section.group" shadow="never">
          <template #header
            ><span class="card-title">{{ section.title }}</span></template
          >
          <dl class="facts">
            <template v-if="section.group === 'payment'">
              <dt>订单金额</dt>
              <dd>
                {{
                  order.totalAmount != null ? money(order.totalAmount, order.totalCurrency) : '—'
                }}
              </dd>
            </template>
            <template v-if="section.group === 'logistics'">
              <dt>物流渠道</dt>
              <dd>{{ order.logistics || '—' }}</dd>
            </template>
            <template v-for="(fact, i) in section.facts" :key="i">
              <dt>{{ fact.label }}</dt>
              <dd>
                <AppClamp>{{ fact.value }}</AppClamp>
              </dd>
            </template>
          </dl>
        </ElCard>

        <!-- 其他、原始信息默认折叠，点击后才展示 -->
        <ElCard v-if="moreFacts.length" shadow="never" class="more-card">
          <ElCollapse>
            <ElCollapseItem title="更多信息" name="more">
              <dl class="facts">
                <template v-for="(fact, i) in moreFacts" :key="i">
                  <dt>{{ fact.label }}</dt>
                  <dd>
                    <AppClamp>{{ fact.value }}</AppClamp>
                  </dd>
                </template>
              </dl>
            </ElCollapseItem>
          </ElCollapse>
        </ElCard>

        <ElCard shadow="never">
          <template #header><span class="card-title">时间线</span></template>
          <ElTimeline class="timeline">
            <ElTimelineItem
              v-for="step in timeline"
              :key="step.label"
              :timestamp="step.time"
              :type="step.done ? 'primary' : 'info'"
              :hollow="!step.done"
              placement="top"
            >
              {{ step.label }}
            </ElTimelineItem>
          </ElTimeline>
        </ElCard>
      </div>
    </div>

    <ElCard v-else-if="loading" shadow="never">
      <ElSkeleton :rows="8" animated />
    </ElCard>
  </div>
</template>

<script setup lang="ts">
  import { ElMessage, ElMessageBox } from 'element-plus'
  import { deleteOrders, fetchOrder, type Order, type OrderLine } from '@/api/order'
  import { detailsOf, factsOf, money, sourceTime, specsOf } from '../order-format'

  defineOptions({ name: 'OrderDetail' })

  const route = useRoute()
  const router = useRouter()

  const order = ref<Order | null>(null)
  const loading = ref(false)
  const loadError = ref('')
  const deleting = ref(false)
  let activeRequest: AbortController | undefined

  const orderId = computed(() => decodeURIComponent(String(route.params.id ?? '')))
  const primaryNumber = computed(() => order.value?.platformOrders[0]?.number || orderId.value)

  // 金额与支付 / 物流 / 状态 / 其他 四类补充信息，没有内容的分组不显示
  const sections = computed(() =>
    order.value
      ? [
          { group: 'payment', title: '金额与支付' },
          { group: 'logistics', title: '物流' },
          { group: 'status', title: '状态' }
        ]
          .map((section) => ({ ...section, facts: factsOf(order.value!, section.group) }))
          .filter(
            (section) =>
              section.facts.length > 0 || ['payment', 'logistics'].includes(section.group)
          )
      : []
  )

  // 订单自身的其他与原始信息：默认折叠在「更多信息」里
  const moreFacts = computed(() =>
    order.value
      ? [...factsOf(order.value, 'other', false), ...factsOf(order.value, 'more', false)]
      : []
  )

  // 商品行：行内展示的补充信息（价格类放价格列，原始内容放「更多信息」）
  const inlineDetails = (line: OrderLine) =>
    line.details.filter((item) => !['price', 'more'].includes(item.group))
  const rowKey = (line: OrderLine) => order.value?.lines.indexOf(line) ?? -1
  const openMore = ref(new Set<number>())
  function toggleMore(key: number) {
    const next = new Set(openMore.value)
    if (!next.delete(key)) next.add(key)
    openMore.value = next
  }

  const timeline = computed(() => {
    const current = order.value
    if (!current) return []
    const steps = [
      { label: '订单创建', time: current.created },
      { label: '买家付款', time: current.paid },
      { label: '订单出库', time: current.dispatched },
      ...factsOf(current, 'time').map((fact) => ({ label: fact.label, time: fact.value }))
    ]
    return steps.map((step) => ({
      label: step.label,
      time: sourceTime(step.time),
      done: Boolean(step.time)
    }))
  })

  async function load() {
    activeRequest?.abort()
    const controller = new AbortController()
    activeRequest = controller
    loading.value = true
    loadError.value = ''
    try {
      const data = await fetchOrder(orderId.value, controller.signal)
      if (!controller.signal.aborted) order.value = data
    } catch (error) {
      if (controller.signal.aborted) return
      order.value = null
      loadError.value = error instanceof Error ? error.message : '无法加载订单详情，请稍后重试。'
    } finally {
      if (activeRequest === controller) loading.value = false
    }
  }

  const goBack = () => router.push('/order/list')

  async function copy(text: string) {
    try {
      await navigator.clipboard.writeText(text)
      ElMessage.success('已复制订单号')
    } catch {
      ElMessage.error('复制失败，请检查浏览器的剪贴板权限。')
    }
  }

  async function remove() {
    if (!order.value || deleting.value) return
    try {
      await ElMessageBox.confirm(
        '确定删除该订单吗？订单同步到本地的商品和平台单信息会一并移除。',
        '删除订单',
        { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' }
      )
    } catch {
      return
    }
    deleting.value = true
    try {
      await deleteOrders([order.value.id])
      ElMessage.success('订单已删除')
      await goBack()
    } catch (error) {
      ElMessage.error(error instanceof Error ? error.message : '无法删除订单，请稍后重试。')
    } finally {
      deleting.value = false
    }
  }

  watch(orderId, () => void load())
  onMounted(() => void load())
  onUnmounted(() => activeRequest?.abort())
</script>

<style scoped>
  .muted {
    display: flex;
    flex-wrap: wrap;
    gap: 0 12px;
    font-size: 13px;
    color: var(--el-text-color-secondary);
  }

  .stack {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }

  /* 顶部标题卡 */
  .head {
    display: flex;
    gap: 16px;
    align-items: flex-start;
    justify-content: space-between;
  }

  .head__main {
    display: flex;
    flex-direction: column;
    gap: 10px;
    min-width: 0;
  }

  .head__title {
    display: flex;
    flex-wrap: wrap;
    gap: 10px;
    align-items: center;
  }

  .head__title h2 {
    padding: 0;
    margin-block: 0;
    font-size: 18px;
    font-weight: 600;
    color: var(--el-text-color-primary);
  }

  .head__tags {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    align-items: center;
  }

  /* 主体：左侧商品明细，右侧信息卡片 */
  .detail-body {
    align-items: flex-start;
  }

  .detail-main {
    flex: 1;
    min-width: 0;
  }

  .detail-side {
    flex: none;
    width: 360px;
  }

  .card-title {
    font-size: 14px;
    font-weight: 600;
  }

  .detail-table {
    font-size: 13px;
  }

  /* 表格内容一律靠上对齐，不垂直居中 */
  .detail-table :deep(td.el-table__cell) {
    padding-block: 12px;
    vertical-align: top;
  }

  .price {
    align-items: flex-end;
  }

  .more-toggle {
    align-self: flex-start;
  }

  .facts.more {
    padding: 8px 12px;
    background: var(--el-fill-color-lighter);
    border-radius: 6px;
  }

  .more-card :deep(.el-card__body) {
    padding-block: 0;
  }

  .fact {
    padding-inline-end: 16px;
    font-size: 13px;
    line-height: 22px;
    color: var(--el-text-color-primary);
    white-space: nowrap;
  }

  .fact em {
    padding-inline-end: 4px;
    font-style: normal;
    color: var(--el-text-color-secondary);
  }

  .product {
    display: flex;
    gap: 12px;
    align-items: flex-start;
  }

  .product__image {
    flex: none;
    width: 72px;
    height: 72px;
    overflow: hidden;
    background: var(--el-fill-color);
    border-radius: 8px;
  }

  .product__image-empty {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 100%;
    height: 100%;
    font-size: 24px;
    color: var(--el-text-color-placeholder);
  }

  .product__body {
    min-width: 0;
  }

  .product__name {
    line-height: 20px;
    color: var(--el-text-color-primary);
  }

  .chips {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }

  .chip {
    padding: 0 8px;
    font-size: 13px;
    color: var(--el-color-primary);
    background: var(--el-color-primary-light-9);
    border-radius: 4px;
  }

  .chip em {
    margin-inline-end: 4px;
    font-style: normal;
    color: var(--el-text-color-secondary);
  }

  /* 名称 / 取值成对的信息列表 */
  .facts {
    display: grid;
    grid-template-columns: auto 1fr;
    gap: 8px 16px;
    margin-block: 0;
    font-size: 13px;
  }

  .facts dt {
    color: var(--el-text-color-secondary);
    white-space: nowrap;
  }

  .facts dd {
    min-width: 0;
    margin-inline: 0;
    overflow-wrap: anywhere;
    color: var(--el-text-color-primary);
  }

  .timeline {
    padding-inline-start: 4px;
    font-size: 13px;
  }

  @media (width <= 1100px) {
    .detail-body {
      flex-direction: column;
    }

    .detail-side {
      width: 100%;
    }
  }
</style>
