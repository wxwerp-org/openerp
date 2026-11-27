import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

// 页面布局与间距规范（docs/development.md「页面布局与间距规范」）的强制检查：
// 业务页的面板间距只能由 .page-stack / .page-row / .page-grid 的 gap 决定，
// 提示统一用 AppNotice，统计块统一用 AppStat。

// vitest 在 OpenERP.Web 目录下运行，路径相对该目录
const read = (path: string) => readFileSync(join(process.cwd(), path), 'utf8')

/** 业务页：必须以 page-stack / page-row 为根容器 */
const pages = [
  'src/views/order/list/index.vue',
  'src/views/order/detail/index.vue',
  'src/views/order/sync/index.vue',
  'src/views/shop/list/index.vue',
  'src/views/system/user/index.vue',
  'src/views/system/erp-binding/index.vue'
]

const templateOf = (source: string) => source.slice(0, source.indexOf('<script'))
const styleOf = (source: string) => source.slice(source.indexOf('<style'))

describe('业务页布局与间距规范', () => {
  it.each(pages)('Given %s When 检查根容器 Then 以 page-stack 或 page-row 开头', (path) => {
    const root = /<template>\s*<div class="([^"]*)"/.exec(read(path))

    expect(root, '模板根节点应是带 class 的 div').not.toBeNull()
    expect(root![1]).toMatch(/\bpage-(stack|row)\b/)
  })

  it.each(pages)(
    'Given %s When 检查外边距 Then 不用 mb-/mt-/my-/m- 工具类，样式里不写 margin / margin-top / margin-bottom',
    (path) => {
      const source = read(path)

      // 面板间距由容器 gap 决定；需要内部留白时用 padding 或 gap
      expect(templateOf(source)).not.toMatch(/class="[^"]*\b(?:mb|mt|my|m)-\d/)
      expect(styleOf(source)).not.toMatch(/\bmargin(?:-top|-bottom)?\s*:/)
    }
  )

  it.each(pages)(
    'Given %s When 检查提示与统计 Then 不用 ElAlert，改用 AppNotice / AppStat',
    (path) => {
      expect(templateOf(read(path))).not.toContain('<ElAlert')
    }
  )

  it('Given 全局样式 When 检查间距变量 Then page-stack 的 gap 来自 --page-gap', () => {
    const scss = read('src/assets/styles/core/app.scss')

    expect(scss).toMatch(/--page-gap:\s*12px/)
    expect(scss).toMatch(/\.page-stack\s*\{[^}]*gap:\s*var\(--page-gap\)/s)
    expect(scss).toMatch(/\.page-row\s*\{[^}]*gap:\s*var\(--page-gap\)/s)
    expect(scss).toMatch(/\.page-grid\s*\{[^}]*gap:\s*var\(--page-gap\)/s)
  })
})

describe('文案规范', () => {
  it.each([...pages, '../README.md', '../docs/development.md'])(
    'Given %s When 检查产品名称 Then 使用“旺销王”而不是“旺销网”',
    (path) => {
      expect(read(path)).not.toContain('旺销网')
    }
  )
})
