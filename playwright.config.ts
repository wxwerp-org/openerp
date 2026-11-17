import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  // 初始化、账户变更和工作台共用同一隔离宿主，按文件和声明顺序运行。
  workers: 1,
  fullyParallel: false,
  forbidOnly: true,
  retries: 0,
  timeout: 60_000,
  globalTimeout: 20 * 60_000,
  reporter: [
    ['line'],
    ['json', { outputFile: 'test-results/results.json' }],
    ['html', { open: 'never' }],
  ],
  use: {
    browserName: 'chromium',
    viewport: { width: 1440, height: 900 },
    trace: process.env.OPENERP_E2E_MODE === 'live' ? 'off' : 'retain-on-failure',
  },
})
