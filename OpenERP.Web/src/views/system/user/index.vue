<!-- 用户管理：筛选 + 列表 + 增删改查，对接 OpenERP.Api `/api/users`。 -->
<template>
  <div class="page-stack page-stack--fill">
    <ElCard class="search-card" shadow="never">
      <ElForm :model="filters" inline @submit.prevent>
        <ElFormItem label="用户角色">
          <ElSelect v-model="filters.role" clearable placeholder="全部" style="width: 130px">
            <ElOption label="管理员" value="Administrator" />
            <ElOption label="普通用户" value="User" />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="真实姓名">
          <ElInput v-model="filters.name" clearable placeholder="真实姓名" style="width: 150px" />
        </ElFormItem>
        <ElFormItem label="查询方式">
          <ElSelect v-model="filters.nameMatch" style="width: 90px">
            <ElOption label="模糊" value="fuzzy" />
            <ElOption label="精确" value="exact" />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="关键字">
          <ElInput
            v-model="filters.keyword"
            clearable
            placeholder="输入用户名、手机号搜索"
            style="width: 220px"
          />
        </ElFormItem>
        <ElFormItem>
          <ElButton @click="resetFilters">重置</ElButton>
        </ElFormItem>
      </ElForm>
    </ElCard>

    <AppNotice v-if="listError" tone="error">
      {{ listError }}
      <template #action><ElButton link @click="loadUsers">重试</ElButton></template>
    </AppNotice>

    <ElCard class="art-table-card" shadow="never">
      <ArtTableHeader :loading="loading" @refresh="loadUsers">
        <template #left>
          <ElButton
            type="primary"
            :disabled="loading"
            data-testid="add-managed-user"
            @click="openEditor()"
          >
            添加用户
          </ElButton>
        </template>
      </ArtTableHeader>

      <ArtTable
        :loading="loading"
        :data="pageData"
        :columns="columns"
        :pagination="pagination"
        @sort-change="onSortChange"
        @pagination:size-change="onSizeChange"
        @pagination:current-change="onPageChange"
      />
    </ElCard>

    <!-- 添加 / 编辑 -->
    <ElDialog
      v-model="editorVisible"
      :title="editing ? '编辑用户' : '添加用户'"
      width="480px"
      :close-on-click-modal="false"
      :before-close="guardClose"
      append-to-body
    >
      <ElForm ref="formRef" :model="form" :rules="rules" label-width="80px" @submit.prevent="save">
        <ElFormItem label="用户名" prop="username">
          <ElInput
            v-model.trim="form.username"
            maxlength="20"
            autocomplete="off"
            :disabled="saving"
          />
        </ElFormItem>
        <ElFormItem label="姓名" prop="name">
          <ElInput v-model.trim="form.name" maxlength="20" autocomplete="off" :disabled="saving" />
        </ElFormItem>
        <ElFormItem v-if="!editing" label="密码" prop="password">
          <ElInput
            v-model="form.password"
            type="password"
            show-password
            maxlength="20"
            autocomplete="new-password"
            :disabled="saving"
          />
          <div class="form-hint">6–20 个字符</div>
        </ElFormItem>
        <ElFormItem label="手机号" prop="phone">
          <ElInput v-model.trim="form.phone" maxlength="20" autocomplete="off" :disabled="saving" />
        </ElFormItem>
        <ElFormItem label="邮箱" prop="email">
          <ElInput v-model.trim="form.email" autocomplete="off" :disabled="saving" />
        </ElFormItem>
        <AppNotice v-if="dialogError" tone="error">{{ dialogError }}</AppNotice>
      </ElForm>
      <template #footer>
        <ElButton :disabled="saving" @click="editorVisible = false">取消</ElButton>
        <ElButton type="primary" :loading="saving" @click="save">保存</ElButton>
      </template>
    </ElDialog>

    <!-- 查看 -->
    <ElDialog v-model="detailVisible" title="查看用户" width="480px" append-to-body>
      <AppNotice v-if="detailError" tone="error">{{ detailError }}</AppNotice>
      <ElDescriptions v-else-if="detail" :column="1" border>
        <ElDescriptionsItem label="用户名">{{ detail.username }}</ElDescriptionsItem>
        <ElDescriptionsItem label="姓名">{{ detail.name }}</ElDescriptionsItem>
        <ElDescriptionsItem label="角色">{{ roleText(detail.role) }}</ElDescriptionsItem>
        <ElDescriptionsItem label="手机号">{{ detail.phone || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem label="邮箱">{{ detail.email || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem label="最近登录时间">
          {{ formatTime(detail.lastLoginAt, '尚未登录') }}
        </ElDescriptionsItem>
      </ElDescriptions>
      <div v-else v-loading="true" style="height: 120px" />
      <template #footer>
        <ElButton @click="detailVisible = false">关闭</ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<script setup lang="ts">
  import { h } from 'vue'
  import { ElMessage, ElMessageBox, ElTag, type FormInstance, type FormRules } from 'element-plus'
  import ArtButtonTable from '@/components/core/forms/art-button-table/index.vue'
  import type { ColumnOption } from '@/types'
  import {
    createUser,
    deleteUser,
    fetchUser,
    fetchUsers,
    updateUser,
    type ManagedUser
  } from '@/api/user'

  defineOptions({ name: 'SystemUser' })

  const users = ref<ManagedUser[]>([])
  const loading = ref(false)
  const listError = ref('')

  const roleText = (role: ManagedUser['role']) => (role === 'Administrator' ? '管理员' : '普通用户')
  const formatTime = (value: string | null, empty = '—') =>
    value ? new Date(value).toLocaleString('zh-CN') : empty

  // ---------- 筛选、排序、分页 ----------
  const filters = reactive({ role: '', name: '', nameMatch: 'fuzzy', keyword: '' })
  const sort = ref<'ascending' | 'descending' | null>(null)
  const pagination = reactive({ current: 1, size: 20, total: 0 })

  const filtered = computed(() => {
    const name = filters.name.trim().toLowerCase()
    const keyword = filters.keyword.trim().toLowerCase()
    const list = users.value.filter((user) => {
      if (filters.role && user.role !== filters.role) return false
      const realName = user.name.toLowerCase()
      if (name && (filters.nameMatch === 'exact' ? realName !== name : !realName.includes(name)))
        return false
      if (!keyword) return true
      return [user.username, user.phone ?? ''].some((text) => text.toLowerCase().includes(keyword))
    })
    if (!sort.value) return list
    const factor = sort.value === 'ascending' ? 1 : -1
    return [...list].sort((a, b) => {
      if (!a.createdAt) return b.createdAt ? 1 : 0
      if (!b.createdAt) return -1
      return (Date.parse(a.createdAt) - Date.parse(b.createdAt)) * factor
    })
  })

  const pageData = computed(() =>
    filtered.value.slice(
      (pagination.current - 1) * pagination.size,
      pagination.current * pagination.size
    )
  )

  watch(
    filtered,
    (list) => {
      pagination.total = list.length
      const last = Math.max(1, Math.ceil(list.length / pagination.size))
      if (pagination.current > last) pagination.current = last
    },
    { immediate: true }
  )
  watch(filters, () => (pagination.current = 1))

  const onSizeChange = (size: number) => {
    pagination.size = size
    pagination.current = 1
  }
  const onPageChange = (page: number) => (pagination.current = page)
  const onSortChange = ({ order }: { order: 'ascending' | 'descending' | null }) => {
    sort.value = order
  }
  const resetFilters = () =>
    Object.assign(filters, { role: '', name: '', nameMatch: 'fuzzy', keyword: '' })

  // ---------- 列表 ----------
  const columns = computed<ColumnOption[]>(() => [
    { type: 'index', width: 60, label: '序号' },
    { prop: 'username', label: '用户名', minWidth: 130 },
    { prop: 'name', label: '真实姓名', minWidth: 110 },
    {
      prop: 'role',
      label: '角色',
      width: 100,
      formatter: (row: ManagedUser) =>
        h(ElTag, { type: row.role === 'Administrator' ? 'danger' : 'info', size: 'small' }, () =>
          roleText(row.role)
        )
    },
    {
      prop: 'phone',
      label: '手机号',
      width: 130,
      formatter: (row: ManagedUser) => row.phone || '—'
    },
    {
      prop: 'email',
      label: '邮箱',
      minWidth: 200,
      formatter: (row: ManagedUser) => row.email || '—'
    },
    {
      prop: 'createdAt',
      label: '创建时间',
      width: 170,
      // 排序在全量数据上由本页完成，需要 Element Plus 的 custom 模式；ColumnOption 类型只声明了 boolean
      sortable: 'custom' as unknown as boolean,
      formatter: (row: ManagedUser) => formatTime(row.createdAt)
    },
    {
      prop: 'lastLoginAt',
      label: '最近登录时间',
      width: 170,
      formatter: (row: ManagedUser) => formatTime(row.lastLoginAt, '尚未登录')
    },
    {
      prop: 'operation',
      label: '操作',
      width: 150,
      fixed: 'right' as const,
      formatter: (row: ManagedUser) =>
        h('div', { class: 'flex gap-1' }, [
          h(ArtButtonTable, {
            icon: 'ri:eye-line',
            iconClass: 'bg-primary/12 text-primary',
            onClick: () => openDetail(row)
          }),
          ...(row.role === 'User'
            ? [
                h(ArtButtonTable, { type: 'edit', onClick: () => openEditor(row) }),
                h(ArtButtonTable, { type: 'delete', onClick: () => confirmDelete(row) })
              ]
            : [])
        ])
    }
  ])

  async function loadUsers() {
    if (loading.value) return
    loading.value = true
    listError.value = ''
    try {
      users.value = await fetchUsers()
    } catch (error) {
      listError.value = error instanceof Error ? error.message : '无法读取用户，请重试。'
    } finally {
      loading.value = false
    }
  }

  // ---------- 添加 / 编辑 ----------
  const editorVisible = ref(false)
  const editing = ref<ManagedUser | null>(null)
  const saving = ref(false)
  const dialogError = ref('')
  const formRef = ref<FormInstance>()
  const form = reactive({ username: '', name: '', password: '', phone: '', email: '' })
  const rules: FormRules = {
    username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
    name: [{ required: true, message: '请输入姓名', trigger: 'blur' }],
    password: [
      { required: true, message: '请输入密码', trigger: 'blur' },
      { min: 6, max: 20, message: '密码为 6–20 个字符', trigger: 'blur' }
    ],
    email: [{ type: 'email', message: '邮箱格式不正确', trigger: 'blur' }]
  }

  function openEditor(user: ManagedUser | null = null) {
    editing.value = user
    Object.assign(form, {
      username: user?.username ?? '',
      name: user?.name ?? '',
      password: '',
      phone: user?.phone ?? '',
      email: user?.email ?? ''
    })
    dialogError.value = ''
    editorVisible.value = true
    nextTick(() => formRef.value?.clearValidate())
  }

  const guardClose = (done: () => void) => {
    if (!saving.value) {
      form.password = ''
      done()
    }
  }

  async function save() {
    if (saving.value || !(await formRef.value?.validate().catch(() => false))) return
    const fields = {
      username: form.username.trim(),
      name: form.name.trim(),
      phone: form.phone.trim() || null,
      email: form.email.trim() || null
    }
    saving.value = true
    dialogError.value = ''
    try {
      if (editing.value) {
        const saved = await updateUser(editing.value.id, fields)
        users.value = users.value.map((user) => (user.id === saved.id ? saved : user))
      } else {
        users.value.push(await createUser({ ...fields, password: form.password }))
      }
      form.password = ''
      editorVisible.value = false
    } catch (error) {
      dialogError.value = error instanceof Error ? error.message : '无法保存用户，请重试。'
    } finally {
      saving.value = false
    }
  }

  // ---------- 查看 / 删除 ----------
  const detailVisible = ref(false)
  const detail = ref<ManagedUser | null>(null)
  const detailError = ref('')
  let detailRequest: AbortController | undefined

  async function openDetail(user: ManagedUser) {
    detail.value = null
    detailError.value = ''
    detailVisible.value = true
    detailRequest?.abort()
    const controller = new AbortController()
    detailRequest = controller
    try {
      const value = await fetchUser(user.id, controller.signal)
      if (!controller.signal.aborted) detail.value = value
    } catch (error) {
      if (!controller.signal.aborted)
        detailError.value = error instanceof Error ? error.message : '无法读取用户详情，请重试。'
    }
  }

  async function confirmDelete(user: ManagedUser) {
    try {
      await ElMessageBox.confirm(
        `确定删除用户 ${user.username}？该用户将无法登录，用户名保留 7 天。`,
        '删除用户',
        { confirmButtonText: '确认删除', cancelButtonText: '取消', type: 'warning' }
      )
    } catch {
      return
    }
    try {
      await deleteUser(user.id)
      users.value = users.value.filter((entry) => entry.id !== user.id)
      ElMessage.success('已删除')
    } catch (error) {
      ElMessage.error(error instanceof Error ? error.message : '无法删除用户，请重试。')
    }
  }

  onMounted(loadUsers)
  onUnmounted(() => detailRequest?.abort())
</script>

<style scoped>
  .form-hint {
    width: 100%;
    font-size: 12px;
    line-height: 18px;
    color: var(--el-text-color-secondary);
  }
</style>
