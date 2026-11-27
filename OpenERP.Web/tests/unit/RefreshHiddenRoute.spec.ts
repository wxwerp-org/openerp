import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'

const { account, sessionState } = vi.hoisted(() => ({
  account: { value: null as unknown },
  sessionState: { value: 'authenticated' }
}))
vi.mock('@/api/auth', () => ({ account, sessionState, checkSession: vi.fn() }))

// 回归：不在侧边菜单里的页面（isHide）刷新后曾因权限检查被重定向到首页
describe('刷新直达隐藏页', () => {
  it('Given 管理员刷新 /system/erp-binding When 路由守卫初始化 Then 停留在该页', async () => {
    setActivePinia(createPinia())
    account.value = {
      id: '1',
      username: 'admin',
      name: 'admin',
      role: 'Administrator',
      email: null,
      phone: null,
      lastLoginAt: null
    }
    const { staticRoutes } = await import('@/router/routes/staticRoutes')
    const { setupBeforeEachGuard } = await import('@/router/guards/beforeEach')
    const router = createRouter({ history: createMemoryHistory(), routes: staticRoutes as never })
    setupBeforeEachGuard(router)

    await router.push('/system/erp-binding')

    expect(router.currentRoute.value.path).toBe('/system/erp-binding')
  }, 30_000)
})
