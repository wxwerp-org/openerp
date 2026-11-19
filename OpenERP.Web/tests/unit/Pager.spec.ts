import { describe, expect, it } from 'vitest'
import { buildPageList } from '../../src/ui/pager'

describe('分页页码列表', () => {
  it('Given 只有一页 When 构建页码 Then 仅包含第 1 页', () => {
    expect(buildPageList(1, 1)).toEqual([1])
  })

  it('Given 四页且在第二页 When 构建页码 Then 页码连续且无省略号', () => {
    expect(buildPageList(2, 4)).toEqual([1, 2, 3, 4])
  })

  it('Given 十页且在第一页 When 构建页码 Then 保留首尾并省略中段', () => {
    expect(buildPageList(1, 10)).toEqual([1, 2, 3, 'ellipsis', 10])
  })

  it('Given 十页且在第五页 When 构建页码 Then 显示当前页前后两页并只省略真正跨越多页的中段', () => {
    expect(buildPageList(5, 10)).toEqual([1, 2, 3, 4, 5, 6, 7, 'ellipsis', 10])
  })

  it('Given 六页且在第二页 When 构建页码 Then 只差一页时直接补页码而不是省略号', () => {
    expect(buildPageList(2, 6)).toEqual([1, 2, 3, 4, 5, 6])
  })

  it('Given 十页且在末页 When 构建页码 Then 末端页码连续', () => {
    expect(buildPageList(10, 10)).toEqual([1, 'ellipsis', 8, 9, 10])
  })

  it('Given 十页且靠近首页 When 构建页码 Then 前段页码连续', () => {
    expect(buildPageList(3, 10)).toEqual([1, 2, 3, 4, 5, 'ellipsis', 10])
  })

  it('Given 越界当前页或无效页数 When 构建页码 Then 钳制到有效范围', () => {
    expect(buildPageList(0, 10)).toEqual([1, 2, 3, 'ellipsis', 10])
    expect(buildPageList(99, 10)).toEqual([1, 'ellipsis', 8, 9, 10])
    expect(buildPageList(2, 0)).toEqual([])
  })
})
