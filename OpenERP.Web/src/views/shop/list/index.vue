<!-- 授权店铺：本地店铺目录（上游 ERP 同步而来）的授权状态、筛选与手动同步。 -->
<template>
  <div class="page-stack">
    <AppNotice v-if="configStatus === 'ready' && !configured && !showGuide" tone="warning">
      尚未绑定旺销王 ERP 密钥，无法同步店铺。
      <template v-if="isAdministrator" #action>
        <RouterLink to="/system/erp-binding">前往 API权限绑定</RouterLink>
      </template>
    </AppNotice>
    <AppNotice v-if="configError" tone="error">{{ configError }}</AppNotice>
    <AppNotice v-if="syncError" tone="error">{{ syncError }}</AppNotice>

    <!-- 没有任何店铺时的向导：绑定密钥 → 读取店铺 → 使用各项功能 -->
    <ElCard v-if="showGuide" shadow="never" class="guide-card">
      <div class="guide">
        <div class="guide__side">
          <div class="guide__badge">
            <ArtSvgIcon icon="ri:store-2-line" />
          </div>
          <h2 class="guide__title">授权店铺</h2>
          <p class="guide__desc">读取旺销王 ERP 中的全部店铺，统一查看各平台的授权状态。</p>
        </div>

        <div class="guide__main">
          <ElSteps :active="guideStep" finish-status="success" align-center>
            <ElStep title="绑定密钥" />
            <ElStep title="读取店铺" />
            <ElStep title="使用功能" />
          </ElSteps>

          <!-- 第 1 步：先绑定旺销王 ERP 密钥 -->
          <div v-if="!configured" class="guide__panel">
            <ArtSvgIcon icon="ri:key-2-line" class="guide__panel-icon" />
            <p class="guide__panel-title">第一步：绑定旺销王 ERP 密钥</p>
            <p class="guide__hint">
              填写 AccessToken 后，系统才能读取你的全部店铺，并通过 API 实现订单、同步等各项功能。
            </p>
            <div class="guide__actions">
              <ElButton v-if="isAdministrator" type="primary" size="large" @click="goSettings">
                前往绑定密钥
              </ElButton>
              <p v-else class="guide__hint">请联系管理员先在 API权限绑定 中绑定密钥。</p>
            </div>
          </div>

          <!-- 第 2 步：密钥已绑定，读取所有店铺 -->
          <div v-else class="guide__panel">
            <ArtSvgIcon icon="ri:cloud-download-line" class="guide__panel-icon" />
            <p class="guide__panel-title">第二步：读取全部店铺</p>
            <p class="guide__hint">
              密钥已绑定。同步后将读取店铺目录与各平台授权状态，之后即可使用订单、订单同步和店铺分配等功能。
            </p>
            <div class="guide__actions">
              <ElButton
                v-if="isAdministrator"
                type="primary"
                size="large"
                :disabled="syncDisabled"
                @click="syncNow"
              >
                {{ syncButtonLabel }}
              </ElButton>
              <p v-else class="guide__hint">请联系管理员同步店铺并分配给你。</p>
            </div>
          </div>

          <ElButton link type="primary" class="guide__link" @click="openAuthorization">
            还没有店铺？绑定新的店铺
          </ElButton>
        </div>
      </div>
    </ElCard>

    <!-- 平台标签页：从左到右，只显示平台标签，统计数量在下面的状态筛选上 -->
    <ElCard v-if="!showGuide" shadow="never" class="platform-tabs-card">
      <ElTabs :model-value="platform" @tab-change="(name) => selectPlatform(String(name))">
        <ElTabPane name="">
          <template #label><span class="platform-tab__all">全部</span></template>
        </ElTabPane>
        <ElTabPane v-for="item in platforms" :key="item.name" :name="item.name">
          <template #label>
            <PlatformTag :name="item.name" :label="item.displayName" />
          </template>
        </ElTabPane>
      </ElTabs>
    </ElCard>

    <!-- 授权状态筛选：不放进面板；数量是当前平台下各状态的店铺数，大于 0 才显示 -->
    <ElRadioGroup
      v-if="!showGuide"
      class="status-filter"
      :model-value="statusFilter ?? ''"
      @change="(value: string | number | boolean | undefined) => selectStatus(String(value))"
    >
      <ElRadioButton v-for="option in statusOptions" :key="option.value" :value="option.value">
        <span class="status-option">
          {{ option.label }}
          <span v-if="countsReady && countFor(option.value) > 0" class="status-count">{{
            countFor(option.value)
          }}</span>
        </span>
      </ElRadioButton>
    </ElRadioGroup>

    <ElCard v-if="!showGuide" class="search-card" shadow="never">
      <ElForm inline @submit.prevent="applyKeyword">
        <ElFormItem label="关键字">
          <ElInput
            v-model="keywordDraft"
            clearable
            placeholder="店铺名称或 ID"
            style="width: 200px"
          />
        </ElFormItem>
        <ElFormItem>
          <ElButton type="primary" native-type="submit">搜索</ElButton>
          <ElButton :disabled="!hasFilters && !keywordDraft" @click="clearFilters">重置</ElButton>
        </ElFormItem>
      </ElForm>
    </ElCard>

    <AppNotice v-if="listError && !showGuide" tone="error">
      {{ listError }}
      <template #action><ElButton link @click="retry">重试</ElButton></template>
    </AppNotice>

    <ElCard v-if="!showGuide" class="art-table-card" shadow="never">
      <ArtTableHeader :loading="isLoading" @refresh="retry">
        <template #left>
          <ElButton type="primary" :disabled="syncDisabled" @click="syncNow">{{
            syncButtonLabel
          }}</ElButton>
          <ElButton @click="openAuthorization">绑定新的店铺</ElButton>
          <span class="sync-meta">
            最近同步：{{ lastSyncedLabel }}
            <ElTag size="small" :type="hubState === 'connected' ? 'success' : 'info'">{{
              hubLabel
            }}</ElTag>
          </span>
        </template>
      </ArtTableHeader>

      <ElTable v-loading="isLoading" :data="shops" row-key="sid">
        <ElTableColumn label="店铺" min-width="180">
          <template #default="{ row }">{{ displayName(row as Shop) }}</template>
        </ElTableColumn>
        <ElTableColumn label="平台" width="180">
          <template #default="{ row }">
            <PlatformTag :name="row.platform" :label="platformLabel(row.platform)" />
          </template>
        </ElTableColumn>
        <ElTableColumn label="地区" width="100">
          <template #default="{ row }">{{ row.region?.trim() || '未知' }}</template>
        </ElTableColumn>
        <ElTableColumn label="授权状态" width="110">
          <template #default="{ row }">
            <ElTag size="small" :type="statusType(row.isTokenOk)">{{
              statusLabel(row.isTokenOk)
            }}</ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="最近检查" width="180">
          <template #default="{ row }">{{ formatDateTime(row.syncedAt) }}</template>
        </ElTableColumn>
        <ElTableColumn label="操作" width="100" fixed="right">
          <template #default="{ row }">
            <ElButton link type="primary" @click="showDetail(row as Shop)">详情</ElButton>
          </template>
        </ElTableColumn>
        <template #empty>
          <ElEmpty :description="hasFilters ? '没有符合条件的店铺' : '暂无店铺'" />
        </template>
      </ElTable>

      <div class="shop-pager">
        <ElPagination
          background
          layout="total, sizes, prev, pager, next, jumper"
          :total="totalCount"
          :page-size="pageSize"
          :current-page="currentPage"
          :page-sizes="[20, 50, 100]"
          :disabled="isLoading"
          @size-change="handlePageSizeChange"
          @current-change="handlePageChange"
        />
      </div>
    </ElCard>

    <ElDialog v-model="detailOpen" title="店铺详情" width="480px" append-to-body>
      <ElDescriptions v-if="detailShop" :column="1" border>
        <ElDescriptionsItem label="店铺">{{ displayName(detailShop) }}</ElDescriptionsItem>
        <ElDescriptionsItem label="店铺 ID">{{ detailShop.sid }}</ElDescriptionsItem>
        <ElDescriptionsItem label="平台">
          <PlatformTag :name="detailShop.platform" :label="platformLabel(detailShop.platform)" />
        </ElDescriptionsItem>
        <ElDescriptionsItem label="地区">{{
          detailShop.region?.trim() || '未知'
        }}</ElDescriptionsItem>
        <ElDescriptionsItem label="授权状态">{{
          statusLabel(detailShop.isTokenOk)
        }}</ElDescriptionsItem>
        <ElDescriptionsItem v-if="detailShop.tokenError" label="授权异常">{{
          detailShop.tokenError
        }}</ElDescriptionsItem>
        <ElDescriptionsItem label="最近检查">{{
          formatDateTime(detailShop.syncedAt)
        }}</ElDescriptionsItem>
      </ElDescriptions>
      <template #footer>
        <ElButton @click="detailOpen = false">关闭</ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<script setup lang="ts">
  import { account } from '@/api/auth'
  import { useErpHub } from '@/hooks/business/useErpHub'
  import request, { readErrorMessage, requestRaw } from '@/utils/http'
  import { errorMessage, isRecord, parsePlatforms, parseShopsResponse } from './responses'
  import type { Shop, ShopPlatformOption, ShopStatusFilter, ShopSummary } from './types'

  defineOptions({ name: 'ShopList' })

  type LoadState = 'idle' | 'loading' | 'success' | 'error'

  const authorizationUrl = 'https://v3.wxwerp.com/system/authorize'
  const isAdministrator = computed(() => account.value?.role === 'Administrator')

  const configStatus = ref<'loading' | 'ready' | 'error'>('loading')
  const configured = ref(false)
  const configError = ref('')
  const platforms = ref<ShopPlatformOption[]>([])
  const shopsStatus = ref<LoadState>('idle')
  const shops = ref<Shop[]>([])
  const summary = ref<ShopSummary>({
    valid: 0,
    invalid: 0,
    unknown: 0,
    lastSyncedAt: null,
    syncVersion: 0
  })
  const totalCount = ref(0)
  const pageCount = ref(0)
  const currentPage = ref(1)
  const pageSize = ref(20)
  const listError = ref('')
  const platform = ref('')
  const statusFilter = ref<ShopStatusFilter | null>(null)
  const keywordDraft = ref('')
  const keyword = ref('')
  const detailOpen = ref(false)
  const detailShop = ref<Shop | null>(null)
  const syncError = ref('')
  const now = ref(Date.now())

  const hub = useErpHub()
  const hubState = hub.state
  const syncStatus = hub.status

  let isMounted = false
  let nowTimer: number | undefined
  let activeController: AbortController | null = null
  let requestVersion = 0
  let refreshing = false

  const isLoading = computed(() => shopsStatus.value === 'loading')
  const hasFilters = computed(() => Boolean(platform.value || statusFilter.value || keyword.value))
  const syncRunning = computed(() => syncStatus.value?.running === true)
  const syncDisabled = computed(
    () =>
      !isAdministrator.value ||
      !configured.value ||
      hubState.value !== 'connected' ||
      syncRunning.value ||
      isLoading.value
  )
  const syncButtonLabel = computed(() => {
    if (!syncRunning.value) return '立即同步'
    const status = syncStatus.value
    return status && status.total > 0 ? `同步中 ${status.checked}/${status.total}` : '同步中…'
  })
  const hubLabels: Record<string, string> = {
    connected: '实时连接正常',
    connecting: '正在连接实时状态…',
    reconnecting: '实时连接重连中…',
    disconnected: '实时连接已断开'
  }
  const hubLabel = computed(() => hubLabels[hubState.value] ?? '实时连接未建立')

  // 各平台授权无效的店铺数（标签页角标）；"全部"标签直接用目录汇总
  type StatusKey = 'valid' | 'invalid' | 'unknown'
  const statusOptions = [
    { value: '', label: '全部' },
    { value: 'valid', label: '有效' },
    { value: 'invalid', label: '无效' },
    { value: 'unknown', label: '未知' }
  ] as const
  /** 授权状态角标：键是平台标识，空串表示全部平台；页面初始化时一次性统计，切换标签页只读缓存 */
  const statusCounts = ref<Record<string, Record<StatusKey, number>>>({})
  const countsReady = ref(false)
  const countFor = (status: '' | StatusKey) => {
    const counts = statusCounts.value[platform.value]
    if (!counts) return 0
    return status ? counts[status] : counts.valid + counts.invalid + counts.unknown
  }

  // 没有任何店铺（且不是被筛选条件筛空）时显示向导，而不是空表格
  const showGuide = computed(
    () =>
      configStatus.value !== 'loading' &&
      shopsStatus.value === 'success' &&
      totalCount.value === 0 &&
      !hasFilters.value
  )
  // 当前步骤（从 0 开始）：未绑定密钥停在第一步，已绑定停在第二步
  const guideStep = computed(() => (configured.value ? 1 : 0))
  const router = useRouter()
  const goSettings = () => router.push('/system/erp-binding')
  const lastSyncedLabel = computed(() => relativeTime(summary.value.lastSyncedAt))
  // 目录响应先读状态版本再读店铺行；状态版本更大说明本地列表尚未包含最近一次完成同步。
  const directoryStale = computed(() => {
    const current = syncStatus.value
    return (
      shopsStatus.value === 'success' &&
      current !== null &&
      !current.running &&
      current.version > summary.value.syncVersion
    )
  })

  watch(directoryStale, (stale) => {
    if (stale) void refreshAfterSync()
  })

  onMounted(async () => {
    isMounted = true
    nowTimer = window.setInterval(() => (now.value = Date.now()), 30_000)
    const requests = [loadConfig(), loadPlatforms(), loadShops(1)]
    if (isAdministrator.value) requests.push(hub.refreshStatus())
    await Promise.allSettled(requests)
    if (isMounted && isAdministrator.value) await hub.connect()
  })

  onUnmounted(() => {
    isMounted = false
    if (nowTimer !== undefined) window.clearInterval(nowTimer)
    requestVersion += 1
    activeController?.abort()
    activeController = null
    refreshing = false
    void hub.disconnect()
  })

  async function loadConfig() {
    try {
      const body: unknown = await request.get({
        url: '/api/erp/order-sync-availability',
        fallbackMessage: '无法读取上游连接状态。'
      })
      if (!isRecord(body) || typeof body.configured !== 'boolean')
        throw new Error('上游连接状态格式无效。')
      configured.value = body.configured
      configStatus.value = 'ready'
      configError.value = ''
    } catch (error) {
      configStatus.value = 'error'
      configError.value = errorMessage(error, '无法读取上游连接状态。')
    }
  }

  async function loadPlatforms() {
    try {
      platforms.value = parsePlatforms(await request.get({ url: '/api/erp/platforms' }))
    } catch {
      // 平台标签页是辅助能力；失败时保留现有选项，不影响本地店铺读取。
    }
    await loadStatusCounts()
  }

  /** 单个（平台, 授权状态）组合的店铺数：只请求一条记录，取响应里的总数 */
  async function countShops(scope: string, status: StatusKey) {
    const response = await requestRaw({
      url: '/api/erp/shops',
      params: { platform: scope, status, page: 1, pageSize: 1 }
    })
    if (!response.ok) throw new Error('无法统计授权状态。')
    return parseShopsResponse(await response.json()).totalCount
  }

  /**
   * 统计「全部平台 + 每个平台」下各授权状态的店铺数。
   * 整体算完再一次性替换：统计期间继续显示上一次的数字，失败时保留旧数字，角标不会时有时无。
   */
  async function loadStatusCounts() {
    const scopes = ['', ...platforms.value.map((item) => item.name)]
    const statuses: StatusKey[] = ['valid', 'invalid', 'unknown']
    try {
      const entries = await Promise.all(
        scopes.map(async (scope) => {
          const [valid, invalid, unknown] = await Promise.all(
            statuses.map((status) => countShops(scope, status))
          )
          return [scope, { valid, invalid, unknown }] as const
        })
      )
      statusCounts.value = Object.fromEntries(entries)
      countsReady.value = true
    } catch {
      // 角标是辅助信息：失败时保留已有数字，不影响店铺列表
    }
  }

  function selectPlatform(name: string) {
    if (name === platform.value) return
    platform.value = name
    applyFilters()
  }

  async function refreshAfterSync() {
    if (refreshing) return
    refreshing = true
    try {
      // 同步可能在本次刷新期间再次完成；只有目录版本真的前进才继续追赶。
      for (let attempt = 0; attempt < 3 && isMounted && directoryStale.value; attempt += 1) {
        const before = summary.value.syncVersion
        await Promise.allSettled([loadPlatforms(), loadShops(currentPage.value)])
        if (summary.value.syncVersion === before) break
      }
    } finally {
      refreshing = false
    }
  }

  async function loadShops(page: number) {
    const version = ++requestVersion
    activeController?.abort()
    const controller = new AbortController()
    activeController = controller
    shopsStatus.value = 'loading'
    listError.value = ''
    try {
      const response = await requestRaw({
        url: '/api/erp/shops',
        params: {
          page,
          pageSize: pageSize.value,
          platform: platform.value,
          status: statusFilter.value,
          keyword: keyword.value
        },
        signal: controller.signal
      })
      if (version !== requestVersion) return
      if (!response.ok) {
        listError.value = await readErrorMessage(response, '无法获取本地店铺。')
        shopsStatus.value = 'error'
        return
      }
      const parsed = parseShopsResponse(await response.json())
      if (version !== requestVersion) return
      shops.value = parsed.items
      summary.value = parsed.summary
      totalCount.value = parsed.totalCount
      pageCount.value = parsed.pageCount
      currentPage.value = parsed.page
      shopsStatus.value = 'success'
    } catch (error) {
      if (version !== requestVersion || controller.signal.aborted) return
      listError.value = errorMessage(error, '无法获取本地店铺，请稍后重试。')
      shopsStatus.value = 'error'
    } finally {
      if (activeController === controller) activeController = null
    }
  }

  function retry() {
    if (configStatus.value === 'error') void loadConfig()
    void loadPlatforms()
    void loadShops(currentPage.value)
  }

  function applyFilters() {
    currentPage.value = 1
    void loadShops(1)
  }

  function applyKeyword() {
    keyword.value = keywordDraft.value.trim()
    applyFilters()
  }

  function clearFilters() {
    platform.value = ''
    statusFilter.value = null
    keyword.value = ''
    keywordDraft.value = ''
    applyFilters()
  }

  function selectStatus(next: string) {
    statusFilter.value = next ? (next as ShopStatusFilter) : null
    applyFilters()
  }

  function handlePageChange(page: number) {
    if (
      page < 1 ||
      (pageCount.value && page > pageCount.value) ||
      isLoading.value ||
      page === currentPage.value
    )
      return
    currentPage.value = page
    void loadShops(page)
  }

  function handlePageSizeChange(size: number) {
    if (isLoading.value) return
    pageSize.value = size
    applyFilters()
  }

  async function syncNow() {
    if (syncDisabled.value) return
    syncError.value = ''
    const result = await hub.startShopSync()
    if (!result.accepted) syncError.value = result.error ?? '无法启动同步，请稍后重试。'
  }

  const displayName = (shop: Shop) => shop.name.trim() || `店铺 ${shop.sid}`
  const platformLabel = (name: string) =>
    platforms.value.find((item) => item.name === name)?.displayName ?? name
  const statusLabel = (ok: boolean | null) =>
    ok === true ? '有效' : ok === false ? '无效' : '未知'
  const statusType = (ok: boolean | null) =>
    ok === true ? 'success' : ok === false ? 'danger' : 'info'

  function showDetail(shop: Shop) {
    detailShop.value = shop
    detailOpen.value = true
  }

  const openAuthorization = () => window.open(authorizationUrl, '_blank', 'noopener,noreferrer')

  function formatDateTime(value: string | null) {
    if (!value) return '尚未检查'
    const date = new Date(value)
    return Number.isFinite(date.getTime())
      ? date.toLocaleString('zh-CN', { hour12: false })
      : '尚未检查'
  }

  function relativeTime(value: string | null) {
    if (!value) return '尚未同步'
    const time = Date.parse(value)
    if (!Number.isFinite(time)) return '尚未同步'
    const minutes = Math.floor(Math.max(0, now.value - time) / 60_000)
    if (minutes < 1) return '刚刚'
    if (minutes < 60) return `${minutes} 分钟前`
    const hours = Math.floor(minutes / 60)
    if (hours < 24) return `${hours} 小时前`
    const days = Math.floor(hours / 24)
    return days < 30 ? `${days} 天前` : new Date(value).toLocaleDateString('zh-CN')
  }
</script>

<style scoped>
  /* 向导：与「API权限绑定」页同款——左侧图标徽章，右侧步骤和当前步骤的面板 */
  .guide-card {
    min-height: 420px;
  }

  .guide {
    display: flex;
    gap: 48px;
    align-items: center;
    max-width: 960px;
    padding: 24px;
    margin-inline: auto;
  }

  .guide__side {
    display: flex;
    flex: none;
    flex-direction: column;
    gap: 12px;
    align-items: center;
    width: 220px;
    text-align: center;
  }

  .guide__badge {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 88px;
    height: 88px;
    font-size: 44px;
    color: var(--el-color-primary);
    background: var(--el-color-primary-light-9);
    border-radius: 24px;
  }

  .guide__title {
    margin-block: 0;
    font-size: 18px;
    font-weight: 600;
  }

  .guide__desc,
  .guide__hint {
    margin-block: 0;
    font-size: 13px;
    line-height: 1.6;
    color: var(--el-text-color-secondary);
  }

  .guide__main {
    display: flex;
    flex: 1;
    flex-direction: column;
    gap: 32px;
    min-width: 0;
  }

  .guide__panel {
    display: flex;
    flex-direction: column;
    gap: 12px;
    align-items: center;
    align-self: center;
    width: 100%;
    max-width: 440px;
    text-align: center;
  }

  .guide__panel-icon {
    font-size: 56px;
    color: var(--el-color-primary);
  }

  .guide__panel-title {
    margin-block: 0;
    font-size: 18px;
    font-weight: 600;
  }

  .guide__actions {
    display: flex;
    gap: 12px;
    justify-content: center;
  }

  .guide__link {
    align-self: center;
  }

  @media (width <= 768px) {
    .guide {
      flex-direction: column;
      gap: 24px;
      padding: 8px;
    }
  }

  /* 平台标签页：紧凑的一行，标签之间只留很小的间距 */
  .platform-tabs-card :deep(.el-card__body) {
    padding: 2px 12px 0;
  }

  .platform-tabs-card :deep(.el-tabs__header) {
    margin-block: 0;
  }

  .platform-tabs-card :deep(.el-tabs__item) {
    height: 40px;
    padding-inline: 4px;
  }

  .platform-tabs-card :deep(.el-tabs__nav) {
    gap: 4px;
  }

  .platform-tab__all {
    padding-inline: 6px;
    font-size: 13px;
  }

  /* 授权状态筛选：面板外的一组按钮，数量以淡色文字跟在文本右侧 */
  .status-filter {
    padding-bottom: 12px;
  }

  .status-option {
    display: inline-flex;
    gap: 4px;
    align-items: baseline;
  }

  .status-count {
    font-size: 12px;
    color: var(--el-text-color-secondary);
  }

  .is-active .status-count {
    color: inherit;
    opacity: 0.85;
  }

  .sync-meta {
    display: inline-flex;
    gap: 6px;
    align-items: center;
    font-size: 12px;
    color: var(--el-text-color-secondary);
  }

  .shop-pager {
    display: flex;
    justify-content: flex-end;
    padding-top: 12px;
  }
</style>
