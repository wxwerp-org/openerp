<script setup lang="ts">
import { computed, ref } from 'vue'
import AppButton from '../ui/AppButton.vue'
import AppCheckbox from '../ui/AppCheckbox.vue'
import AppIcon from '../ui/AppIcon.vue'
import AppIconButton from '../ui/AppIconButton.vue'
import AppListSearchbar from '../ui/AppListSearchbar.vue'
import AppListToolbar from '../ui/AppListToolbar.vue'
import AppPager from '../ui/AppPager.vue'
import AppTable from '../ui/AppTable.vue'
import AppTd from '../ui/AppTd.vue'
import AppTh from '../ui/AppTh.vue'

const users = [
  {
    username: 'lucy.lin',
    realname: '林沁',
    phone: '139****1024',
    email: 'lucy.lin@auroralabs.cn',
    status: '开启',
    department: '管理中心',
    created: '2026-02-12 09:18:21',
    role: '超级管理员',
    perms: '全部',
    logins: '268',
    lastLogin: '2026-11-12 15:14:11',
    lastIp: '10.24.16.38',
    thirdParty: '钉钉已授权',
    security: 'MFA 已绑定',
  },
  {
    username: 'alex.chen',
    realname: '陈予安',
    phone: '138****2048',
    email: 'alex.chen@auroralabs.cn',
    status: '开启',
    department: '运营中心',
    created: '2025-03-18 10:22:41',
    role: '运营主管',
    perms: '12 项',
    logins: '128',
    lastLogin: '2026-11-12 09:44:18',
    lastIp: '113.88.24.61',
    thirdParty: '钉钉已授权',
    security: 'MFA 已绑定',
  },
  {
    username: 'mia.zhou',
    realname: '周觅',
    phone: '186****7712',
    email: 'mia.zhou@auroralabs.cn',
    status: '开启',
    department: '财务中心',
    created: '2025-06-02 14:08:12',
    role: '财务管理员',
    perms: '9 项',
    logins: '64',
    lastLogin: '2026-11-11 18:20:09',
    lastIp: '58.246.17.203',
    thirdParty: '-',
    security: 'MFA 已绑定',
  },
  {
    username: 'leo.wang',
    realname: '王乐',
    phone: '159****3096',
    email: 'leo.wang@auroralabs.cn',
    status: '停用',
    department: '产品中心',
    created: '2026-01-15 09:30:26',
    role: '产品运营',
    perms: '6 项',
    logins: '12',
    lastLogin: '2026-09-28 11:05:42',
    lastIp: '101.80.112.39',
    thirdParty: '飞书已授权',
    security: '未绑定',
  },
]

const sortDirection = ref<'ascending' | 'descending' | null>(null)
const sortedUsers = computed(() => {
  if (!sortDirection.value) return users
  const factor = sortDirection.value === 'ascending' ? 1 : -1
  return [...users].sort((left, right) => left.created.localeCompare(right.created) * factor)
})

function toggleCreatedSort(): void {
  sortDirection.value = sortDirection.value === 'ascending' ? 'descending' : 'ascending'
}

/** 旧筛选条的拼接控件与上下文覆盖较多，按实测几何显式书写，避免组合类互相覆盖。 */
const joinedControlStart =
  'inline-flex h-[var(--control-h)] shrink-0 items-center gap-6 rounded-l-4 border-y border-l border-input bg-white px-10 text-12/28 whitespace-nowrap text-placeholder hover:border-input-hover'
const fieldBox =
  'inline-flex h-[var(--control-h)] shrink-0 items-center border border-input bg-white focus-within:border-primary'
const fieldBoxJoinedEnd =
  'inline-flex h-[var(--control-h)] shrink-0 items-center border-y border-r border-input bg-white focus-within:border-primary'
const fieldBoxInput =
  'h-28 min-w-0 border-0 border-current bg-transparent px-10 py-0 text-12/18 text-ink outline-none placeholder:text-placeholder'
const searchButton =
  'flex h-28 w-30 shrink-0 items-center justify-center text-search-icon hover:bg-search-hover-bg hover:text-primary'
const splitButtonStart =
  'inline-flex h-[var(--control-h)] shrink-0 items-center justify-center gap-5 rounded-l-4 border-y border-l border-input bg-white px-10 text-12/28 whitespace-nowrap text-ink hover:border-input-hover hover:bg-btn-hover-bg'
const splitButtonEnd =
  'inline-flex h-[var(--control-h)] w-30 shrink-0 items-center justify-center rounded-r-4 border border-input bg-white px-0 text-12/28 text-ink hover:border-input-hover hover:bg-btn-hover-bg'
const caretClass = 'opacity-[0.65]'
const pageSizeClass =
  'inline-flex h-28 min-w-100 shrink-0 items-center justify-between gap-10 rounded-4 border border-input bg-white px-9 text-ink'
const statusPill =
  'inline-flex h-18 items-center justify-center rounded-2 border px-5 text-11/16 whitespace-nowrap'
const statusPillOn = `${statusPill} border-tag-green-border bg-tag-green-bg text-success`
const statusPillOff = `${statusPill} border-status-off-border bg-tag-gray-bg text-muted`
const pageJumpInput =
  'h-28 w-46 rounded-4 border border-input bg-white px-4 py-0 text-center text-12/18 text-ink outline-none focus:border-primary'
</script>

<template>
  <section id="page-users" class="flex h-full w-full min-h-0 flex-col overflow-hidden bg-white">
    <h1 class="sr-only">用户管理</h1>
    <AppListSearchbar label="用户筛选">
      <div class="flex items-stretch">
        <button :class="joinedControlStart" type="button">
          登录时间<AppIcon name="i-chev-down" :size="10" class="text-caret" />
        </button>
        <div :class="fieldBoxJoinedEnd" class="w-[155px] rounded-r-4">
          <input
            :class="fieldBoxInput"
            aria-label="登录时间范围"
            placeholder="开始日期 - 结束日期"
            readonly
          />
          <AppIcon name="i-calendar" :size="14" class="mr-8 text-placeholder" />
        </div>
      </div>
      <button
        class="inline-flex h-[var(--control-h)] shrink-0 items-center gap-6 rounded-4 border border-input bg-white px-10 text-12/28 whitespace-nowrap text-placeholder hover:border-input-hover"
        type="button"
      >
        用户状态<AppIcon name="i-chev-down" :size="10" class="text-caret" />
      </button>
      <button
        class="inline-flex h-[var(--control-h)] shrink-0 items-center gap-6 rounded-4 border border-input bg-white px-10 text-12/28 whitespace-nowrap text-placeholder hover:border-input-hover"
        type="button"
      >
        用户角色<AppIcon name="i-chev-down" :size="10" class="text-caret" />
      </button>
      <div :class="fieldBox" class="w-[83px] rounded-4">
        <input :class="fieldBoxInput" aria-label="真实姓名" placeholder="真实姓名" />
      </div>
      <div :class="fieldBox" class="-ml-1 w-[59px] rounded-4">
        <input :class="fieldBoxInput" aria-label="查询方式" value="模糊" readonly />
        <AppIcon name="i-chev-down" :size="10" class="mr-9 text-caret" />
      </div>
      <div
        class="flex h-[var(--control-h)] min-w-40 max-w-237 flex-1 items-center overflow-hidden rounded-4 border border-input bg-white"
      >
        <input
          :class="fieldBoxInput"
          class="flex-1"
          aria-label="搜索用户"
          placeholder="输入用户名、手机号搜索"
        />
        <button :class="searchButton" type="button" title="搜索" aria-label="搜索">
          <AppIcon name="i-search" :size="16" />
        </button>
        <span class="h-16 w-px shrink-0 bg-line-soft"></span>
        <button :class="searchButton" type="button" title="高级搜索" aria-label="高级搜索">
          <AppIcon name="i-filter" :size="16" />
        </button>
      </div>
      <AppButton>更多筛选</AppButton>
      <AppButton variant="soft">重置</AppButton>
    </AppListSearchbar>
    <AppListToolbar>
      <div class="inline-flex shrink-0 items-stretch">
        <button :class="splitButtonStart" type="button">添加用户</button>
        <button :class="splitButtonEnd" type="button" aria-label="添加用户选项">
          <AppIcon name="i-chev-down" :size="10" :class="caretClass" />
        </button>
      </div>
      <AppButton>批量<AppIcon name="i-chev-down" :size="10" :class="caretClass" /></AppButton>
      <AppButton>负责人交接<AppIcon name="i-chev-down" :size="10" :class="caretClass" /></AppButton>
      <AppButton>批量解绑</AppButton>
      <template #right>
        <AppIconButton label="刷新" tone="toolbar"><AppIcon name="i-refresh" /></AppIconButton>
        <AppIconButton label="帮助" tone="toolbar"><AppIcon name="i-help" /></AppIconButton>
      </template>
    </AppListToolbar>
    <AppTable caption="用户管理列表" label="用户管理表格" :width="2152">
      <colgroup>
        <col style="width: 38px" />
        <col style="width: 120px" />
        <col style="width: 120px" />
        <col style="width: 120px" />
        <col style="width: 140px" />
        <col style="width: 80px" />
        <col style="width: 140px" />
        <col style="width: 160px" />
        <col style="width: 160px" />
        <col style="width: 120px" />
        <col style="width: 80px" />
        <col style="width: 160px" />
        <col style="width: 120px" />
        <col style="width: 280px" />
        <col style="width: 260px" />
        <col style="width: 54px" />
      </colgroup>
      <thead>
        <tr>
          <AppTh sticky="left" class="pl-8">
            <AppCheckbox aria-label="全选" />
          </AppTh>
          <AppTh>用户名</AppTh>
          <AppTh>真实姓名</AppTh>
          <AppTh>手机号</AppTh>
          <AppTh>邮箱</AppTh>
          <AppTh>状态</AppTh>
          <AppTh>部门</AppTh>
          <AppTh data-sort="created" :aria-sort="sortDirection ?? undefined">
            <button
              class="inline-flex min-h-24 max-w-full items-center gap-3 text-left disabled:cursor-default"
              type="button"
              aria-label="按创建时间排序"
              @click="toggleCreatedSort"
            >
              创建时间<AppIcon name="i-sort" icon-class="h-12 w-8 text-primary" />
            </button>
          </AppTh>
          <AppTh>角色</AppTh>
          <AppTh>关联权限</AppTh>
          <AppTh>登录次数</AppTh>
          <AppTh>最近登录时间</AppTh>
          <AppTh>最近登录IP</AppTh>
          <AppTh>第三方授权</AppTh>
          <AppTh>账号安全</AppTh>
          <AppTh sticky="right">操作</AppTh>
        </tr>
      </thead>
      <tbody id="userTableBody">
        <tr v-for="user in sortedUsers" :key="user.username">
          <AppTd sticky="left" class="pl-10">
            <AppCheckbox :aria-label="`选择${user.realname}`" />
          </AppTd>
          <AppTd tone="ink" data-testid="user-name" :title="user.username">{{
            user.username
          }}</AppTd>
          <AppTd>{{ user.realname }}</AppTd>
          <AppTd>{{ user.phone }}</AppTd>
          <AppTd :title="user.email">{{ user.email }}</AppTd>
          <AppTd>
            <span :class="user.status === '停用' ? statusPillOff : statusPillOn">
              {{ user.status }}
            </span>
          </AppTd>
          <AppTd>{{ user.department }}</AppTd>
          <AppTd>{{ user.created }}</AppTd>
          <AppTd>{{ user.role }}</AppTd>
          <AppTd>{{ user.perms }}</AppTd>
          <AppTd>{{ user.logins }}</AppTd>
          <AppTd>{{ user.lastLogin }}</AppTd>
          <AppTd>{{ user.lastIp }}</AppTd>
          <AppTd>{{ user.thirdParty }}</AppTd>
          <AppTd>
            <span class="inline-flex items-center gap-5 text-body">
              <AppIcon
                v-if="user.security !== '未绑定'"
                name="i-shield-check"
                :size="12"
                class="text-menu-icon"
              />
              {{ user.security }}
            </span>
          </AppTd>
          <AppTd sticky="right">
            <div class="flex items-center gap-12">
              <button
                class="text-12/18 whitespace-nowrap text-primary hover:underline"
                type="button"
              >
                编辑
              </button>
            </div>
          </AppTd>
        </tr>
      </tbody>
      <template #footer>
        <AppPager :page="1" :page-count="1" label="用户分页">
          <template #prefix><span>共4条</span></template>
          <template #suffix>
            <button :class="pageSizeClass" type="button">
              20条/页<AppIcon name="i-chev-down" :size="10" class="text-placeholder" />
            </button>
            <span class="inline-flex items-center gap-7 pl-3 text-body">
              前往<input :class="pageJumpInput" value="1" aria-label="前往页码" />页
            </span>
          </template>
        </AppPager>
      </template>
    </AppTable>
  </section>
</template>
