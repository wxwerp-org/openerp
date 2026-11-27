import type { RouteLocationRaw, Router } from 'vue-router'

/**
 * 在新的浏览器页面中打开站内路由。
 * 列表里的「详情」「同步」等跳转一律使用它，不在当前页面直接跳转。
 */
export function openInNewPage(router: Router, to: RouteLocationRaw): void {
  const { href } = router.resolve(to)
  window.open(href, '_blank', 'noopener')
}
