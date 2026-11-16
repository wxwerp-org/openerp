import { defineConfig } from '@playwright/test'

export default defineConfig({
  // 同一套用例共用唯一管理员，改密用例恢复原密码后再执行其他用例。
  workers: 1,
  testDir: './tests/e2e',
  use: {
    baseURL: process.env.PLAYWRIGHT_BASE_URL,
    browserName: 'chromium',
    viewport: { width: 1440, height: 900 },
  },
})
