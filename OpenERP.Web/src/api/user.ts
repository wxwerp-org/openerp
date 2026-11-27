/** 用户管理接口（OpenERP.Api `/api/users`） */
import request from '@/utils/http'
import type { Account } from '@/utils/http/session'

export type ManagedUser = Omit<Account, 'expiresAt'> & { createdAt: string | null }

export interface UserFields {
  username: string
  name: string
  phone: string | null
  email: string | null
}

export function fetchUsers() {
  return request.get<ManagedUser[]>({
    url: '/api/users',
    fallbackMessage: '无法读取用户，请重试。'
  })
}

export function fetchUser(id: string, signal?: AbortSignal) {
  return request.get<ManagedUser>({
    url: `/api/users/${id}`,
    signal,
    fallbackMessage: '无法读取用户详情，请重试。'
  })
}

export function createUser(fields: UserFields & { password: string }) {
  return request.post<ManagedUser>({
    url: '/api/users',
    data: fields,
    fallbackMessage: '无法添加用户，请重试。'
  })
}

export function updateUser(id: string, fields: UserFields) {
  return request.put<ManagedUser>({
    url: `/api/users/${id}`,
    data: fields,
    fallbackMessage: '无法保存用户，请重试。'
  })
}

export function deleteUser(id: string) {
  return request.del<void>({ url: `/api/users/${id}`, fallbackMessage: '无法删除用户，请重试。' })
}
