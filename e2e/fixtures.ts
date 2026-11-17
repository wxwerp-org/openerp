import { test as base } from '@playwright/test'

export { expect } from '@playwright/test'
export type { Page } from '@playwright/test'

export const test = base.extend({
  // Playwright 用参数解构声明 fixture 依赖，此 fixture 不依赖其他 fixture。
  // eslint-disable-next-line no-empty-pattern
  baseURL: async ({}, use) => {
    const url = process.env.PLAYWRIGHT_BASE_URL
    if (!url || !process.env.OPENERP_TEST_ADMIN_PASSWORD)
      throw new Error('The isolated Aspire E2E host did not provide its endpoint and administrator password.')
    await use(url)
  },
})
