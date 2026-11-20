import type { RouteLocationNormalized, RouteLocationRaw, Router } from 'vue-router'
import { account, type SessionState } from '../account/session'

function isAuthRoute(name: unknown): boolean {
  return name === 'login' || name === 'initialize' || name === 'legacy-password-change'
}

export function sessionRedirect(
  router: Router,
  to: RouteLocationNormalized,
  state: SessionState,
): RouteLocationRaw | undefined {
  const authRoute = isAuthRoute(to.name)
  const destination = authRoute ? returnDestination(router, to.query.returnTo) : to.fullPath
  if (state === 'uninitialized' && to.name !== 'initialize') {
    return { name: 'initialize', query: { returnTo: destination }, replace: true }
  }
  if (state === 'anonymous' && to.name !== 'login') {
    const query =
      to.name === 'initialize'
        ? { returnTo: destination, initialized: '1' }
        : { returnTo: destination }
    return { name: 'login', query, replace: true }
  }
  if (state === 'authenticated' && authRoute) return destination
  if (
    state === 'authenticated' &&
    to.meta.administratorOnly &&
    account.value?.role !== 'Administrator'
  )
    return { name: 'dashboard', replace: true }
}

export function returnDestination(router: Router, value: unknown): string {
  if (
    typeof value !== 'string' ||
    !value.startsWith('/') ||
    value.startsWith('//') ||
    value.includes('\\')
  )
    return '/'
  const target = new URL(value, window.location.origin)
  if (target.origin !== window.location.origin) return '/'
  if (
    !router.getRoutes().some((route) => route.path === target.pathname && !isAuthRoute(route.name))
  )
    return '/'
  return target.pathname + target.search + target.hash
}
