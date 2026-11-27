<!-- Order sync page: per-shop manual sync, sync all, admin schedule. -->
<template>
  <div class="page-stack page-stack--fill">
    <AppNotice v-if="orderSyncEnabled && hubState !== 'connected'" tone="warning">
      {{ hubTitle }}
      <template v-if="hubState === 'disconnected'" #action>
        <ElButton link @click="connect">重新连接</ElButton>
      </template>
    </AppNotice>
    <AppNotice v-if="configStatus === 'unconfigured'" tone="warning">
      尚未绑定旺销王 ERP 密钥，订单同步通过旺销王的平台接口读取订单。
      <template v-if="!employee" #action>
        <RouterLink to="/system/erp-binding">前往 API权限绑定</RouterLink>
      </template>
    </AppNotice>
    <AppNotice v-if="configStatus === 'configured' && !orderSyncEnabled" tone="warning">
      当前环境未开启订单同步，仅 Development / Production 提供历史订单同步能力。
    </AppNotice>
    <AppNotice v-if="shopsStatus === 'error'" tone="error">{{ errorMessage }}</AppNotice>
    <AppNotice v-if="batchError" tone="error">{{ batchError }}</AppNotice>
    <AppNotice v-else-if="batchMessage" tone="success">{{ batchMessage }}</AppNotice>

    <!-- 标签页：店铺列表 / 配置定时同步（仅管理员）；当前标签同步到地址栏 ?tab= -->
    <ElTabs :model-value="activeTab" class="sync-tabs" @tab-change="changeTab">
      <ElTabPane label="店铺列表" name="shops" />
      <ElTabPane v-if="canManageSchedule" label="配置定时同步" name="schedule" />
    </ElTabs>

    <!-- 定时同步（仅管理员） -->
    <ElCard
      v-if="canManageSchedule && activeTab === 'schedule'"
      shadow="never"
      data-testid="sync-settings-card"
    >
      <template #header>
        <div class="card-title">
          <span>定时同步</span>
          <ElTag
            v-if="syncSettingsStatus === 'ready'"
            size="small"
            :type="syncScheduleEnabled ? 'success' : 'info'"
          >
            {{ syncScheduleEnabled ? '已启用' : '未启用' }}
          </ElTag>
        </div>
      </template>
      <div v-if="syncSettingsStatus === 'loading'" class="hint">正在读取定时同步设置…</div>
      <AppNotice v-else-if="syncSettingsStatus === 'error'" tone="error">
        {{ syncSettingsError }}
        <template #action><ElButton link @click="loadSyncSchedule">重试</ElButton></template>
      </AppNotice>
      <ElForm v-else label-position="top" class="schedule-form" @submit.prevent>
        <ElFormItem label="启用定时同步">
          <div class="switch-row">
            <ElSwitch
              v-model="syncScheduleEnabled"
              :disabled="syncSettingsSaving || configStatus !== 'configured'"
              @change="clearSyncSettingsMessage"
            />
            <span class="hint">
              每
              {{ syncScheduleIntervalMinutes }}
              分钟尝试一轮；首次执行会等待一个周期，与运行中的批次冲突时本轮跳过。
            </span>
          </div>
        </ElFormItem>
        <ElFormItem label="选择店铺">
          <div class="shop-field">
            <div v-if="syncScheduleShopsStatus === 'loading'" class="hint">正在读取可选店铺…</div>
            <div v-else-if="syncScheduleShopsStatus === 'error'" class="hint">
              {{ syncScheduleShopsError }}
              <ElButton link type="primary" :disabled="syncSettingsSaving" @click="retrySyncShops"
                >重试</ElButton
              >
            </div>
            <div v-else-if="syncSchedulePickerShops.length === 0" class="hint">暂无</div>
            <template v-else>
              <ElScrollbar max-height="132px" class="shop-picker">
                <ElCheckboxGroup
                  v-model="syncScheduleShopSids"
                  :disabled="syncSettingsSaving"
                  @change="clearSyncSettingsMessage"
                >
                  <ElCheckbox
                    v-for="shop in syncSchedulePickerShops"
                    :key="shop.sid"
                    :value="shop.sid"
                    data-testid="sync-shop-option"
                  >
                    {{ shopName(shop) }}（{{ shop.sid }}）
                  </ElCheckbox>
                </ElCheckboxGroup>
              </ElScrollbar>
              <div class="shop-actions">
                <ElButton
                  link
                  type="primary"
                  :disabled="syncSettingsSaving"
                  @click="selectAllSyncShops"
                  >全选可见店铺</ElButton
                >
                <ElButton
                  link
                  type="primary"
                  :disabled="syncSettingsSaving || syncScheduleShopSids.length === 0"
                  @click="clearSyncShops"
                  >清空</ElButton
                >
                <span class="hint">已选 {{ syncScheduleShopSids.length }} 家</span>
              </div>
            </template>
          </div>
        </ElFormItem>
        <AppNotice v-if="syncScheduleEnabled && syncScheduleShopSids.length === 0" tone="warning">
          启用定时同步前请至少选择一家店铺。
        </AppNotice>
        <div class="save-row">
          <ElButton
            type="primary"
            :disabled="!syncScheduleCanSave"
            :loading="syncSettingsSaving"
            @click="saveSyncSchedule"
            >保存</ElButton
          >
          <span v-if="syncSettingsMessage" class="hint">{{ syncSettingsMessage }}</span>
          <span
            v-if="syncSettingsError && syncSettingsStatus === 'ready'"
            class="hint text-danger"
            >{{ syncSettingsError }}</span
          >
        </div>
      </ElForm>
    </ElCard>

    <!-- 店铺列表 -->
    <ElCard v-if="activeTab === 'shops'" class="art-table-card" shadow="never">
      <ArtTableHeader :loading="isLoading" @refresh="retry">
        <template #left>
          <div class="table-left">
            <ElButton
              v-if="orderSyncEnabled"
              type="primary"
              :disabled="!canSyncAll"
              :loading="submittingAll"
              @click="syncAll"
              >全部同步</ElButton
            >
            <span class="hint">
              <template v-if="shopsStatus === 'success'">共 {{ totalCount }} 家店铺</template>
              <template v-else-if="shopsStatus === 'loading'">正在加载店铺列表</template>
            </span>
          </div>
        </template>
      </ArtTableHeader>

      <div class="table-wrap">
        <ElTable v-loading="isLoading" :data="shops" row-key="sid" height="100%">
          <template #empty>
            <ElEmpty
              :description="
                employee
                  ? '暂无可见的店铺，请联系管理员确认店铺分配。'
                  : '本地暂无授权店铺，请先同步授权店铺目录。'
              "
            >
              <ElButton v-if="!employee" @click="goShops">前往授权店铺</ElButton>
            </ElEmpty>
          </template>
          <ElTableColumn prop="sid" label="编号" width="90" />
          <ElTableColumn label="名称" min-width="160">
            <template #default="{ row }">{{ shopName(row as Shop) }}</template>
          </ElTableColumn>
          <ElTableColumn prop="platform" label="平台" width="120" />
          <ElTableColumn label="区域" width="90">
            <template #default="{ row }">{{ row.region?.trim() || '未知' }}</template>
          </ElTableColumn>
          <ElTableColumn label="订单同步" width="120">
            <template #default="{ row }">
              <ElTag v-if="!row.syncSupported" size="small" type="info">暂未支持同步</ElTag>
              <ElTag v-else size="small" type="success">支持同步</ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn v-if="orderSyncEnabled" label="操作" width="120" fixed="right">
            <template #default="{ row }">
              <ElButton
                size="small"
                :disabled="shopSyncDisabled(row as Shop)"
                :loading="submittingSids.has(Number(row.sid))"
                @click="syncShop(Number(row.sid))"
              >
                同步订单
              </ElButton>
            </template>
          </ElTableColumn>
          <ElTableColumn v-if="orderSyncEnabled" label="同步进度" min-width="360">
            <template #default="{ row }">
              <div v-if="taskOf(row.sid)" class="task-cell">
                <div class="task-head">
                  <ElTag
                    size="small"
                    :type="tagType(orderSyncStatusClass(taskOf(row.sid)!.status))"
                  >
                    {{ orderSyncStatusLabel(taskOf(row.sid)!) }}
                  </ElTag>
                  <span>{{ orderSyncStageLabel(taskOf(row.sid)!.stage) }}</span>
                </div>
                <ElProgress
                  :percentage="orderSyncProgress(taskOf(row.sid)!)"
                  :status="progressStatus(taskOf(row.sid)!)"
                />
                <div class="hint">
                  读取 {{ taskOf(row.sid)!.read }} / {{ taskOf(row.sid)!.total }} · 已处理
                  {{ taskOf(row.sid)!.processed }} · 新增 {{ taskOf(row.sid)!.added }} · 更新
                  {{ taskOf(row.sid)!.updated }} · 未变 {{ taskOf(row.sid)!.unchanged }} · 跳过
                  {{ taskOf(row.sid)!.skipped }}
                </div>
                <div
                  v-for="[reason, count] in Object.entries(taskOf(row.sid)!.skipReasons)"
                  :key="reason"
                  class="hint"
                >
                  {{ reason }}：{{ count }}
                </div>
                <div v-if="taskOf(row.sid)!.error" class="hint text-danger">
                  {{ taskOf(row.sid)!.error }}
                </div>
              </div>
              <span v-else class="hint">—</span>
              <div v-if="orderSyncErrors[Number(row.sid)]" class="hint text-danger" role="alert">
                {{ orderSyncErrors[Number(row.sid)] }}
              </div>
            </template>
          </ElTableColumn>
        </ElTable>
      </div>

      <div class="table-pager">
        <ElPagination
          background
          layout="total, sizes, prev, pager, next"
          :total="totalCount"
          :page-size="pageSize"
          :current-page="currentPage"
          :page-sizes="[20, 50, 100]"
          :disabled="paginationDisabled"
          @size-change="handlePageSizeChange"
          @current-change="handlePageChange"
        />
      </div>
    </ElCard>
  </div>
</template>

<script setup lang="ts">
  import { account } from '@/api/auth'
  import request from '@/utils/http'
  import { useErpHub } from '@/hooks/business/useErpHub'
  import { parseConfig, parseShopsResponse, type Shop } from '@/hooks/business/orderSyncResponses'
  import { useSyncSchedule, type ConfigStatus } from '@/hooks/business/useSyncSchedule'
  import {
    orderSyncProgress,
    orderSyncProgressTone,
    orderSyncStageLabel,
    orderSyncStatusClass,
    orderSyncStatusLabel,
    type TagClass
  } from '@/hooks/business/orderSyncPresentation'
  import type { OrderSyncTask } from '@/hooks/business/orderSyncTask'

  defineOptions({ name: 'OrderSync' })

  const hub = useErpHub({ shopSync: false })
  const { state: hubState, orderSyncTasks, startOrderSync, startAllOrderSync, connect } = hub
  const router = useRouter()
  const route = useRoute()
  const taskOf = (sid: string | number) => orderSyncTasks.value[Number(sid)]

  type ShopsStatus = 'idle' | 'loading' | 'success' | 'error'
  const submittingSids = ref(new Set<number>())
  const submittingAll = ref(false)
  const batchMessage = ref('')
  const batchError = ref('')
  const configStatus = ref<ConfigStatus>('loading')
  const shopsStatus = ref<ShopsStatus>('idle')
  const shops = ref<Shop[]>([])
  const currentPage = ref(1)
  const pageSize = ref(20)
  const totalCount = ref(0)
  const pageCount = ref(0)
  const errorMessage = ref('')
  const orderSyncErrors = ref<Record<string, string>>({})
  const orderSyncEnabled = ref(false)
  const employee = computed(() => account.value?.role === 'User')
  const canManageSchedule = computed(
    () => orderSyncEnabled.value && account.value?.role === 'Administrator'
  )

  /** 当前标签由地址栏 ?tab= 决定；非管理员或未知值一律回到店铺列表 */
  const activeTab = computed<'shops' | 'schedule'>(() =>
    route.query.tab === 'schedule' && canManageSchedule.value ? 'schedule' : 'shops'
  )
  const changeTab = (name: string | number) => {
    if (name === activeTab.value) return
    void router.push({ query: { ...route.query, tab: String(name) } })
  }

  const schedule = useSyncSchedule({ canManage: canManageSchedule, configStatus })
  const {
    syncSettingsStatus,
    syncSettingsError,
    syncSettingsMessage,
    syncSettingsSaving,
    syncScheduleEnabled,
    syncScheduleShopSids,
    syncScheduleIntervalMinutes,
    syncScheduleShopsStatus,
    syncScheduleShopsError,
    syncSchedulePickerShops,
    syncScheduleCanSave,
    loadSyncSchedule,
    retrySyncShops,
    saveSyncSchedule,
    selectAllSyncShops,
    clearSyncShops,
    clearSyncSettingsMessage
  } = schedule

  const isLoading = computed(
    () => configStatus.value === 'loading' || shopsStatus.value === 'loading'
  )
  const paginationDisabled = computed(() => isLoading.value || pageCount.value < 1)
  const hubTitle = computed(() =>
    hubState.value === 'disconnected'
      ? '实时连接已断开'
      : hubState.value === 'reconnecting'
        ? '正在重新连接…'
        : '正在连接…'
  )
  const tagType = (tone: TagClass) =>
    ({ green: 'success', red: 'danger', gray: 'info', amber: 'warning' })[tone] as
      'success' | 'danger' | 'info' | 'warning'
  const progressStatus = (task: OrderSyncTask) => {
    const tone = orderSyncProgressTone(task)
    return tone === 'primary' ? undefined : tone === 'danger' ? 'exception' : tone
  }
  const shopName = (shop: { sid: string; name: string }) => shop.name.trim() || `店铺 ${shop.sid}`

  const goShops = () => {
    router.push('/shop/list')
  }

  const canStartOrderSync = computed(
    () =>
      orderSyncEnabled.value &&
      configStatus.value === 'configured' &&
      hubState.value === 'connected'
  )
  const canSyncAll = computed(
    () =>
      canStartOrderSync.value &&
      shopsStatus.value === 'success' &&
      shops.value.some((shop) => shop.syncSupported) &&
      !submittingAll.value
  )
  const shopSyncDisabled = (shop: Shop) =>
    !shop.syncSupported ||
    !canStartOrderSync.value ||
    submittingSids.value.has(Number(shop.sid)) ||
    orderSyncTasks.value[Number(shop.sid)]?.status === 'running'

  async function syncShop(sid: number) {
    const shop = shops.value.find((item) => Number(item.sid) === sid)
    if (!shop || shopSyncDisabled(shop)) return
    submittingSids.value.add(sid)
    delete orderSyncErrors.value[sid]
    try {
      const result = await startOrderSync(sid)
      if (!result.accepted)
        orderSyncErrors.value[sid] = result.error ?? '无法启动订单同步，请稍后重试。'
    } finally {
      submittingSids.value.delete(sid)
    }
  }

  async function syncAll() {
    if (!canSyncAll.value) return
    submittingAll.value = true
    batchMessage.value = ''
    batchError.value = ''
    try {
      const result = await startAllOrderSync()
      if (!result.accepted) {
        batchError.value = result.error ?? '无法启动全部同步，请稍后重试。'
        return
      }
      orderSyncErrors.value = {}
      batchMessage.value = result.busyCount
        ? `已启动 ${result.startedCount} 家店铺同步，${result.busyCount} 家正在同步的店铺已跳过。`
        : `已启动 ${result.startedCount} 家店铺同步。`
    } finally {
      submittingAll.value = false
    }
  }

  function setOrderSyncEnabled(enabled: boolean) {
    orderSyncEnabled.value = enabled
    if (canManageSchedule.value) {
      schedule.activate()
      return
    }
    schedule.reset()
    if (!enabled) orderSyncErrors.value = {}
  }

  function clearShops() {
    shops.value = []
    totalCount.value = 0
    pageCount.value = 0
  }

  let requestVersion = 0
  let activeController: AbortController | null = null

  async function loadPage(
    page: number,
    size: number,
    checkConfiguration = false,
    clampPage = true
  ): Promise<void> {
    const requestId = ++requestVersion
    activeController?.abort()
    const controller = new AbortController()
    activeController = controller
    currentPage.value = page
    pageSize.value = size
    shops.value = []
    errorMessage.value = ''
    shopsStatus.value = 'loading'
    const isCurrent = () => requestId === requestVersion

    try {
      if (
        checkConfiguration ||
        configStatus.value === 'loading' ||
        configStatus.value === 'error'
      ) {
        configStatus.value = 'loading'
        const config = parseConfig(
          await request.get({
            url: '/api/order-sync/availability',
            signal: controller.signal,
            fallbackMessage: '无法读取订单同步配置'
          })
        )
        if (!isCurrent()) return
        configStatus.value = config.configured ? 'configured' : 'unconfigured'
        setOrderSyncEnabled(config.orderSyncEnabled)
      }
      if (configStatus.value !== 'configured') {
        shopsStatus.value = 'idle'
        return
      }

      const response = parseShopsResponse(
        await request.get({
          url: '/api/order-sync/shops',
          params: { page, pageSize: size },
          signal: controller.signal,
          fallbackMessage: '无法获取授权店铺'
        })
      )
      if (!isCurrent()) return
      const lastPage = Math.max(response.pageCount, 1)
      if (clampPage && response.page > lastPage) {
        await loadPage(lastPage, response.pageSize, false, false)
        return
      }
      shops.value = response.items
      currentPage.value = response.page
      pageSize.value = response.pageSize
      totalCount.value = response.totalCount
      pageCount.value = response.pageCount
      shopsStatus.value = 'success'
    } catch (error) {
      if (!isCurrent()) return
      if (configStatus.value === 'loading') configStatus.value = 'error'
      clearShops()
      shopsStatus.value = 'error'
      errorMessage.value = error instanceof Error ? error.message : '请求失败，请稍后重试。'
    } finally {
      if (isCurrent()) activeController = null
    }
  }

  const retry = () => void loadPage(currentPage.value, pageSize.value, true)
  const handlePageChange = (page: number) =>
    page !== currentPage.value && void loadPage(page, pageSize.value)
  const handlePageSizeChange = (size: number) => size !== pageSize.value && void loadPage(1, size)

  onMounted(() => {
    void loadPage(currentPage.value, pageSize.value, true)
    void connect()
  })

  // 员工账号重连后重新拉取店铺（可见范围可能变化）
  watch(hubState, (state, previous) => {
    if (!employee.value || state !== 'connected' || previous === 'idle') return
    orderSyncErrors.value = {}
    void loadPage(currentPage.value, pageSize.value)
  })

  onUnmounted(() => {
    schedule.dispose()
    void hub.disconnect()
    requestVersion += 1
    activeController?.abort()
  })
</script>

<style scoped>
  .hint {
    font-size: 12px;
    line-height: 18px;
    color: var(--el-text-color-secondary);
  }

  .sync-tabs :deep(.el-tabs__header) {
    margin-block-end: 0;
  }

  .card-title,
  .switch-row,
  .shop-actions,
  .save-row,
  .table-left,
  .task-head {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    align-items: center;
  }

  .card-title {
    justify-content: space-between;
  }

  .schedule-form {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }

  .schedule-form :deep(.el-form-item) {
    margin-block-end: 0;
  }

  .schedule-form :deep(.el-form-item__label) {
    padding-block-end: 2px;
    line-height: 20px;
  }

  .shop-field {
    display: flex;
    flex-direction: column;
    gap: 4px;
    width: 100%;
  }

  .shop-picker {
    border: 1px solid var(--el-border-color-lighter);
    border-radius: 4px;
  }

  .shop-picker :deep(.el-checkbox-group) {
    display: flex;
    flex-wrap: wrap;
    gap: 0 16px;
    padding: 6px 10px;
  }

  .task-cell {
    display: flex;
    flex-direction: column;
    gap: 2px;
    padding-block: 6px;
  }

  /* 卡片内纵向布局：表格内部滚动，分页条固定在底部 */
  .art-table-card :deep(.el-card__body) {
    display: flex;
    flex-direction: column;
    min-height: 0;
  }

  .table-wrap {
    flex: 1;
    min-height: 0;
  }

  .table-pager {
    display: flex;
    flex-shrink: 0;
    justify-content: flex-end;
    padding-top: 12px;
  }
</style>
