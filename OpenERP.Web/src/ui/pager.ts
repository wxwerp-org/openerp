export type PageItem = number | 'ellipsis'

/**
 * 生成分页页码列表：固定首尾页，当前页前后各两页，间隔用省略号表示。
 */
export function buildPageList(current: number, pageCount: number): PageItem[] {
  if (!Number.isFinite(pageCount) || pageCount < 1) return []

  const total = Math.floor(pageCount)
  const active = Math.min(Math.max(Math.floor(Number.isFinite(current) ? current : 1), 1), total)
  const pages = new Set<number>([1, total])
  for (let page = active - 2; page <= active + 2; page += 1) {
    if (page >= 1 && page <= total) pages.add(page)
  }

  const result: PageItem[] = []
  let previous = 0
  for (const page of [...pages].sort((left, right) => left - right)) {
    if (previous > 0 && page - previous === 2) result.push(previous + 1)
    if (previous > 0 && page - previous > 2) result.push('ellipsis')
    result.push(page)
    previous = page
  }

  return result
}
