#!/usr/bin/env node
// 报告 src/styles 中定义的类是否仍被任何模板/脚本引用，辅助分阶段删除旧 CSS。
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'

const root = process.cwd()
const stylesDir = join(root, 'src/styles')
const srcDir = join(root, 'src')

function walk(dir) {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    return statSync(full).isDirectory() ? walk(full) : [full]
  })
}

function parseClasses(css) {
  const classes = new Set()
  const stripped = css.replace(/\/\*[\s\S]*?\*\//g, '')
  for (const match of stripped.matchAll(/([^{}]+)\{/g)) {
    const selector = match[1].trim()
    if (selector.startsWith('@') || /^\d+%$/.test(selector) || /^(from|to)$/.test(selector))
      continue
    for (const token of selector.matchAll(/\.([a-zA-Z][\w-]*)/g)) classes.add(token[1])
  }
  return classes
}

const styles = new Map()
for (const file of walk(stylesDir)) {
  if (!file.endsWith('.css')) continue
  styles.set(relative(root, file), parseClasses(readFileSync(file, 'utf8')))
}

const sourceText = walk(srcDir)
  .filter((file) => !file.startsWith(stylesDir))
  .filter((file) => /\.(vue|ts|js)$/.test(file))
  .map((file) => readFileSync(file, 'utf8'))
  .join('\n')
const tokens = new Set(sourceText.match(/[a-zA-Z][\w-]*/g) ?? [])

const dead = []
for (const [file, classes] of styles) {
  for (const className of classes) {
    if (!tokens.has(className)) dead.push({ file, className })
  }
}

const byFile = new Map()
for (const item of dead) {
  if (!byFile.has(item.file)) byFile.set(item.file, [])
  byFile.get(item.file).push(item.className)
}

if (process.argv.includes('--json')) {
  console.log(JSON.stringify({ dead }, null, 2))
} else {
  const total = [...styles.values()].reduce((sum, classes) => sum + classes.size, 0)
  console.log(`旧 CSS 定义类 ${total} 个，未在 src（styles 之外）发现引用 ${dead.length} 个。`)
  for (const [file, classes] of [...byFile.entries()].sort()) {
    console.log(`\n${file} (${classes.length})`)
    console.log(classes.sort().join(' '))
  }
  console.log(
    '\n提示：模板中的同名字符串可能让报告偏保守（误报“仍在使用”）；删除规则前仍需 grep 复核。',
  )
}
