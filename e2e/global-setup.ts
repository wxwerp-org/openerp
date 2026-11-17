import { spawn } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import { createInterface } from 'node:readline'
import { resolve } from 'node:path'
import { mkdir } from 'node:fs/promises'
import { startWxwStub } from './wxw-stub'

export default async function setup() {
  const mode = process.env.OPENERP_E2E_MODE ?? 'stub'
  if (!['stub', 'live'].includes(mode)) throw new Error('OPENERP_E2E_MODE must be stub or live.')
  const token = mode === 'live' ? process.env.OPENERP_TEST_ERP_TOKEN : 'e2e-wxw-token'
  if (!token?.trim()) throw new Error('Live wxw E2E requires OPENERP_TEST_ERP_TOKEN; it cannot be skipped.')
  process.env.OPENERP_E2E_MODE = mode
  process.env.OPENERP_TEST_ADMIN_PASSWORD = randomBytes(8).toString('hex')
  const stub = mode === 'stub' ? await startWxwStub(token) : undefined
  const diagnostics = resolve('test-results/aspire')
  await mkdir(diagnostics, { recursive: true })
  const host = spawn('dotnet', ['tests/OpenERP.E2EHost/bin/Debug/net10.0/OpenERP.E2EHost.dll'], {
    stdio: ['pipe', 'pipe', 'pipe'],
    env: { ...process.env, OPENERP_TEST_ERP_TOKEN: token,
      OPENERP_E2E_ERP_URL: stub?.url ?? '', OPENERP_CI_RESULTS_DIR: diagnostics },
  })
  const exited = new Promise<number | null>((resolve, reject) => {
    host.once('error', reject)
    host.once('exit', resolve)
  })
  // 保留退出结果供 teardown 检查，也避免启动阶段退出造成未处理 rejection。
  void exited.catch(() => {})
  host.stderr.on('data', data => process.stderr.write(data))
  const lines = createInterface({ input: host.stdout })
  const stop = async () => {
    host.stdin.end('\n')
    let timer: ReturnType<typeof setTimeout> | undefined
    try {
      const code = await Promise.race([exited, new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new Error('Aspire E2E host cleanup timed out.')), 90_000)
      })])
      if (code !== 0) throw new Error(`Aspire E2E host exited with ${code}; resource startup or cleanup failed.`)
    } catch (error) {
      if (host.exitCode === null && host.signalCode === null) {
        // 先请求 Aspire 正常清理；超时后只终止本次创建的测试进程，避免管道让运行器永远挂起。
        // 这不代表容器已成功清理，原始清理失败必须传回 Playwright。
        host.kill('SIGTERM')
        const terminate = setTimeout(() => host.kill('SIGKILL'), 5_000)
        try { await exited } catch { /* spawn 失败已包含在原始错误中。 */ }
        finally { clearTimeout(terminate) }
      }
      throw error
    } finally {
      clearTimeout(timer)
      lines.close()
      await stub?.close()
    }
  }
  let timer: ReturnType<typeof setTimeout> | undefined
  try {
    const ready = await Promise.race([
      new Promise<{ baseUrl: string; controlUrl: string; certificateFile: string }>((resolve, reject) => {
        lines.on('line', line => {
          if (line.startsWith('OPENERP_E2E_READY=')) {
            try { resolve(JSON.parse(line.slice('OPENERP_E2E_READY='.length))) }
            catch (error) { reject(error) }
          } else process.stdout.write(line + '\n')
        })
        timer = setTimeout(() => reject(new Error('Aspire E2E host did not become ready within 5 minutes.')), 300_000)
      }),
      exited.then(code => { throw new Error(`Aspire E2E host exited before readiness (${code}).`) }),
    ])
    process.env.PLAYWRIGHT_BASE_URL = ready.baseUrl
    process.env.OPENERP_E2E_CONTROL_URL = ready.controlUrl
    process.env.NODE_EXTRA_CA_CERTS = ready.certificateFile
    return stop
  } catch (error) {
    try { await stop() } catch (cleanup) { throw new AggregateError([error, cleanup], 'E2E startup and cleanup failed.', { cause: cleanup }) }
    throw error
  } finally { clearTimeout(timer) }
}
