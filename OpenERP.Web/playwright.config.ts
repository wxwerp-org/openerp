import { defineConfig } from '@playwright/test'

const externalBaseUrl = process.env.PLAYWRIGHT_BASE_URL

export default defineConfig({
  // 同一套用例共用唯一管理员，改密用例恢复原密码后再执行其他用例。
  workers: 1,
  testDir: './tests/e2e',
  use: {
    baseURL: externalBaseUrl ?? 'http://127.0.0.1:4173',
    browserName: 'chromium',
    viewport: { width: 1440, height: 900 },
  },
  // 未指定外部地址时由 Playwright 构建并启动预览服务；真实宿主入口会传入 PLAYWRIGHT_BASE_URL。
  webServer: externalBaseUrl
    ? undefined
    : {
        command: 'pnpm build && pnpm preview --host 127.0.0.1 --port 4173 --strictPort',
        url: 'http://127.0.0.1:4173',
        reuseExistingServer: !process.env.CI,
        stdout: 'pipe',
        timeout: 120_000,
      },
})
