import { beforeEach, describe, expect, it, vi } from 'vitest'

const apiRequest = vi.fn()
vi.mock('@/utils/http/session', () => ({ apiRequest }))

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('请求封装 request', () => {
  beforeEach(() => apiRequest.mockReset())

  it('Given 查询参数含空值 When GET Then 只发送有值的参数并返回解析后的响应体', async () => {
    apiRequest.mockResolvedValue(json({ ok: true }))
    const { default: request } = await import('@/utils/http')

    const result = await request.get<{ ok: boolean }>({
      url: '/api/orders',
      params: { page: 2, platform: '', status: undefined, number: null, keyword: 'a b' }
    })

    expect(result).toEqual({ ok: true })
    expect(apiRequest).toHaveBeenCalledWith(
      '/api/orders?page=2&keyword=a+b',
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('Given 请求体 When POST Then 以 JSON 发送并带 Content-Type', async () => {
    apiRequest.mockResolvedValue(json({ id: 1 }, 201))
    const { default: request } = await import('@/utils/http')

    await request.post({ url: '/api/users', data: { username: 'alice' } })

    const [, init] = apiRequest.mock.calls[0] as [string, RequestInit]
    expect(init.method).toBe('POST')
    expect(init.body).toBe(JSON.stringify({ username: 'alice' }))
    expect(init.headers).toEqual({ 'Content-Type': 'application/json' })
  })

  it('Given 204 无内容 When 请求 Then 返回 undefined 而不是解析空体', async () => {
    apiRequest.mockResolvedValue(new Response(null, { status: 204 }))
    const { default: request } = await import('@/utils/http')

    await expect(request.del({ url: '/api/users/1' })).resolves.toBeUndefined()
  })

  it('Given 服务端返回 ProblemDetails When 失败 Then 抛出带 detail 文案和状态码的 ApiError', async () => {
    apiRequest.mockResolvedValue(json({ detail: '用户名已存在' }, 409))
    const { default: request, ApiError } = await import('@/utils/http')

    const error = await request.post({ url: '/api/users', data: {} }).catch((cause) => cause)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ message: '用户名已存在', status: 409 })
  })

  it('Given 校验错误集合 When 失败 Then 合并为一条文案', async () => {
    apiRequest.mockResolvedValue(
      json({ errors: { username: ['过短'], password: ['过简单'] } }, 400)
    )
    const { default: request } = await import('@/utils/http')

    await expect(request.post({ url: '/api/users', data: {} })).rejects.toThrow('过短 过简单')
  })

  it('Given 响应没有可读错误体 When 失败 Then 使用调用方的兜底文案', async () => {
    apiRequest.mockResolvedValue(new Response('boom', { status: 500 }))
    const { default: request } = await import('@/utils/http')

    await expect(
      request.get({ url: '/api/x', fallbackMessage: '无法加载订单，请稍后重试。' })
    ).rejects.toThrow('无法加载订单，请稍后重试。')
  })
})
