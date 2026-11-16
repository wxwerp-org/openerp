import { expect, test } from '@playwright/test'

test('Given 原型工作台 When 打开首页并进入用户管理 Then 全部卡片和四条用户数据可见', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.goto('/')

  await expect(page.locator('.page-dashboard')).toBeVisible()
  await expect(page.locator('.rank-table tbody tr')).toHaveCount(5)
  await expect(page.locator('.trend-chart')).toBeVisible()
  await expect(page.locator('.donut-chart')).toBeVisible()

  await page.locator('[data-group="settings"] .nav-group-head').click()
  await page.locator('[data-nav-page="users"]').click()
  await expect(page).toHaveURL(/\/users$/)
  await expect(page.locator('#userTableBody tr')).toHaveCount(4)
  await expect(page.locator('.page-tab.active .tab-label')).toHaveText('用户管理')
})

test('Given 多平台配置 When 选择店铺设置并打开账户弹窗 Then 配置内容和焦点行为与原型一致', async ({ page }) => {
  await page.goto('/settings')
  await page.locator('[data-settings-group="multiplatform"]').click()
  await page.locator('[data-settings-item="店铺设置"]').click()

  await expect(page.locator('#settingsSecondTitle')).toHaveText('多平台')
  await expect(page.locator('#settingsContent')).toContainText('配置多平台模块下的店铺设置能力')

  await page.locator('.account-button').click()
  await page.locator('[data-account-action="profile"]').click()
  await expect(page.locator('#profileLayer')).toBeVisible()
  await expect(page.locator('#profileName')).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(page.locator('#profileLayer')).not.toBeVisible()
  await expect(page.locator('.account-button')).toBeFocused()

  await page.locator('.account-button').click()
  await page.locator('[data-account-action="password"]').click()
  await expect(page.locator('#oldPassword')).toBeFocused()
  await page.locator('#oldPassword').fill('old-password')
  await page.locator('#newPassword').fill('new-password')
  await page.locator('#confirmPassword').fill('different')
  await page.locator('#savePassword').click()
  await expect(page.getByText('两次输入的密码不一致')).toBeVisible()
})

test('Given 产品和订单原型 When 打开表单与公告 Then 弹窗可见且桌面页面无横向溢出', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.goto('/products')
  await page.locator('#openProductForm').click()
  await expect(page.locator('#productFormLayer')).toBeVisible()
  await expect(page.locator('.form-nav-item')).toHaveCount(7)
  await page.locator('#saveProductForm').click()
  await expect(page.getByText('请输入产品名称')).toBeVisible()
  await page.locator('#closeProductForm').click()

  await page.goto('/orders')
  await page.locator('#openNotice').click()
  await expect(page.locator('#noticeLayer')).toBeVisible()
  await expect(page.locator('#noticeTitle')).toHaveText('【订单列表】优化公告')
  await page.locator('#closeNotice').click()

  await page.setViewportSize({ width: 1440, height: 900 })
  const hasPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)
  expect(hasPageOverflow).toBe(false)
})
