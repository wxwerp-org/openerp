/** 平台的展示约定：品牌色与 logo 字母，供标签页、表格和详情统一使用 */

/** 已知平台的品牌色，按名称前缀（不区分大小写）匹配 */
const brandColors: [prefix: string, color: string][] = [
  ['temu', '#FF6A00'],
  ['amazon', '#E88B00'],
  ['shopee', '#EE4D2D'],
  ['lazada', '#2A3FC7'],
  ['tiktok', '#1F1F1F'],
  ['aliexpress', '#E62E04'],
  ['ebay', '#0064D2'],
  ['walmart', '#0071CE'],
  ['shopify', '#5E8E3E'],
  ['ozon', '#005BFF'],
  ['mercado', '#D6A100']
]

/** 未知平台按名称哈希从调色板里稳定取色，同一平台任何地方颜色一致 */
const palette = ['#2F6BFF', '#13A8A8', '#8A4FFF', '#E8590C', '#1C9C5C', '#D6336C']

export function platformColor(name: string): string {
  const key = name.trim().toLowerCase()
  const known = brandColors.find(([prefix]) => key.startsWith(prefix))
  if (known) return known[1]

  let hash = 0
  for (const char of key) hash = (hash * 31 + char.codePointAt(0)!) >>> 0
  return palette[hash % palette.length]!
}

/** logo：平台名称的首字母大写；名称为空时用问号 */
export function platformInitial(name: string): string {
  const first = Array.from(name.trim())[0]
  return first ? first.toUpperCase() : '?'
}
