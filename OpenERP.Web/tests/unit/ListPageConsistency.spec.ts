// @vitest-environment node

import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 列表页一致性：列表类页面统一使用 `AppListPage` 搭建「提示区、筛选条、工具条、通栏表格」，
 * 不再各自拼装卡片外壳，也不再使用只为单个页面存在的表格外观。
 */
const srcRoot = fileURLToPath(new URL('../../src/', import.meta.url))
const read = (file: string): string => readFileSync(`${srcRoot}${file}`, 'utf8')

describe('Given 列表类页面', () => {
  it.each(['views/UsersView.vue', 'views/OrdersView.vue', 'views/ShopsView.vue'])(
    'When 检查 %s Then 使用 AppListPage 且表格使用标准列表外观',
    (file) => {
      const source = read(file)
      expect(source).toContain('<AppListPage')
      expect(source).not.toMatch(/<AppTable[^>]*variant="(card|plain)"/s)
    },
  )

  it('When 检查 AppTable Then 不再提供仅订单页使用的 card 外观', () => {
    expect(read('ui/AppTable.vue')).not.toContain("'card'")
  })
})
