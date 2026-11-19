#!/usr/bin/env node
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

const [, , beforeDir, afterDir] = process.argv
if (!beforeDir || !afterDir) {
  console.error('用法: node scripts/compare-visual.mjs <before目录> <after目录>')
  process.exit(2)
}

function readProbe(dir) {
  const payload = JSON.parse(readFileSync(join(dir, 'probe.json'), 'utf8'))
  const scenarios = new Map()
  for (const scenario of payload.scenarios ?? []) scenarios.set(scenario.name, scenario)
  return scenarios
}

const before = readProbe(beforeDir)
const after = readProbe(afterDir)
const tolerance = Number(process.env.VISUAL_TOLERANCE ?? 4)
const failures = []

function px(value) {
  const parsed = Number.parseFloat(String(value))
  return Number.isFinite(parsed) ? parsed : 0
}

function compareNumber(label, left, right) {
  if (Math.abs(px(left) - px(right)) > tolerance) {
    failures.push(`${label}: ${left} -> ${right}`)
  }
}

function compareExact(label, left, right) {
  if (left !== right) failures.push(`${label}: ${JSON.stringify(left)} -> ${JSON.stringify(right)}`)
}

function compareMap(label, leftItems, rightItems, compares) {
  const leftMap = new Map(leftItems.map((item) => [item.key, item]))
  const rightMap = new Map(rightItems.map((item) => [item.key, item]))
  for (const key of leftMap.keys()) {
    if (!rightMap.has(key)) {
      failures.push(`${label} 缺失: ${key}`)
      continue
    }
    compares(`${label} ${key}`, leftMap.get(key), rightMap.get(key))
  }
  for (const key of rightMap.keys()) {
    if (!leftMap.has(key)) failures.push(`${label} 新增: ${key}`)
  }
}

function compareStyle(label, left, right) {
  for (const field of ['fontSize', 'lineHeight']) {
    compareNumber(`${label}.${field}`, left[field], right[field])
  }
  for (const field of [
    'fontWeight',
    'color',
    'background',
    'borderColor',
    'borderWidth',
    'borderRadius',
    'padding',
    'boxShadow',
    'opacity',
  ]) {
    compareExact(`${label}.${field}`, left[field], right[field])
  }
  compareNumber(`${label}.height`, left.height, right.height)
}

for (const name of new Set([...before.keys(), ...after.keys()])) {
  const left = before.get(name)
  const right = after.get(name)
  if (!left) {
    failures.push(`场景新增: ${name}`)
    continue
  }
  if (!right) {
    failures.push(`场景缺失: ${name}`)
    continue
  }
  compareMap(`${name} 控件`, left.probe.controls, right.probe.controls, compareStyle)
  compareMap(`${name} 排版`, left.probe.typography, right.probe.typography, (label, a, b) => {
    compareNumber(`${label}.fontSize`, a.fontSize, b.fontSize)
    compareNumber(`${label}.lineHeight`, a.lineHeight, b.lineHeight)
    compareExact(`${label}.fontWeight`, a.fontWeight, b.fontWeight)
    compareExact(`${label}.color`, a.color, b.color)
  })

  const leftLayout = left.probe.layout
  const rightLayout = right.probe.layout
  compareExact(`${name} innerWidth`, leftLayout.innerWidth, rightLayout.innerWidth)
  compareExact(`${name} innerHeight`, leftLayout.innerHeight, rightLayout.innerHeight)
  compareNumber(
    `${name} documentScrollWidth`,
    leftLayout.documentScrollWidth,
    rightLayout.documentScrollWidth,
  )
  compareNumber(
    `${name} bodyScrollHeight`,
    leftLayout.bodyScrollHeight,
    rightLayout.bodyScrollHeight,
  )
  for (const area of ['mainRect', 'asideRect', 'headerRect']) {
    const a = leftLayout[area]
    const b = rightLayout[area]
    if (!a || !b) {
      if (Boolean(a) !== Boolean(b)) failures.push(`${name} ${area} 存在性变化`)
      continue
    }
    for (const field of ['x', 'y', 'width', 'height']) {
      compareNumber(`${name} ${area}.${field}`, a[field], b[field])
    }
  }
  const leftRegions = new Map(leftLayout.regions.map((region) => [region.label, region]))
  const rightRegions = new Map(rightLayout.regions.map((region) => [region.label, region]))
  for (const [label, region] of leftRegions) {
    if (!rightRegions.has(label)) {
      failures.push(`${name} 滚动区缺失: ${label}`)
      continue
    }
    compareNumber(
      `${name} ${label}.scrollHeight`,
      region.scrollHeight,
      rightRegions.get(label).scrollHeight,
    )
    compareNumber(
      `${name} ${label}.clientHeight`,
      region.clientHeight,
      rightRegions.get(label).clientHeight,
    )
  }
  for (const label of rightRegions.keys()) {
    if (!leftRegions.has(label)) failures.push(`${name} 滚动区新增: ${label}`)
  }

  for (const palette of [
    'color',
    'background',
    'borderColor',
    'fontSize',
    'lineHeight',
    'borderRadius',
    'boxShadow',
  ]) {
    const leftValues = new Set(left.probe.palette[palette])
    const rightValues = new Set(right.probe.palette[palette])
    for (const value of leftValues) {
      if (!rightValues.has(value)) failures.push(`${name} 调色板移除 ${palette}: ${value}`)
    }
    for (const value of rightValues) {
      if (!leftValues.has(value)) failures.push(`${name} 调色板新增 ${palette}: ${value}`)
    }
  }
}

if (failures.length > 0) {
  console.error(`视觉探针差异 ${failures.length} 项（容差 ${tolerance}px）:`)
  for (const failure of failures.slice(0, 200)) console.error(`- ${failure}`)
  process.exit(1)
}
console.log(`视觉探针一致（${before.size} 个场景，容差 ${tolerance}px）。`)
