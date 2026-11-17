import type { FullResult, Reporter, Suite } from '@playwright/test/reporter'

// Playwright 原生结果仍决定失败；这里只补足它没有的“跳过即失败”。
export default class RequireExecution implements Reporter {
  private suite?: Suite
  onBegin(_: unknown, suite: Suite) { this.suite = suite }
  async onEnd(result: FullResult): Promise<{ status: 'failed' } | undefined> {
    const tests = this.suite?.allTests() ?? []
    if (!tests.length || tests.some(test => test.outcome() === 'skipped')) {
      console.error('E2E must execute tests without skips; see the native Playwright report.')
      return { status: 'failed' }
    }
    if (result.status !== 'passed') return { status: 'failed' }
  }
}
