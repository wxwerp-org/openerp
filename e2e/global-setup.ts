import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { randomBytes } from 'node:crypto'
import { realpathSync } from 'node:fs'
import { startWxwStub } from './wxw-stub'

const exec = promisify(execFile)
const appHost = 'OpenERP.AppHost/OpenERP.AppHost.csproj'

type Resource = {
  displayName: string
  urls: { url: string }[]
  environment: Record<string, string>
}

export default async function setup() {
  const mode = process.env.OPENERP_E2E_MODE ?? 'stub'
  if (mode !== 'stub' && mode !== 'live') throw new Error('OPENERP_E2E_MODE must be stub or live.')
  const token = mode === 'live' ? process.env.OPENERP_TEST_ERP_TOKEN : 'e2e-wxw-token'
  if (!token?.trim()) throw new Error('Live wxw E2E requires OPENERP_TEST_ERP_TOKEN.')
  const running = await exec('aspire', ['ps', '--format', 'Json', '--non-interactive'])
  const hosts: { appHostPath: string }[] = JSON.parse(running.stdout)
  if (hosts.some(host => realpathSync(host.appHostPath) === realpathSync(appHost)))
    throw new Error('Stop the AppHost in this checkout before running E2E; tests must not replace a development session.')
  const stub = mode === 'stub' ? await startWxwStub(token) : undefined
  const aspire = (...args: string[]) => exec('aspire', [
    ...args, '--apphost', appHost, '--non-interactive',
  ], { timeout: 300_000, maxBuffer: 4 * 1024 * 1024, env: {
    ...process.env, E2E__Mode: mode, 'Parameters__wxw-erp-access-token': token, E2E__WxwUrl: stub?.url ?? '',
  } })
  const stop = async () => {
    try { await aspire('stop') }
    finally { await stub?.close() }
  }
  try {
    await aspire('start', '--no-build', '--isolated')
    await aspire('wait', 'web')
    const { stdout } = await aspire('describe', '--format', 'Json')
    const { resources }: { resources: Resource[] } = JSON.parse(stdout)
    const web = resources.find(resource => resource.displayName === 'web')
    const api = resources.find(resource => resource.displayName === 'api')
    const url = web?.urls.find(endpoint => endpoint.url.startsWith('https:'))?.url
    if (!url || !api?.environment.ConnectionStrings__openerp || !web?.environment.NODE_EXTRA_CA_CERTS)
      throw new Error('Aspire did not provide the web HTTPS endpoint, database connection or Node CA configuration.')
    process.env.PLAYWRIGHT_BASE_URL = url
    process.env.OPENERP_TEST_ADMIN_PASSWORD = randomBytes(8).toString('hex')
    process.env.OPENERP_E2E_MODE = mode
    // 复用 Aspire 已生成的连接串和 Node CA 配置；不导出证书、不记录环境变量。
    process.env.OPENERP_E2E_POSTGRES = api.environment.ConnectionStrings__openerp
    process.env.NODE_EXTRA_CA_CERTS = web.environment.NODE_EXTRA_CA_CERTS
    return stop
  } catch (error) {
    try { await stop() }
    catch (cleanup) { throw new AggregateError([error, cleanup], 'Aspire setup and cleanup failed.', { cause: cleanup }) }
    throw error
  }
}
