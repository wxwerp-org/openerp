import { describe, expect, it } from 'vitest'
import { platformColor, platformInitial } from '@/utils/platform'

describe('平台展示约定', () => {
  it('Given 平台名称 When 取 logo Then 使用名称首字母大写', () => {
    expect(platformInitial('TemuPop')).toBe('T')
    expect(platformInitial('amazon')).toBe('A')
    expect(platformInitial('  shopee')).toBe('S')
  })

  it('Given 空名称 When 取 logo Then 退回问号', () => {
    expect(platformInitial('')).toBe('?')
    expect(platformInitial('   ')).toBe('?')
  })

  it('Given 名称以中文开头 When 取 logo Then 不会拆坏字符', () => {
    expect(platformInitial('旺销')).toBe('旺')
  })

  it('Given 已知平台 When 取颜色 Then 按名称前缀匹配品牌色，不区分大小写', () => {
    expect(platformColor('TemuPop')).toBe('#FF6A00')
    expect(platformColor('temu')).toBe('#FF6A00')
    expect(platformColor('Amazon')).toBe(platformColor('AMAZON'))
  })

  it('Given 未知平台 When 多次取颜色 Then 同一名称颜色稳定，且是十六进制色值', () => {
    const first = platformColor('SomeNewPlatform')

    expect(platformColor('SomeNewPlatform')).toBe(first)
    expect(first).toMatch(/^#[0-9A-F]{6}$/i)
  })

  it('Given 不同的未知平台 When 取颜色 Then 都能得到合法颜色', () => {
    for (const name of ['Alpha', 'Beta', 'Gamma', 'Delta', 'Epsilon', 'Zeta', 'Eta']) {
      expect(platformColor(name)).toMatch(/^#[0-9A-F]{6}$/i)
    }
  })
})
