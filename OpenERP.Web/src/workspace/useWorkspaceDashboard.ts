import { onMounted, onUnmounted, ref } from 'vue'
import { apiRequest } from '../account/session'
import { parseOrderSnapshotReport, type OrderSnapshotReport } from '../reports/orderSnapshot'
import {
  parseErpAvailability,
  parseErpConfig,
  parseShopDirectory,
  type ErpAvailability,
  type ErpConfigSummary,
  type ShopDirectory,
} from './workspaceData'

export type LoadStatus = 'idle' | 'loading' | 'success' | 'error'

export type WorkspaceDashboardOptions = {
  administrator: boolean
}

async function responseTitle(response: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await response.json()
    if (body && typeof body === 'object' && 'title' in body) {
      const title = (body as { title?: unknown }).title
      if (typeof title === 'string' && title.trim()) return title
    }
  } catch {
    // 错误响应没有可读取的 ProblemDetails 时使用兜底文案。
  }
  return `${fallback}（HTTP ${response.status}）`
}

/**
 * 工作台取数：报表是主数据，目录/配置/可用性是辅助数据。
 * 刷新失败保留上次成功快照，并标记为「显示上次结果」；解析完成后再校验请求版本，避免旧响应覆盖新快照。
 */
export function useWorkspaceDashboard({ administrator }: WorkspaceDashboardOptions) {
  const report = ref<OrderSnapshotReport | null>(null)
  const reportStatus = ref<LoadStatus>('loading')
  const reportError = ref('')
  const reportRefreshing = ref(false)
  const reportStale = ref(false)

  const directory = ref<ShopDirectory | null>(null)
  const directoryStatus = ref<LoadStatus>('idle')
  const directoryError = ref('')

  const config = ref<ErpConfigSummary | null>(null)
  const configStatus = ref<LoadStatus>('idle')
  const configError = ref('')

  const availability = ref<ErpAvailability | null>(null)
  const availabilityStatus = ref<LoadStatus>('idle')
  const availabilityError = ref('')

  /** 是否存在有效 TemuPop 店铺；用目录总数查询判定，不受首屏 100 家窗口限制。 */
  const validTemuShopCount = ref<number | null>(null)

  let reportVersion = 0
  let directoryVersion = 0
  let configVersion = 0
  let availabilityVersion = 0
  let temuShopVersion = 0

  async function loadReport(): Promise<void> {
    const version = ++reportVersion
    if (!report.value) reportStatus.value = 'loading'
    reportRefreshing.value = true
    reportError.value = ''
    try {
      const response = await apiRequest('/api/reports/order-snapshot?page=1&pageSize=1', {
        cache: 'no-store',
      })
      if (version !== reportVersion) return
      if (!response.ok)
        throw new Error(await responseTitle(response, '无法加载工作台数据，请稍后重试。'))
      const parsed = parseOrderSnapshotReport(await response.json())
      if (version !== reportVersion) return
      report.value = parsed
      reportStatus.value = 'success'
      reportStale.value = false
    } catch (cause) {
      if (version !== reportVersion) return
      const message = cause instanceof Error ? cause.message : '无法加载工作台数据，请稍后重试。'
      if (report.value) {
        reportStale.value = true
        reportError.value = message
      } else {
        reportStatus.value = 'error'
        reportError.value = message
      }
    } finally {
      if (version === reportVersion) reportRefreshing.value = false
    }
  }

  /** 管理员拿完整目录（含汇总），员工只拿四字段投影用于店名映射。 */
  async function loadDirectory(): Promise<void> {
    const version = ++directoryVersion
    if (!directory.value) directoryStatus.value = 'loading'
    directoryError.value = ''
    try {
      const response = await apiRequest('/api/erp/shops?page=1&pageSize=100', {
        cache: 'no-store',
      })
      if (version !== directoryVersion) return
      if (!response.ok) throw new Error(await responseTitle(response, '无法读取店铺目录。'))
      const parsed = parseShopDirectory(await response.json())
      if (version !== directoryVersion) return
      directory.value = parsed
      directoryStatus.value = 'success'
    } catch (cause) {
      if (version !== directoryVersion) return
      directoryStatus.value = 'error'
      directoryError.value = cause instanceof Error ? cause.message : '无法读取店铺目录。'
    }
  }

  async function loadValidTemuShopCount(): Promise<void> {
    if (!administrator) return
    const version = ++temuShopVersion
    try {
      const response = await apiRequest(
        '/api/erp/shops?platform=TemuPop&status=valid&page=1&pageSize=1',
        { cache: 'no-store' },
      )
      if (version !== temuShopVersion || !response.ok) return
      const body: unknown = await response.json()
      if (version !== temuShopVersion) return
      if (body && typeof body === 'object' && 'totalCount' in body) {
        const total = (body as { totalCount?: unknown }).totalCount
        if (typeof total === 'number') validTemuShopCount.value = total
      }
    } catch {
      // 引导判定失败时保持上一次结果，由目录卡片承担重试。
    }
  }

  async function loadConfig(): Promise<void> {
    if (!administrator) return
    const version = ++configVersion
    if (!config.value) configStatus.value = 'loading'
    configError.value = ''
    try {
      const response = await apiRequest('/api/erp/config', { cache: 'no-store' })
      if (version !== configVersion) return
      if (!response.ok) throw new Error(await responseTitle(response, '无法读取 ERP 配置。'))
      const parsed = parseErpConfig(await response.json())
      if (version !== configVersion) return
      config.value = parsed
      configStatus.value = 'success'
    } catch (cause) {
      if (version !== configVersion) return
      configStatus.value = 'error'
      configError.value = cause instanceof Error ? cause.message : '无法读取 ERP 配置。'
    }
  }

  async function loadAvailability(): Promise<void> {
    const version = ++availabilityVersion
    if (!availability.value) availabilityStatus.value = 'loading'
    availabilityError.value = ''
    try {
      const response = await apiRequest('/api/erp/order-sync-availability', { cache: 'no-store' })
      if (version !== availabilityVersion) return
      if (!response.ok) throw new Error(await responseTitle(response, '无法读取订单同步状态。'))
      const parsed = parseErpAvailability(await response.json())
      if (version !== availabilityVersion) return
      availability.value = parsed
      availabilityStatus.value = 'success'
    } catch (cause) {
      if (version !== availabilityVersion) return
      availabilityStatus.value = 'error'
      availabilityError.value = cause instanceof Error ? cause.message : '无法读取订单同步状态。'
    }
  }

  async function refresh(): Promise<void> {
    await Promise.allSettled([
      loadReport(),
      loadDirectory(),
      loadValidTemuShopCount(),
      loadConfig(),
      loadAvailability(),
    ])
  }

  onMounted(() => {
    void refresh()
  })

  onUnmounted(() => {
    reportVersion += 1
    directoryVersion += 1
    configVersion += 1
    availabilityVersion += 1
    temuShopVersion += 1
  })

  return {
    report,
    reportStatus,
    reportError,
    reportRefreshing,
    reportStale,
    directory,
    directoryStatus,
    directoryError,
    config,
    configStatus,
    configError,
    availability,
    availabilityStatus,
    availabilityError,
    validTemuShopCount,
    loadReport,
    loadDirectory,
    loadValidTemuShopCount,
    loadConfig,
    loadAvailability,
    refresh,
  }
}
