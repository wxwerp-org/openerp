import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import UsersView from '../../src/views/UsersView.vue'
import { adminAccount } from './adminAccount'
import { installNativeDialogShim } from './installNativeDialogShim'

const administrator = {
  id: adminAccount.id,
  username: adminAccount.username,
  name: adminAccount.name,
  phone: adminAccount.phone,
  email: adminAccount.email,
  lastLoginAt: adminAccount.lastLoginAt,
  role: adminAccount.role,
  createdAt: null,
}

const employee = {
  id: '019b2b91-0f43-7891-b8a7-87a15139c722',
  username: 'alice',
  name: '员工账号',
  phone: null,
  email: 'alice@example.com',
  lastLoginAt: null,
  role: 'User',
  createdAt: '2026-11-18T04:00:00Z',
}

describe('真实用户管理', () => {
  let wrapper: VueWrapper | undefined

  beforeAll(installNativeDialogShim)

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    vi.unstubAllGlobals()
  })

  it('Given 管理员打开添加用户 When 填写并保存 Then 列表显示新账号且不显示密码', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (_url: string, init?: RequestInit) => {
        if (init?.method !== 'POST') return Response.json([administrator])
        const body = JSON.parse(String(init.body))
        if (body.password !== 'a-secret' || body.username !== 'alice' || body.name !== '员工账号')
          return Response.json({ detail: '资料无效。' }, { status: 400 })
        return Response.json(employee, { status: 201 })
      }),
    )
    wrapper = mount(UsersView)
    await flushPromises()
    await wrapper.get('[data-testid="add-managed-user"]').trigger('click')
    const dialog = wrapper.get('dialog[open]')
    await dialog.get('[name="username"]').setValue(employee.username)
    await dialog.get('[name="name"]').setValue(employee.name)
    await dialog.get('[name="password"]').setValue('a-secret')
    await dialog.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('dialog[open]').exists()).toBe(false)
    expect(wrapper.findAll('tbody tr')).toHaveLength(2)
    expect(wrapper.findAll('tbody tr')[1].text()).toContain(employee.username)
    expect(wrapper.text()).not.toContain('a-secret')
  })

  it('Given 用户列表已有账号 When 查看管理员 Then 展示该账号的最新详情', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        if (url === `/api/users/${administrator.id}`) {
          return Response.json({ ...administrator, email: 'admin@example.com' })
        }
        return Response.json([administrator, employee])
      }),
    )
    wrapper = mount(UsersView)
    await flushPromises()
    await wrapper.get('tbody tr button').trigger('click')
    await flushPromises()

    const dialog = wrapper.get('dialog[open]')
    expect(dialog.text()).toContain('查看用户')
    expect(dialog.text()).toContain('admin@example.com')
    expect(dialog.find('button[type="submit"]').exists()).toBe(false)
  })

  it('Given 普通用户已存在 When 管理员编辑用户名 Then 通过原账号 UUID 保存且只更新原有行', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init?: RequestInit) => {
        if (url === '/api/users') return Response.json([administrator, employee])
        if (url === `/api/users/${employee.id}` && init?.method === 'PUT') {
          const fields = JSON.parse(String(init.body))
          if ('role' in fields || 'password' in fields)
            return Response.json({ detail: '不可修改角色或密码。' }, { status: 400 })
          return Response.json({ ...employee, ...fields })
        }
        return new Response(null, { status: 404 })
      }),
    )
    wrapper = mount(UsersView)
    await flushPromises()
    await wrapper.findAll('tbody tr')[1].findAll('button')[1].trigger('click')
    await flushPromises()
    const dialog = wrapper.get('dialog[open]')
    expect(dialog.text()).toContain('编辑用户')
    expect(dialog.find('[name="password"]').exists()).toBe(false)
    await dialog.get('[name="username"]').setValue('bob')
    await dialog.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('dialog[open]').exists()).toBe(false)
    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(2)
    expect(rows[1].get('[scope="row"]').text()).toBe('bob')
  })

  it('Given 普通用户仍在使用 When 先取消再确认删除 Then 取消保留用户且确认后移除原账号行', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init?: RequestInit) => {
        if (url === '/api/users') return Response.json([administrator, employee])
        if (url === `/api/users/${employee.id}` && init?.method === 'DELETE')
          return new Response(null, { status: 204 })
        return new Response(null, { status: 404 })
      }),
    )
    wrapper = mount(UsersView)
    await flushPromises()
    const deleteUser = wrapper.findAll('tbody tr')[1].findAll('button')[2]
    await deleteUser.trigger('click')
    await flushPromises()
    const confirmation = wrapper.get('dialog[open]')
    expect(confirmation.text()).toContain('删除用户')
    expect(confirmation.text()).toContain('alice')
    await confirmation.get('button[type="button"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('dialog[open]').exists()).toBe(false)
    expect(wrapper.findAll('tbody tr')).toHaveLength(2)

    await deleteUser.trigger('click')
    await flushPromises()
    await wrapper.get('dialog[open] form').trigger('submit')
    await flushPromises()
    expect(wrapper.find('dialog[open]').exists()).toBe(false)
    expect(wrapper.findAll('tbody tr')).toHaveLength(1)
    expect(wrapper.get('tbody tr').text()).toContain('admin')
  })

  it.each(['添加', '编辑'])(
    'Given 用户名仍在删除冷却期 When %s账号保存 Then 显示服务器原因并保留表单以便重试',
    async (action) => {
      vi.stubGlobal(
        'fetch',
        vi.fn(async (_url: string, init?: RequestInit) =>
          init?.method
            ? Response.json({ detail: '该用户名正在使用或处于删除保留期。' }, { status: 409 })
            : Response.json([administrator, employee]),
        ),
      )
      wrapper = mount(UsersView)
      await flushPromises()
      const button =
        action === '添加'
          ? wrapper.get('[data-testid="add-managed-user"]')
          : wrapper.get('button[aria-label="编辑：alice"]')
      await button.trigger('click')
      await flushPromises()
      const dialog = wrapper.get('dialog[open]')
      const username = dialog.get<HTMLInputElement>('[name="username"]')
      await username.setValue('bob')
      await dialog.get('[name="name"]').setValue('员工账号')
      if (action === '添加') await dialog.get('[name="password"]').setValue('a-secret')
      await dialog.get('form').trigger('submit')
      await flushPromises()

      expect(dialog.get('[role="alert"]').text()).toBe('该用户名正在使用或处于删除保留期。')
      expect(username.element.value).toBe('bob')
      expect(wrapper.findAll('tbody tr')).toHaveLength(2)
    },
  )

  it('Given 员工有一条目录缺失的分配 When 分页并按平台与关键字选店 Then 保存多个店铺且可撤销全部分配', async () => {
    let assignments = [{ platform: 'TemuPop', sid: 99 }]
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init?: RequestInit) => {
        if (url === '/api/users') return Response.json([administrator, employee])
        if (url === '/api/erp/platforms') {
          return Response.json({ items: [{ name: 'TemuPop', displayName: 'Temu' }] })
        }
        if (url === `/api/users/${employee.id}/shops`) {
          if (init?.method !== 'PUT') return Response.json(assignments)
          assignments = JSON.parse(String(init.body))
          return new Response(null, { status: 204 })
        }
        const params = new URL(url, 'https://example.test').searchParams
        if (url.startsWith('/api/erp/shops?')) {
          if (params.get('platform') === 'TemuPop' && params.get('keyword') === '华南') {
            return Response.json({
              items: [{ sid: '12', platform: 'TemuPop', name: '华南店' }],
              page: 1,
              pageCount: 1,
            })
          }
          const page = Number(params.get('page')) || 1
          return Response.json({
            items: [{ sid: String(page), platform: 'TemuPop', name: `门店 ${page}` }],
            page,
            pageCount: 2,
          })
        }
        return new Response(null, { status: 404 })
      }),
    )

    wrapper = mount(UsersView)
    await flushPromises()
    const open = wrapper.get('button[aria-label="分配店铺：alice"]')
    await open.trigger('click')
    await flushPromises()
    let dialog = wrapper.get('dialog[open]')
    expect(dialog.get('fieldset').text()).toContain('99')
    await dialog.get('button[aria-label="下一页"]').trigger('click')
    await flushPromises()
    await dialog.get('input[type="checkbox"][value="TemuPop/2"]').setValue(true)
    await dialog.get('select').setValue('TemuPop')
    await dialog.get('input[type="search"]').setValue('华南')
    await dialog.get('form[role="search"]').trigger('submit')
    await flushPromises()
    expect(dialog.text()).toContain('华南店')
    await dialog.get('input[type="checkbox"][value="TemuPop/12"]').setValue(true)
    await dialog.get('form[data-testid="save-shop-assignments"]').trigger('submit')
    await flushPromises()
    expect(wrapper.find('dialog[open]').exists()).toBe(false)

    await open.trigger('click')
    await flushPromises()
    dialog = wrapper.get('dialog[open]')
    const selected = dialog.get('fieldset')
    expect(selected.text()).toContain('TemuPop / 99')
    expect(selected.text()).toContain('TemuPop / 2')
    expect(selected.text()).toContain('TemuPop / 12')
    expect(assignments).toEqual([
      { platform: 'TemuPop', sid: 99 },
      { platform: 'TemuPop', sid: 2 },
      { platform: 'TemuPop', sid: 12 },
    ])
    for (const button of selected.findAll('button')) await button.trigger('click')
    await dialog.get('form[data-testid="save-shop-assignments"]').trigger('submit')
    await flushPromises()
    await open.trigger('click')
    await flushPromises()
    expect(wrapper.get('dialog[open] fieldset').text()).toContain('尚未选择店铺')
  })

  it('Given 系统中的管理员和普通用户 When 打开用户管理 Then 展示真实账号且管理员仅可查看', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Response.json([administrator, employee])),
    )
    wrapper = mount(UsersView)
    await flushPromises()

    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(2)
    expect(rows[0].text()).toContain('admin')
    expect(rows[0].text()).toContain('管理员')
    expect(rows[0].findAll('button').map((button) => button.text())).toEqual(['查看'])
    expect(rows[1].text()).toContain('alice')
    expect(rows[1].text()).toContain('alice@example.com')
    expect(rows[1].findAll('button').map((button) => button.text())).toEqual([
      '查看',
      '编辑',
      '删除',
      '分配店铺',
    ])
  })
})
