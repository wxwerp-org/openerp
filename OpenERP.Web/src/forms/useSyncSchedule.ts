import { computed, ref, type Ref } from 'vue'
import { apiRequest } from '../account/session'
import {
  httpErrorMessage,
  parseSyncSettings,
  parseSyncShopSelection,
  type SelectableShop,
  type SyncSettings,
} from './orderSyncResponses'

export type ConfigStatus = 'loading' | 'configured' | 'unconfigured' | 'error'
type SyncSettingsStatus = 'idle' | 'loading' | 'ready' | 'error'
type SyncShopStatus = 'idle' | 'loading' | 'ready' | 'error'

/**
 * 订单同步面板里的「定时同步」设置：读取当前设置与可选店铺、勾选、保存。
 *
 * 只有管理员且订单同步能力开启时才会请求接口；每次读取带版本号，面板关闭或状态切换后，
 * 迟到的旧响应不会覆盖新状态。
 */
export function useSyncSchedule(options: {
  canManage: Ref<boolean>
  configStatus: Ref<ConfigStatus>
}) {
  const { canManage, configStatus } = options

  const syncSettingsStatus = ref<SyncSettingsStatus>('idle')
  const syncSettingsError = ref('')
  const syncSettingsMessage = ref('')
  const syncSettingsSaving = ref(false)
  const syncScheduleEnabled = ref(false)
  const syncScheduleShopSids = ref<string[]>([])
  const syncScheduleIntervalMinutes = ref(0)
  const syncScheduleShops = ref<SelectableShop[]>([])
  const syncScheduleShopsStatus = ref<SyncShopStatus>('idle')
  const syncScheduleShopsError = ref('')

  let syncScheduleRequestVersion = 0

  const syncScheduleSelectionValid = computed(
    () => !syncScheduleEnabled.value || syncScheduleShopSids.value.length > 0,
  )

  const syncSchedulePickerShops = computed<SelectableShop[]>(() => {
    const known = new Set(syncScheduleShops.value.map((shop) => shop.sid))
    const missing = syncScheduleShopSids.value
      .filter((sid) => !known.has(sid))
      .map((sid) => ({ sid, name: `已选店铺 ${sid}（不在当前可选列表）` }))
    return [...syncScheduleShops.value, ...missing]
  })

  const syncScheduleCanSave = computed(
    () =>
      canManage.value &&
      syncSettingsStatus.value === 'ready' &&
      configStatus.value === 'configured' &&
      !syncSettingsSaving.value &&
      syncScheduleSelectionValid.value,
  )

  function applySyncSettings(settings: SyncSettings): void {
    syncScheduleEnabled.value = settings.enabled
    syncScheduleShopSids.value = settings.shopSids.map(String)
    syncScheduleIntervalMinutes.value = settings.intervalMinutes
  }

  async function loadSyncSchedule(): Promise<void> {
    if (!canManage.value) return

    const version = ++syncScheduleRequestVersion
    syncSettingsStatus.value = 'loading'
    syncSettingsError.value = ''
    syncSettingsMessage.value = ''

    try {
      const settingsResponse = await apiRequest('/api/erp/order-sync-settings')
      if (version !== syncScheduleRequestVersion) return
      if (!settingsResponse.ok) {
        throw new Error(await httpErrorMessage(settingsResponse, '无法读取定时同步设置'))
      }

      const settings = parseSyncSettings(await settingsResponse.json())
      if (version !== syncScheduleRequestVersion) return
      applySyncSettings(settings)
      syncSettingsStatus.value = 'ready'
    } catch (error) {
      if (version !== syncScheduleRequestVersion) return
      syncSettingsStatus.value = 'error'
      syncSettingsError.value =
        error instanceof Error ? error.message : '无法读取定时同步设置，请稍后重试。'
      return
    }

    await loadSyncScheduleShops(version)
  }

  async function loadSyncScheduleShops(version: number): Promise<void> {
    if (!canManage.value) return
    if (configStatus.value !== 'configured') {
      syncScheduleShops.value = []
      syncScheduleShopsStatus.value = 'error'
      syncScheduleShopsError.value =
        configStatus.value === 'unconfigured'
          ? '配置私有 ERP AccessToken 后可选择店铺。'
          : '服务端配置读取失败，暂时无法选择店铺。'
      return
    }

    syncScheduleShopsStatus.value = 'loading'
    syncScheduleShopsError.value = ''
    try {
      const shopsResponse = await apiRequest('/api/erp/order-sync-settings/shops')
      if (version !== syncScheduleRequestVersion) return
      if (!shopsResponse.ok) {
        throw new Error(await httpErrorMessage(shopsResponse, '无法读取可选店铺'))
      }

      const shops = parseSyncShopSelection(await shopsResponse.json())
      if (version !== syncScheduleRequestVersion) return
      syncScheduleShops.value = shops
      syncScheduleShopsStatus.value = 'ready'
    } catch (error) {
      if (version !== syncScheduleRequestVersion) return
      syncScheduleShops.value = []
      syncScheduleShopsStatus.value = 'error'
      syncScheduleShopsError.value =
        error instanceof Error ? error.message : '无法读取可选店铺，请稍后重试。'
    }
  }

  async function retrySyncShops(): Promise<void> {
    if (!canManage.value || syncSettingsSaving.value) return
    await loadSyncScheduleShops(++syncScheduleRequestVersion)
  }

  async function saveSyncSchedule(): Promise<void> {
    if (!syncScheduleCanSave.value) return

    syncSettingsSaving.value = true
    syncSettingsError.value = ''
    syncSettingsMessage.value = ''
    try {
      const response = await apiRequest('/api/erp/order-sync-settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          enabled: syncScheduleEnabled.value,
          shopSids: syncScheduleShopSids.value.map(Number),
        }),
      })
      if (!response.ok) {
        throw new Error(await httpErrorMessage(response, '无法保存定时同步设置'))
      }

      applySyncSettings(parseSyncSettings(await response.json()))
      syncSettingsMessage.value = syncScheduleEnabled.value
        ? '定时同步已启用，将在下一个周期执行。'
        : '定时同步已关闭。'
    } catch (error) {
      syncSettingsError.value =
        error instanceof Error ? error.message : '保存定时同步设置失败，请稍后重试。'
    } finally {
      syncSettingsSaving.value = false
    }
  }

  function selectAllSyncShops(): void {
    if (syncScheduleShopsStatus.value !== 'ready') return
    syncScheduleShopSids.value = syncScheduleShops.value.map((shop) => shop.sid)
    syncSettingsMessage.value = ''
  }

  function clearSyncShops(): void {
    syncScheduleShopSids.value = []
    syncSettingsMessage.value = ''
  }

  function clearSyncSettingsMessage(): void {
    syncSettingsMessage.value = ''
  }

  /** 有管理权限时：尚未读取或上次失败就读取设置，可选店铺失败则只重试店铺。 */
  function activate(): void {
    if (syncSettingsStatus.value === 'idle' || syncSettingsStatus.value === 'error') {
      void loadSyncSchedule()
      return
    }
    if (syncScheduleShopsStatus.value === 'error') void retrySyncShops()
  }

  /** 组件卸载时：作废在途请求，迟到的响应不再写入状态。 */
  function dispose(): void {
    syncScheduleRequestVersion += 1
  }

  /** 失去管理权限或能力关闭时：作废在途请求并清空全部设置状态。 */
  function reset(): void {
    syncScheduleRequestVersion += 1
    syncSettingsStatus.value = 'idle'
    syncSettingsError.value = ''
    syncSettingsMessage.value = ''
    syncScheduleShops.value = []
    syncScheduleShopsStatus.value = 'idle'
    syncScheduleShopsError.value = ''
    syncScheduleEnabled.value = false
    syncScheduleShopSids.value = []
    syncScheduleIntervalMinutes.value = 0
  }

  return {
    syncSettingsStatus,
    syncSettingsError,
    syncSettingsMessage,
    syncSettingsSaving,
    syncScheduleEnabled,
    syncScheduleShopSids,
    syncScheduleIntervalMinutes,
    syncScheduleShops,
    syncScheduleShopsStatus,
    syncScheduleShopsError,
    syncScheduleSelectionValid,
    syncSchedulePickerShops,
    syncScheduleCanSave,
    loadSyncSchedule,
    retrySyncShops,
    saveSyncSchedule,
    selectAllSyncShops,
    clearSyncShops,
    clearSyncSettingsMessage,
    activate,
    dispose,
    reset,
  }
}
