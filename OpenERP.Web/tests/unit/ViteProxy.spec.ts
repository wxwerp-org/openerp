// @vitest-environment node

import { execFileSync } from 'node:child_process'
import { once } from 'node:events'
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { createServer as createHttpServer } from 'node:http'
import { createServer as createHttpsServer } from 'node:https'
import type { Server } from 'node:net'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createServer as createViteServer } from 'vite'
import { afterEach, describe, expect, it, onTestFinished, vi } from 'vitest'

const host = '127.0.0.1'
const apiPath = '/api/proxy-test?source=web'
const webRoot = fileURLToPath(new URL('../../', import.meta.url))

function serverUrl(server: Server | null, protocol = 'http') {
  const address = server?.address()
  if (!address || typeof address === 'string') throw new Error('测试服务未监听 TCP 端口')
  return `${protocol}://${host}:${address.port}`
}

async function startUpstream(server: Server, protocol = 'http') {
  server.listen(0, host)
  await once(server, 'listening')
  onTestFinished(() => new Promise<void>((resolve, reject) => {
    server.close((error) => error ? reject(error) : resolve())
  }))
  return serverUrl(server, protocol)
}

function startHttpUpstream(name: string) {
  return startUpstream(createHttpServer((request, response) => {
    response.setHeader('Content-Type', 'application/json')
    response.end(JSON.stringify({ name, path: request.url, host: request.headers.host }))
  }))
}

async function requestApi(apiHttps?: string, apiBaseUrl?: string) {
  vi.stubEnv('API_HTTPS', apiHttps)
  vi.stubEnv('API_BASE_URL', apiBaseUrl)

  const vite = await createViteServer({
    root: webRoot,
    configFile: join(webRoot, 'vite.config.ts'),
    logLevel: 'silent',
    optimizeDeps: { noDiscovery: true },
    server: { host, port: 0, watch: null, ws: false },
  })
  onTestFinished(() => vite.close())
  await vite.listen()

  return fetch(`${serverUrl(vite.httpServer)}${apiPath}`, {
    headers: { Accept: 'application/json' },
    signal: AbortSignal.timeout(3_000),
  })
}

function createSelfSignedCertificate() {
  const directory = mkdtempSync(join(tmpdir(), 'openerp-vite-tls-'))
  const keyFile = join(directory, 'key.pem')
  const certFile = join(directory, 'cert.pem')
  try {
    execFileSync('openssl', [
      'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1', '-subj', `/CN=${host}`,
      '-addext', `subjectAltName=IP:${host}`, '-keyout', keyFile, '-out', certFile,
    ], { stdio: 'pipe', timeout: 5_000 })
    return { key: readFileSync(keyFile), cert: readFileSync(certFile) }
  } finally {
    rmSync(directory, { recursive: true, force: true })
  }
}

describe('Vite 开发代理', () => {
  afterEach(() => vi.unstubAllEnvs())

  it('Given 两个上游地址均已设置 When 请求 /api Then 优先转发至 API_HTTPS 并保留路径和查询串', async () => {
    const preferred = await startHttpUpstream('preferred')
    const legacy = await startHttpUpstream('legacy')

    const response = await requestApi(preferred, legacy)

    expect(response.status).toBe(200)
    await expect(response.json()).resolves.toEqual({
      name: 'preferred',
      path: apiPath,
      host: new URL(preferred).host,
    })
  })

  it('Given 仅设置 API_HTTPS When 请求 /api Then 转发至该上游', async () => {
    const preferred = await startHttpUpstream('preferred')

    const response = await requestApi(preferred)

    expect(response.status).toBe(200)
    await expect(response.json()).resolves.toMatchObject({ name: 'preferred' })
  })

  it('Given 仅设置 API_BASE_URL When 请求 /api Then 转发至旧地址', async () => {
    const legacy = await startHttpUpstream('legacy')

    const response = await requestApi(undefined, legacy)

    expect(response.status).toBe(200)
    await expect(response.json()).resolves.toMatchObject({ name: 'legacy' })
  })

  it('Given 未设置上游地址 When 请求 /api Then Vite 返回 404', async () => {
    const response = await requestApi()

    expect(response.status).toBe(404)
  })

  it('Given HTTPS 上游使用不受信任的自签证书 When 请求 /api Then 代理拒绝连接', async () => {
    let receivedRequests = 0
    const upstream = await startUpstream(createHttpsServer(createSelfSignedCertificate(), (_request, response) => {
      receivedRequests++
      response.end('上游受理了请求')
    }), 'https')

    const response = await requestApi(undefined, upstream)

    expect(response.status).toBe(502)
    expect(receivedRequests).toBe(0)
  })
})
