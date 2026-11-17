import { defineConfig } from '@playwright/test'

const externalBaseUrl = process.env.PLAYWRIGHT_BASE_URL

export default defineConfig({
  // 按文件和声明顺序串行执行。真实宿主先运行 authentication.spec.ts 的初始化场景，再共享账号。
  workers: 1,
  testDir: './tests/e2e',
  use: {
    baseURL: externalBaseUrl ?? 'http://127.0.0.1:4173',
    browserName: 'chromium',
    viewport: { width: 1440, height: 900 },
    // 失败时保留截图与 trace；CI 上传的 HTML 报告会包含这些现场。
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
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
