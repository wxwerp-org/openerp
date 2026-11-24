// @vitest-environment node

import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 控件一致性：表单控件只能通过 `src/ui` 里的通用组件使用，页面不得直接写原生控件，
 * 否则每个页面都要重复声明一遍边框、高度与箭头样式，外观会逐渐偏离设计规范原型。
 */
const srcRoot = fileURLToPath(new URL('../../src/', import.meta.url))

function vueFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) return vueFiles(path)
    return entry.name.endsWith('.vue') ? [path] : []
  })
}

function templateOf(path: string): string {
  const source = readFileSync(path, 'utf8')
  return source.slice(source.indexOf('<template>'))
}

function offenders(pattern: RegExp, allowed: string[]): string[] {
  return vueFiles(srcRoot)
    .map((path) => relative(srcRoot, path).replaceAll('\\', '/'))
    .filter((file) => !allowed.includes(file))
    .filter((file) => pattern.test(templateOf(join(srcRoot, file))))
}

describe('Given 业务页面与组件', () => {
  it('When 检查下拉选择 Then 只有 AppSelect 可以直接使用原生 select', () => {
    expect(offenders(/<select[\s>]/, ['ui/AppSelect.vue'])).toEqual([])
  })

  it('When 检查日期输入 Then 只有 AppInput 可以直接使用原生 date input', () => {
    expect(offenders(/<input[^>]*type="date"/s, ['ui/AppInput.vue'])).toEqual([])
  })
})
