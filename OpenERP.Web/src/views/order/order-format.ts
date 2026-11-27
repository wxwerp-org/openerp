/** 订单列表与订单详情共用的展示辅助（与平台无关） */
import type { Order } from '@/api/order'

/** 取一组补充信息（不合并，保持顺序），用于单个商品行 */
export const detailsOf = (
  details: { label: string; value: string; group: string }[],
  group: string
) => details.filter((item) => (item.group || 'other') === group)

/** 保留来源记录的日历日期和墙钟时间，与创建日期筛选的口径一致 */
export const sourceTime = (value: string | null) =>
  value ? value.slice(0, 19).replace('T', ' ') : '—'

export const money = (amount: number, currency: string | null) =>
  `${currency ? `${currency} ` : ''}${amount.toFixed(2)}`

/** 平台规格形如 “Color: Red; Size: M”：按分号拆成条目，条目内第一个冒号区分名称与取值 */
export function specsOf(specs: string | null) {
  return (specs ?? '')
    .split(/[;；\n]/)
    .map((part) => part.trim())
    .filter(Boolean)
    .map((part) => {
      const at = part.search(/[:：]/)
      return at > 0
        ? { label: part.slice(0, at).trim(), value: part.slice(at + 1).trim() }
        : { label: '', value: part }
    })
}

/** 取某个分组下的补充信息：订单自身的条目加上各商品行的条目；同名条目合并取值（去重） */
export function factsOf(order: Order, group: string, withLines = true) {
  const merged = new Map<string, string[]>()
  const items = withLines
    ? [...order.details, ...order.lines.flatMap((line) => line.details)]
    : order.details
  for (const item of items) {
    if ((item.group || 'other') !== group) continue
    const values = merged.get(item.label) ?? []
    if (!values.includes(item.value)) values.push(item.value)
    merged.set(item.label, values)
  }
  return [...merged].map(([label, values]) => ({ label, value: values.join('、') }))
}
