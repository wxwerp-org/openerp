import { onMounted, onUnmounted, ref } from 'vue'
import { apiRequest } from '../account/session'
import { parseOrderSnapshotReport, type OrderSnapshotReport } from '../reports/orderSnapshot'
import { resolveTrendRange, type TrendRange } from './workspaceDerivation'
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

  /**
   * 报表是走势的主数据；区间变化时重新取数。
   * 返回本次请求是否成功；`onLoaded` 只在本次响应仍然是最新请求时触发，
   * 用于把「已展示周期」与报表在同一版本校验内一起提交，避免迟到响应写回旧周期。
   */
  async function loadReport(range: TrendRange, onLoaded?: () => void): Promise<boolean> {
    const version = ++reportVersion
    if (!report.value) reportStatus.value = 'loading'
    reportRefreshing.value = true
    reportError.value = ''
    let succeeded = false
    try {
      const query = new URLSearchParams({
        page: '1',
        pageSize: '1',
        trendStart: range.start,
        trendEnd: range.end,
      })
      const response = await apiRequest(`/api/reports/order-snapshot?${query.toString()}`, {
        cache: 'no-store',
      })
      if (version !== reportVersion) return false
      if (!response.ok)
        throw new Error(await responseTitle(response, '无法加载工作台数据，请稍后重试。'))
      const parsed = parseOrderSnapshotReport(await response.json())
      if (version !== reportVersion) return false
      report.value = parsed
      reportStatus.value = 'success'
      reportStale.value = false
      succeeded = true
      onLoaded?.()
    } catch (cause) {
      if (version !== reportVersion) return false
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
    return succeeded
  }

  /** 读取当前账号的目录用于店名映射，管理员另有全局授权汇总。 */
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

  async function refresh(
    range: TrendRange = resolveTrendRange('last7'),
    onReportLoaded?: () => void,
  ): Promise<boolean> {
    const [reportResult] = await Promise.allSettled([
      loadReport(range, onReportLoaded),
      loadDirectory(),
      loadValidTemuShopCount(),
      loadConfig(),
      loadAvailability(),
    ])
    return reportResult.status === 'fulfilled' && reportResult.value === true
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
