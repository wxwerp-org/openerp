// @vitest-environment node

import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 设计 token 约束：`theme.css` 是唯一的设计值来源，必须保持精简。
 * 没有任何引用的 token 要删除，取值相同的 token 只保留一个，避免每个模块各自再造一个新名字。
 */
const webRoot = fileURLToPath(new URL('../../', import.meta.url))
const themePath = join(webRoot, 'src/ui/theme.css')

function sourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((name) => {
    const path = join(directory, name)
    if (statSync(path).isDirectory()) return sourceFiles(path)
    return /\.(vue|ts|css)$/.test(name) && path !== themePath ? [path] : []
  })
}

const theme = readFileSync(themePath, 'utf8')
const corpus = [...sourceFiles(join(webRoot, 'src')), ...sourceFiles(join(webRoot, 'tests'))]
  .map((path) => readFileSync(path, 'utf8'))
  .join('\n')

const tokens = [
  ...theme.matchAll(
    /^\s*--((?:color|shadow|radius|text|leading|control|drop-shadow|animate)[a-z0-9-]*)\s*:\s*([^;]+);/gm,
  ),
].map(([, name, value]) => ({ name, value: value.trim().replace(/\s+/g, ' ').toLowerCase() }))

const escape = (text: string): string => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

/** Tailwind 的字号工具类会自动读取 `--text-N--line-height`，不是孤立 token。 */
const isCompanion = (name: string): boolean => name.endsWith('--line-height')

function isUsed(name: string): boolean {
  const short = name.replace(/^(color|shadow|radius|text|leading|drop-shadow|animate)-/, '')
  if (corpus.includes(`var(--${name})`) || theme.includes(`var(--${name})`)) return true
  return new RegExp(
    `(?<![\\w-])(?:[\\w\\[\\]&:>()@*%.-]+:)*-?[a-z-]*?-?${escape(short)}(?![\\w-])`,
  ).test(corpus)
}

describe('Given theme.css 的设计 token', () => {
  it('When 检查引用 Then 每个 token 都至少被使用一次', () => {
    const unused = tokens
      .filter(({ name }) => !isCompanion(name))
      .filter(({ name }) => !isUsed(name))
      .map(({ name }) => name)
    expect(unused).toEqual([])
  })

  it('When 检查引用 Then 所有 var(--token) 都指向已定义的 token', () => {
    const defined = new Set(tokens.map(({ name }) => name))
    const referenced = [
      ...`${corpus}\n${theme}`.matchAll(
        /var\(--((?:color|shadow|radius|text|leading|control)[a-z0-9-]*)\)/g,
      ),
    ]
      .map(([, name]) => name)
      .filter((name) => !defined.has(name))
    expect([...new Set(referenced)]).toEqual([])
  })

  it('When 检查取值 Then 同一个值只对应一个颜色 token', () => {
    const byValue = new Map<string, string[]>()
    for (const { name, value } of tokens.filter(({ name }) => name.startsWith('color-'))) {
      byValue.set(value, [...(byValue.get(value) ?? []), name])
    }
    const duplicates = [...byValue.entries()]
      .filter(([, names]) => names.length > 1)
      .map(([value, names]) => `${value}: ${names.join(', ')}`)
    expect(duplicates).toEqual([])
  })
})
