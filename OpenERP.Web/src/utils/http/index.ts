/**
 * HTTP 请求封装
 *
 * 对接 OpenERP.Api：Bearer 访问令牌 + CSRF 防伪令牌 + Cookie 刷新令牌，均由 `session.ts` 负责。
 * 对外保持 SpiderUI 的调用方式：`request.get<T>({ url, params })`，成功返回已解析的响应体，
 * 失败抛出带 ProblemDetails `detail` 文案的 `ApiError`。
 *
 * @module utils/http
 */

import { apiRequest } from './session'

/** 请求失败：携带 HTTP 状态码与服务端返回的错误文案 */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

type Query = Record<string, string | number | boolean | null | undefined>

export interface RequestConfig {
  url: string
  /** GET 查询参数；空值（undefined、null、空串）自动忽略 */
  params?: Query
  /** POST / PUT 的 JSON 请求体 */
  data?: unknown
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  signal?: AbortSignal
  /** 失败时的兜底文案 */
  fallbackMessage?: string
}

function buildUrl(url: string, params?: Query): string {
  if (!params) return url
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue
    search.set(key, String(value))
  }
  const query = search.toString()
  return query ? `${url}?${query}` : url
}

/** 读取 ProblemDetails 的 detail 或校验错误，缺省使用兜底文案 */
export async function readErrorMessage(response: Response, fallback: string): Promise<string> {
  const problem = await response.json().catch(() => null)
  if (typeof problem?.detail === 'string') return problem.detail
  const errors = Object.values(problem?.errors ?? {})
    .flat()
    .filter((message): message is string => typeof message === 'string')
  return errors.length ? errors.join(' ') : fallback
}

/** 发送请求并返回原始 Response，由调用方自行判断状态（如需要区分 204 / 409） */
export function requestRaw(config: RequestConfig): Promise<Response> {
  const init: RequestInit = { method: config.method ?? 'GET', signal: config.signal }
  if (config.data !== undefined) {
    init.headers = { 'Content-Type': 'application/json' }
    init.body = JSON.stringify(config.data)
  }
  return apiRequest(buildUrl(config.url, config.params), init)
}

async function request<T = unknown>(config: RequestConfig): Promise<T> {
  const response = await requestRaw(config)
  if (!response.ok) {
    const fallback = config.fallbackMessage ?? '请求失败，请稍后重试。'
    throw new ApiError(await readErrorMessage(response, fallback), response.status)
  }
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

type Shortcut = <T = unknown>(config: Omit<RequestConfig, 'method'>) => Promise<T>

request.get = ((config) => request({ ...config, method: 'GET' })) as Shortcut
request.post = ((config) => request({ ...config, method: 'POST' })) as Shortcut
request.put = ((config) => request({ ...config, method: 'PUT' })) as Shortcut
request.del = ((config) => request({ ...config, method: 'DELETE' })) as Shortcut

export default request
