import type { RouteLocationRaw, Router } from 'vue-router'

export function loginRoute(fullPath: string): RouteLocationRaw {
  return { name: 'login', query: { returnTo: fullPath }, replace: true }
}

export function passwordChangeRoute(fullPath: string): RouteLocationRaw {
  return { name: 'required-password-change', query: { returnTo: fullPath }, replace: true }
}

export function isAuthRoute(name: unknown): boolean {
  return name === 'login' || name === 'required-password-change'
}

export function returnDestination(router: Router, value: unknown): string {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) return '/'
  const target = new URL(value, window.location.origin)
  if (target.origin !== window.location.origin) return '/'
  if (!router.getRoutes().some((route) => route.path === target.pathname && !isAuthRoute(route.name))) return '/'
  return target.pathname + target.search + target.hash
}
