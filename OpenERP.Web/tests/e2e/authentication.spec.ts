import { randomBytes } from 'node:crypto'
import { expect, test, type Page } from '@playwright/test'
import { adminPassword, csrfHeaders, signInForWorkspace, signInWithApi } from './authentication'

// eslint-disable-next-line playwright/no-skipped-test -- 真实会话用例使用隔离宿主和测试设置的密码。
test.skip(!adminPassword, '设置 OPENERP_TEST_ADMIN_PASSWORD 以使用真实管理员会话。')

async function restorePassword(page: Page, newPassword: string): Promise<number> {
  const login = await signInWithApi(page.request, newPassword)
  if (!login.ok()) {
    const initialLogin = await signInWithApi(page.request)
    return initialLogin.ok() ? 204 : initialLogin.status()
  }
  const restored = await page.request.post('/api/auth/change-password', {
    headers: await csrfHeaders(page.request),
    data: { oldPassword: newPassword, newPassword: adminPassword },
  })
  return restored.status()
}

async function submitLogin(page: Page, password: string): Promise<void> {
  await page.getByLabel('用户名').fill('admin')
  await page.getByLabel('密码', { exact: true }).fill(password)
  await page.getByRole('button', { name: '登录', exact: true }).click()
}

test('Given 全新数据库尚未初始化 When 浏览器设置管理员密码 Then 重新登录后返回原地址且其他页签转到登录', async ({ page, browser, baseURL }, testInfo) => {
  const secondDevice = await browser.newContext({ baseURL })
  try {
    const status = await page.request.get('/api/auth/status')
    await expect(status).toBeOK()
    expect(status.headers()['cache-control']).toContain('no-store')
    expect(await status.json()).toEqual({ initialized: false })

    const blocked = await page.request.get('/api/ping')
    expect(blocked.status()).toBe(409)
    expect((await blocked.json()).code).toBe('initialization_required')
    const preInitializationLogin = await signInWithApi(secondDevice.request)
    expect(preInitializationLogin.status()).toBe(409)
    expect((await preInitializationLogin.json()).code).toBe('initialization_required')

    const secondPage = await secondDevice.newPage()
    await secondPage.goto('/orders')
    await expect(secondPage.getByRole('heading', { name: '初始化 OpenERP' })).toBeVisible()

    await page.goto('/users?first=1#main')
    await expect(page).toHaveURL(/\/initialize\?returnTo=/)
    await expect(page.getByRole('heading', { name: '初始化 OpenERP' })).toBeVisible()
    await expect(page.locator('.sidebar')).toHaveCount(0)
    await expect(page.getByLabel('用户名')).toHaveValue('admin')
    await expect(page.getByLabel('用户名')).not.toBeEditable()
    await expect(page.getByLabel('原密码')).toHaveCount(0)
    await page.screenshot({ path: testInfo.outputPath('initialize.png') })

    await page.reload()
    await expect(page.getByRole('heading', { name: '初始化 OpenERP' })).toBeVisible()
    await page.getByLabel('新密码', { exact: true }).fill(adminPassword)
    await page.getByLabel('确认新密码').fill(adminPassword)
    await page.getByRole('button', { name: '设置管理员密码' }).click()
    await expect(page.getByRole('heading', { name: '登录 OpenERP' })).toBeVisible()
    await expect(page.getByRole('status')).toHaveText('管理员密码初始化成功，请登录。')
    await page.screenshot({ path: testInfo.outputPath('initialized-login.png') })
    expect((await page.request.get('/api/auth/me')).status()).toBe(401)

    const duplicate = await secondDevice.request.post('/api/auth/initialize', {
      headers: await csrfHeaders(secondDevice.request),
      data: { newPassword: 'another-password' },
    })
    expect(duplicate.status()).toBe(409)
    expect((await duplicate.json()).code).toBe('already_initialized')
    await secondPage.evaluate(() => window.dispatchEvent(new Event('focus')))
    await expect(secondPage.getByRole('heading', { name: '登录 OpenERP' })).toBeVisible()
    await expect(secondPage.getByRole('status')).toHaveText('管理员账号已初始化，请登录。')

    await submitLogin(page, adminPassword)
    await expect(page).toHaveURL(/\/users\?first=1#main$/)
    await expect(page.locator('.account-button')).toBeVisible()
  } finally {
    await secondDevice.close()
  }
})

test('Given 未登录时直达工作台 When 错误凭证失败后成功登录 Then 返回原地址且刷新后保持会话', async ({ page, request }, testInfo) => {
  await signInForWorkspace(request)
  await page.goto('/users?from=login#main')
  await expect(page).toHaveURL(/\/login\?returnTo=/)
  await expect(page.getByRole('heading', { name: '登录 OpenERP' })).toBeVisible()
  await expect(page.locator('.sidebar')).toHaveCount(0)
  await page.screenshot({ path: testInfo.outputPath('login.png') })

  await submitLogin(page, 'wrong-password')
  await expect(page.getByRole('alert')).toBeVisible()
  await expect(page).toHaveURL(/\/login\?returnTo=/)
  await submitLogin(page, adminPassword)
  await expect(page).toHaveURL(/\/users\?from=login#main$/)
  await expect(page.locator('#userTableBody tr')).toHaveCount(4)
  await page.reload()
  await expect(page.locator('.account-button')).toBeVisible()
  await expect(page).toHaveURL(/\/users\?from=login#main$/)
})

test('Given 两个浏览器已登录 When 保存资料然后退出当前浏览器 Then 资料刷新后保持且另一浏览器继续在线', async ({ page, browser, baseURL }, testInfo) => {
  await signInForWorkspace(page.request)
  const me = await page.request.get('/api/auth/me')
  expect(me.status()).toBe(200)
  const { name, phone, email } = await me.json()
  const secondDevice = await browser.newContext({ baseURL })
  try {
    expect((await signInWithApi(secondDevice.request)).status()).toBe(200)
    await page.goto('/')
    await page.locator('.account-button').click()
    await page.getByRole('menuitem', { name: '修改个人信息' }).click()
    await expect(page.getByLabel('用户名')).toHaveValue('admin')
    await expect(page.getByLabel('用户名')).not.toBeEditable()
    await expect(page.locator('#profileLayer time')).toHaveAttribute('datetime', /^\d{4}-/)
    await page.screenshot({ path: testInfo.outputPath('profile.png') })
    const newName = '验收管理员'
    await page.getByLabel('姓名').fill(newName)
    await page.getByLabel('手机号').fill('13900001111')
    await page.getByLabel('邮箱').fill('invalid-address')
    await page.getByRole('button', { name: '保存' }).click()
    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByText('请输入有效的邮箱地址')).toBeVisible()
    await page.getByLabel('邮箱').fill('admin@example.test')
    await page.getByRole('button', { name: '保存' }).click()
    await expect(page.getByRole('dialog')).toBeHidden()
    await expect(page.locator('.account-button')).toHaveText(newName)
    await page.reload()
    await expect(page.locator('.account-button')).toHaveText(newName)

    await page.locator('.account-button').click()
    await page.getByRole('menuitem', { name: '退出登录' }).click()
    await expect(page.getByRole('heading', { name: '登录 OpenERP' })).toBeVisible()
    expect((await page.request.get('/api/auth/me')).status()).toBe(401)
    expect((await secondDevice.request.get('/api/auth/me')).status()).toBe(200)
  } finally {
    try {
      const restored = await secondDevice.request.put('/api/account/profile', {
        headers: await csrfHeaders(secondDevice.request),
        data: { name, phone: phone ?? '', email: email ?? '' },
      })
      expect(restored.status()).toBe(200)
    } finally {
      await secondDevice.close()
    }
  }
})

test('Given 两个浏览器已登录 When 原密码校验失败后修改密码成功 Then 两处会话失效且只能用新密码登录', async ({ page, browser, baseURL }) => {
  const secondDevice = await browser.newContext({ baseURL })
  const newPassword = ` ${randomBytes(6).toString('hex')} `
  let restore = async () => 204
  try {
    await signInForWorkspace(page.request)
    expect((await signInWithApi(secondDevice.request)).status()).toBe(200)
    const secondPage = await secondDevice.newPage()
    await secondPage.goto('/users')
    await expect(secondPage.locator('.account-button')).toBeVisible()

    await page.goto('/')
    await page.locator('.account-button').click()
    await page.getByRole('menuitem', { name: '修改密码' }).click()
    await page.getByLabel('原密码').fill('wrong-password')
    await page.locator('#newPassword').fill(newPassword)
    await page.getByLabel('确认新密码').fill(newPassword)
    await page.getByRole('button', { name: '确认修改' }).click()
    await expect(page.locator('#passwordLayer .account-error')).toBeVisible()

    await page.getByLabel('原密码').fill(adminPassword)
    restore = () => restorePassword(page, newPassword)
    await page.getByRole('button', { name: '确认修改' }).click()
    await expect(page.getByRole('heading', { name: '登录 OpenERP' })).toBeVisible()
    expect((await page.request.get('/api/auth/me')).status()).toBe(401)
    await secondPage.evaluate(() => window.dispatchEvent(new Event('focus')))
    await expect(secondPage.getByRole('heading', { name: '登录 OpenERP' })).toBeVisible()
    expect((await signInWithApi(page.request)).status()).toBe(401)
    expect((await signInWithApi(page.request, newPassword)).status()).toBe(200)
  } finally {
    try {
      expect(await restore()).toBe(204)
    } finally {
      await secondDevice.close()
    }
  }
})

test('Given 隔离宿主中的管理员已登录 When 保存关闭状态的定时同步设置 Then 通过防伪校验并保存店铺选择', async ({ page, request }) => {
  // eslint-disable-next-line playwright/no-skipped-test -- 此用例只写入浏览器验收宿主的隔离数据库。
  test.skip(process.env.OPENERP_RUN_BROWSER_TESTS !== '1', '通过隔离宿主运行定时同步保存验收。')
  await signInForWorkspace(request)

  // 只模拟 ERP 的读取结果，登录、防伪校验及设置读写均走真实服务。
  await page.route('**/api/erp/config', (route) => route.fulfill({
    json: { configured: true, baseUrl: 'https://erp.test', orderSyncEnabled: true },
  }))
  await page.route('**/api/erp/shops?**', (route) => route.fulfill({
    json: { items: [], page: 1, pageSize: 20, totalCount: 0, pageCount: 0 },
  }))
  await page.route('**/api/erp/order-sync-settings/shops', (route) => route.fulfill({
    json: { items: [{ sid: '42', name: '定时同步验收店铺' }] },
  }))

  await page.goto('/shops')
  await submitLogin(page, adminPassword)
  const settings = page.locator('.sync-settings-card')
  await expect(settings.getByText('未启用')).toBeVisible()

  const settingsUrl = '/api/erp/order-sync-settings'
  const initial = await page.request.get(settingsUrl)
  await expect(initial).toBeOK()
  const { enabled, shopSids } = await initial.json()
  expect(enabled).toBe(false)
  await settings.getByRole('button', { name: '全选可见店铺' }).click()

  try {
    await settings.getByRole('button', { name: '保存', exact: true }).click()
    await expect(settings.getByRole('status')).toHaveText('定时同步已关闭。')
    const saved = await page.request.get(settingsUrl)
    await expect(saved).toBeOK()
    expect(await saved.json()).toMatchObject({ enabled: false, shopSids: [42] })
  } finally {
    const restored = await page.request.put(settingsUrl, {
      headers: await csrfHeaders(page.request),
      data: { enabled, shopSids },
    })
    await expect(restored).toBeOK()
  }
})
