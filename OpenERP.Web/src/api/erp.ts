/** ERP 接入配置接口（OpenERP.Api `/api/erp/config`） */
import request, { requestRaw } from '@/utils/http'

export interface ErpConfigState {
  configured: boolean
  /** 是否独立部署（false 为公共版） */
  independent: boolean
  /** 当前上游地址 */
  baseUrl: string
}

/** 读取 ERP 接入配置状态；Token 本身不会返回 */
export async function fetchErpConfig(): Promise<ErpConfigState> {
  const config = await request.get<{
    configured?: unknown
    independent?: unknown
    baseUrl?: unknown
  }>({ url: '/api/erp/config' })
  if (typeof config?.configured !== 'boolean') throw new Error('读取失败')
  return {
    configured: config.configured,
    independent: config.independent === true,
    baseUrl: typeof config.baseUrl === 'string' ? config.baseUrl : ''
  }
}

/** 读取 ERP AccessToken 是否已配置 */
export async function fetchErpConfigured(): Promise<boolean> {
  return (await fetchErpConfig()).configured
}

/** 向上游 ERP 检测 AccessToken 可用性（不保存）；baseUrl 为空表示公共版 */
export async function validateErpToken(
  accessToken: string,
  baseUrl?: string
): Promise<{ valid: boolean; message: string }> {
  const result = await request.post<{ valid?: unknown; message?: unknown }>({
    url: '/api/erp/config/validate',
    data: { accessToken, baseUrl }
  })
  if (typeof result?.valid !== 'boolean') throw new Error('检测失败，请重试。')
  return { valid: result.valid, message: typeof result.message === 'string' ? result.message : '' }
}

/** 撤销 ERP 接入配置，成功返回 204 */
export async function revokeErpConfig(): Promise<void> {
  const response = await requestRaw({ url: '/api/erp/config', method: 'DELETE' })
  if (response.status !== 204) throw new Error('撤销失败，请重试。')
}

/** 保存新的 ERP AccessToken，成功返回 204 */
export async function saveErpToken(accessToken: string, baseUrl?: string): Promise<void> {
  const response = await requestRaw({
    url: '/api/erp/config',
    method: 'PUT',
    data: { accessToken, baseUrl }
  })
  if (response.status !== 204) throw new Error('保存失败，请重试。')
}

/** 平台接口分类 */
export interface PlatformAbilityCategory {
  id: number
  name: string
  count: number
  frozen: boolean
  frozenReason?: string | null
}

/** 平台官方 API 条目；schemaName 为空表示没有可读取的 Schema */
export interface PlatformAbilityApi {
  name: string
  api: string
  method: string
  category: string
  categoryId: number
  description: string
  frozen: boolean
  frozenReason?: string | null
  schemaName?: string | null
}

/** 一个平台的 API 能力：代理入口信息 + 官方接口清单（上游未提供接口索引时 apis 为空） */
export interface PlatformAbility {
  key: string
  name: string
  description: string
  officialUrl?: string | null
  officialUrlText?: string | null
  apiPath: string
  requestExample?: string | null
  steps: string[]
  categories: PlatformAbilityCategory[]
  apis: PlatformAbilityApi[]
}

/** 平台接口 Schema 字段 */
export interface PlatformSchemaField {
  name: string
  type?: string
  required?: string
  description?: string
}

/** 平台接口 Schema（读取自旺销王开放 API） */
export interface PlatformApiSchema {
  name: string
  description?: string
  notice?: string
  platformApi?: { api?: string; method?: string; sourceUrl?: string }
  requestFields?: PlatformSchemaField[]
  responseFields?: PlatformSchemaField[]
  requestExample?: unknown
  responseExample?: unknown
}

/** 读取各平台 API 能力清单 */
export async function fetchPlatformAbilities(): Promise<PlatformAbility[]> {
  const items = await request.get<PlatformAbility[]>({ url: '/api/erp/platform-abilities' })
  if (!Array.isArray(items)) throw new Error('平台能力读取失败')
  return items
}

/** 读取某个平台接口的 Schema */
export async function fetchPlatformApiSchema(
  platform: string,
  name: string
): Promise<PlatformApiSchema> {
  return request.get<PlatformApiSchema>({
    url: `/api/erp/platform-abilities/${encodeURIComponent(platform)}/schemas/${encodeURIComponent(name)}`
  })
}
