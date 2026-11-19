<script setup lang="ts">
import { computed, ref } from 'vue'
import AppIcon from '../ui/AppIcon.vue'

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
</script>

<template>
  <section id="page-users" class="page page-users active">
    <h1 class="visually-hidden">用户管理</h1>
    <div class="list-searchbar user-filter">
      <div class="date-group">
        <button class="control placeholder" type="button">
          登录时间<AppIcon name="i-chev-down" icon-class="caret" />
        </button>
        <div class="field-box" style="width: 155px">
          <input aria-label="登录时间范围" placeholder="开始日期 - 结束日期" readonly /><AppIcon
            name="i-calendar"
          />
        </div>
      </div>
      <button class="control placeholder" type="button">
        用户状态<AppIcon name="i-chev-down" icon-class="caret" />
      </button>
      <button class="control placeholder" type="button">
        用户角色<AppIcon name="i-chev-down" icon-class="caret" />
      </button>
      <div class="field-box realname-field" style="width: 83px">
        <input aria-label="真实姓名" placeholder="真实姓名" />
      </div>
      <div class="field-box mode-field" style="width: 59px; margin-left: -1px">
        <input aria-label="查询方式" value="模糊" readonly /><AppIcon
          name="i-chev-down"
          icon-class="caret"
        />
      </div>
      <div class="search-box">
        <input aria-label="搜索用户" placeholder="输入用户名、手机号搜索" />
        <button class="search-btn" type="button" title="搜索" aria-label="搜索">
          <AppIcon name="i-search" icon-class="ic-16" />
        </button>
        <span class="split-line"></span>
        <button class="search-btn" type="button" title="高级搜索" aria-label="高级搜索">
          <AppIcon name="i-filter" icon-class="ic-16" />
        </button>
      </div>
      <button class="btn" type="button">更多筛选</button>
      <button class="btn soft" type="button">重置</button>
    </div>
    <div class="list-toolbar">
      <div class="toolbar-left">
        <div class="split-select">
          <button class="btn" type="button" style="border-radius: 4px 0 0 4px; border-right: 0">
            添加用户
          </button>
          <button
            class="btn"
            type="button"
            style="width: 30px; padding: 0; border-radius: 0 4px 4px 0"
            aria-label="添加用户选项"
          >
            <AppIcon name="i-chev-down" icon-class="caret" />
          </button>
        </div>
        <button class="btn" type="button">
          批量<AppIcon name="i-chev-down" icon-class="caret" />
        </button>
        <button class="btn" type="button">
          负责人交接<AppIcon name="i-chev-down" icon-class="caret" />
        </button>
        <button class="btn" type="button">批量解绑</button>
      </div>
      <div class="toolbar-right">
        <button class="btn-icon" type="button" title="刷新" aria-label="刷新">
          <AppIcon name="i-refresh" />
        </button>
        <button class="btn-icon" type="button" title="帮助" aria-label="帮助">
          <AppIcon name="i-help" />
        </button>
      </div>
    </div>
    <div class="table-region">
      <div class="table-box">
        <div class="table-area">
          <div class="table-scroll" tabindex="0" role="region" aria-label="用户管理表格">
            <table class="grid-table user-table" style="width: 2152px">
              <caption class="visually-hidden">
                用户管理列表
              </caption>
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
                  <th scope="col" class="col-check">
                    <input class="checkbox" type="checkbox" aria-label="全选" />
                  </th>
                  <th scope="col">用户名</th>
                  <th scope="col">真实姓名</th>
                  <th scope="col">手机号</th>
                  <th scope="col">邮箱</th>
                  <th scope="col">状态</th>
                  <th scope="col">部门</th>
                  <th scope="col" data-sort="created" :aria-sort="sortDirection ?? undefined">
                    <button
                      class="sort-head"
                      type="button"
                      aria-label="按创建时间排序"
                      @click="toggleCreatedSort"
                    >
                      创建时间<AppIcon
                        name="i-sort"
                        icon-class="sort-icon"
                        style="color: #005bf5"
                      />
                    </button>
                  </th>
                  <th scope="col">角色</th>
                  <th scope="col">关联权限</th>
                  <th scope="col">登录次数</th>
                  <th scope="col">最近登录时间</th>
                  <th scope="col">最近登录IP</th>
                  <th scope="col">第三方授权</th>
                  <th scope="col">账号安全</th>
                  <th scope="col" class="col-action">操作</th>
                </tr>
              </thead>
              <tbody id="userTableBody">
                <tr v-for="user in sortedUsers" :key="user.username">
                  <td class="col-check">
                    <input class="checkbox" type="checkbox" :aria-label="`选择${user.realname}`" />
                  </td>
                  <td class="user-name" :title="user.username">{{ user.username }}</td>
                  <td>{{ user.realname }}</td>
                  <td>{{ user.phone }}</td>
                  <td :title="user.email">{{ user.email }}</td>
                  <td>
                    <span class="status-pill" :class="{ off: user.status === '停用' }">{{
                      user.status
                    }}</span>
                  </td>
                  <td>{{ user.department }}</td>
                  <td>{{ user.created }}</td>
                  <td>{{ user.role }}</td>
                  <td>{{ user.perms }}</td>
                  <td>{{ user.logins }}</td>
                  <td>{{ user.lastLogin }}</td>
                  <td>{{ user.lastIp }}</td>
                  <td>{{ user.thirdParty }}</td>
                  <td>
                    <span class="security-cell"
                      ><AppIcon
                        v-if="user.security !== '未绑定'"
                        name="i-shield-check"
                        icon-class="ic-12"
                      />{{ user.security }}</span
                    >
                  </td>
                  <td class="col-action">
                    <div class="oper-cell">
                      <button class="oper-link" type="button">编辑</button>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
        <div class="table-pagination">
          <span>共4条</span>
          <button class="page-btn" type="button" disabled aria-label="上一页">
            <AppIcon name="i-chev-left" icon-class="ic-12" />
          </button>
          <span class="page-num">1</span>
          <button class="page-btn" type="button" disabled aria-label="下一页">
            <AppIcon name="i-chev-right" icon-class="ic-12" />
          </button>
          <button class="page-size" type="button">20条/页<AppIcon name="i-chev-down" /></button>
          <span class="page-jump">前往<input value="1" aria-label="前往页码" />页</span>
        </div>
      </div>
    </div>
  </section>
</template>
