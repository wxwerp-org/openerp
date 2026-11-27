import { describe, expect, it } from 'vitest'
import { RoutePermissionValidator } from '@/router/core/RoutePermissionValidator'

describe('RoutePermissionValidator', () => {
  const menu = [
    {
      path: '/system',
      name: 'System',
      meta: { title: 'x', isHide: true },
      children: [{ path: '/system/erp-binding', name: 'B', meta: { title: 'y', isHide: true } }]
    }
  ]

  it('Given 菜单含 isHide 的页面 When 刷新直达该路径 Then 有权限，不回退首页', () => {
    expect(RoutePermissionValidator.validatePath('/system/erp-binding', menu, '/home')).toEqual({
      path: '/system/erp-binding',
      hasPermission: true
    })
  })

  it('Given 菜单中没有的路径 When 校验 Then 回退首页', () => {
    expect(RoutePermissionValidator.validatePath('/nope', [], '/home').hasPermission).toBe(false)
  })
})
