/**
 * 认证与账号接口（OpenERP.Api）
 *
 * 登录、初始化、刷新、退出的令牌处理在 `@/utils/http/session` 内完成，这里只做转发并保持页面调用简单。
 */
import request from '@/utils/http'
import {
  account,
  changePassword,
  checkSession,
  initialize,
  login,
  logout,
  saveProfile,
  sessionState,
  initializationCompleted,
  type Account
} from '@/utils/http/session'

export {
  account,
  changePassword,
  checkSession,
  initialize,
  initializationCompleted,
  login,
  logout,
  saveProfile,
  sessionState
}
export type { Account }

/** 关于信息（版本、构建等） */
export function fetchAbout<T = Record<string, unknown>>() {
  return request.get<T>({ url: '/api/about' })
}
