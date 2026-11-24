// @vitest-environment node

import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 任意尺寸约束：项目的间距刻度是 1px（`--spacing: 1px`），`min-h-42`、`ml-264` 这样的写法已经够用，
 * 不要再写 `min-h-[42px]`。响应式断点变体（`max-[480px]:`）和 `[@media(...)]:` 不在此限。
 */
const srcRoot = fileURLToPath(new URL('../../src/', import.meta.url))

function vueFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) return vueFiles(path)
    return entry.name.endsWith('.vue') ? [path] : []
  })
}

describe('Given 业务页面与组件的模板', () => {
  it('When 检查尺寸类 Then 不使用 [Npx] 形式的任意尺寸', () => {
    const arbitraryPixels = /(?<![\w-])[a-z]+(?:-[a-z]+)*-\[\d+(?:\.\d+)?px\](?!:)/g
    const offenders = vueFiles(srcRoot).flatMap((path) => {
      const source = readFileSync(path, 'utf8')
      const template = source.slice(source.indexOf('<template>'))
      const style = template.indexOf('<style')
      const markup = style === -1 ? template : template.slice(0, style)
      return [...markup.matchAll(arbitraryPixels)].map(
        ([match]) => `${relative(srcRoot, path).replaceAll('\\', '/')}: ${match}`,
      )
    })
    expect(offenders).toEqual([])
  })
})
