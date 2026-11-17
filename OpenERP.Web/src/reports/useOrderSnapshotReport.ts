import { onMounted, onUnmounted, ref } from 'vue'
import { apiRequest } from '../account/session'
import { parseOrderSnapshotReport, type OrderSnapshotReport, type ReportShop } from './orderSnapshot'

export type ReportStatus = 'loading' | 'success' | 'error'

export function useOrderSnapshotReport() {
  const report = ref<OrderSnapshotReport | null>(null)
  const availablePlatforms = ref<string[]>([])
  const availableShops = ref<ReportShop[]>([])
  const status = ref<ReportStatus>('loading')
  const errorMessage = ref('')
  const platform = ref('')
  const selectedShopKeys = ref<string[]>([])
  const currentPage = ref(1)
  const pageSize = ref(20)
  const expandedOrderIds = ref(new Set<string>())

  let requestVersion = 0
  let activeController: AbortController | null = null

  function isCurrentRequest(requestId: number): boolean {
    return requestId === requestVersion
  }

  async function errorTitle(response: Response): Promise<string> {
    const problem = await response.json().catch(() => null)
    if (problem && typeof problem.title === 'string' && problem.title.trim()) {
      return problem.title
    }
    if (response.status === 400) return '报表查询条件无效'
    if (response.status === 503) return '报表数据暂不可用'
    return '无法加载商品量报表，请稍后重试。'
  }

  async function load(page = 1, size = pageSize.value): Promise<void> {
    const requestId = ++requestVersion
    activeController?.abort()
    const controller = new AbortController()
    activeController = controller
    report.value = null
    status.value = 'loading'
    errorMessage.value = ''
    expandedOrderIds.value = new Set()
    currentPage.value = page
    pageSize.value = size

    const query = new URLSearchParams({ page: String(page), pageSize: String(size) })
    if (platform.value) query.set('platform', platform.value)
    for (const key of selectedShopKeys.value) query.append('shops', key)

    try {
      const response = await apiRequest(`/api/reports/order-snapshot?${query.toString()}`, {
        signal: controller.signal,
      })
      if (!isCurrentRequest(requestId)) return
      if (!response.ok) throw new Error(await errorTitle(response))

      const result = parseOrderSnapshotReport(await response.json())
      if (!isCurrentRequest(requestId)) return
      report.value = result
      availablePlatforms.value = result.platforms
      availableShops.value = result.shops
      currentPage.value = result.page
      pageSize.value = result.pageSize
      status.value = 'success'
    } catch (cause) {
      if (!isCurrentRequest(requestId) || controller.signal.aborted) return
      status.value = 'error'
      errorMessage.value = cause instanceof Error
        ? cause.message
        : '无法加载商品量报表，请稍后重试。'
    } finally {
      if (isCurrentRequest(requestId)) activeController = null
    }
  }

  function updateFilters(): void {
    void load(1)
  }

  function resetFilters(): void {
    platform.value = ''
    selectedShopKeys.value = []
    void load(1)
  }

  function changePage(page: number): void {
    const pageCount = report.value?.pageCount ?? 1
    const target = Math.min(Math.max(page, 1), pageCount)
    if (target !== currentPage.value) void load(target)
  }

  function toggleOrder(id: string): void {
    const next = new Set(expandedOrderIds.value)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    expandedOrderIds.value = next
  }

  onMounted(() => {
    void load()
  })

  onUnmounted(() => {
    requestVersion += 1
    activeController?.abort()
    activeController = null
  })

  return {
    report,
    availablePlatforms,
    availableShops,
    status,
    errorMessage,
    platform,
    selectedShopKeys,
    currentPage,
    pageSize,
    expandedOrderIds,
    load,
    updateFilters,
    resetFilters,
    changePage,
    toggleOrder,
  }
}
