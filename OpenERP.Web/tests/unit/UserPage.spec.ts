import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'

// 用普通函数而不是 vi.fn：vi.fn 会给返回的 Promise 挂记录回调，被拒绝的 Promise 会因此变成未处理的拒绝
const api = vi.hoisted(() => ({ users: (): Promise<unknown> => Promise.resolve([]) }))
const fetchUsers = {
  resolves: (value: unknown) => (api.users = () => Promise.resolve(value)),
  rejects: (error: Error) => (api.users = () => Promise.reject(error))
}
vi.mock('@/api/user', () => ({
  fetchUsers: () => api.users(),
  fetchUser: vi.fn(),
  createUser: vi.fn(),
  updateUser: vi.fn(),
  deleteUser: vi.fn()
}))

const user = (
  id: string,
  username: string,
  name: string,
  role: 'Administrator' | 'User',
  phone: string | null
) => ({
  id,
  role,
  username,
  name,
  phone,
  email: `${username}@example.com`,
  lastLoginAt: null,
  createdAt: '2026-01-01T00:00:00Z'
})

const users = [
  user('1', 'lucy.lin', '林沁', 'Administrator', '13900001024'),
  user('2', 'alex.chen', '陈予安', 'User', '13800002048'),
  user('3', 'mia.zhou', '周觅', 'User', null),
  user('4', 'leo.wang', '王乐', 'User', '15900003096')
]

// ArtTable 依赖全局 store 与国际化；单测只关心它收到的行数据与分页
const ArtTableStub = defineComponent({
  name: 'ArtTable',
  props: {
    data: { type: Array, default: () => [] },
    pagination: { type: Object, default: () => ({}) }
  },
  setup: (props) => () =>
    h('div', [
      h('span', { class: 'total' }, String((props.pagination as { total: number }).total)),
      ...(props.data as { username: string }[]).map((row) =>
        h('div', { class: 'row' }, row.username)
      )
    ])
})
const ArtTableHeaderStub = defineComponent({
  name: 'ArtTableHeader',
  setup:
    (_, { slots }) =>
    () =>
      h('div', slots.left?.())
})

async function mountPage() {
  const { default: UserPage } = await import('@/views/system/user/index.vue')
  const wrapper = mount(UserPage, {
    global: { stubs: { ArtTable: ArtTableStub, ArtTableHeader: ArtTableHeaderStub } }
  })
  await flushPromises()
  return wrapper
}

const rows = (wrapper: Awaited<ReturnType<typeof mountPage>>) =>
  wrapper.findAll('.row').map((row) => row.text())

describe('用户管理页', () => {
  beforeEach(() => fetchUsers.resolves([]))

  it('Given 服务端返回 4 位用户 When 打开页面 Then 列表展示全部用户且总数为 4', async () => {
    fetchUsers.resolves(users)

    const wrapper = await mountPage()

    expect(rows(wrapper)).toEqual(['lucy.lin', 'alex.chen', 'mia.zhou', 'leo.wang'])
    expect(wrapper.get('.total').text()).toBe('4')
  })

  it('Given 已加载用户 When 输入关键字 alex Then 仅保留用户名匹配的用户', async () => {
    fetchUsers.resolves(users)
    const wrapper = await mountPage()

    await wrapper.get('input[placeholder="输入用户名、手机号搜索"]').setValue('alex')

    expect(rows(wrapper)).toEqual(['alex.chen'])
    expect(wrapper.get('.total').text()).toBe('1')
  })

  it('Given 已加载用户 When 按手机号片段搜索 Then 命中手机号', async () => {
    fetchUsers.resolves(users)
    const wrapper = await mountPage()

    await wrapper.get('input[placeholder="输入用户名、手机号搜索"]').setValue('1590000')

    expect(rows(wrapper)).toEqual(['leo.wang'])
  })

  it('Given 已筛选出 0 条 When 点击重置 Then 恢复全部用户', async () => {
    fetchUsers.resolves(users)
    const wrapper = await mountPage()
    await wrapper.get('input[placeholder="输入用户名、手机号搜索"]').setValue('不存在的用户')
    expect(rows(wrapper)).toEqual([])

    const reset = wrapper.findAll('button').find((button) => button.text() === '重置')!
    await reset.trigger('click')

    expect(rows(wrapper)).toHaveLength(4)
  })

  it('Given 默认模糊查询 When 输入真实姓名的一部分 Then 命中包含该片段的用户', async () => {
    fetchUsers.resolves(users)
    const wrapper = await mountPage()
    const name = wrapper.get('input[placeholder="真实姓名"]')

    await name.setValue('予')
    expect(rows(wrapper)).toEqual(['alex.chen'])
  })

  it('Given 读取用户失败 When 打开页面 Then 显示错误提示且列表为空', async () => {
    fetchUsers.rejects(new Error('无法读取用户，请重试。'))

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('无法读取用户，请重试。')
    expect(rows(wrapper)).toEqual([])
  })
})
