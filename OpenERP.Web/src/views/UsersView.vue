<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { apiRequest, type Account } from '../account/session'
import AppButton from '../ui/AppButton.vue'
import AppDialog from '../ui/AppDialog.vue'
import AppEmptyState from '../ui/AppEmptyState.vue'
import AppIcon from '../ui/AppIcon.vue'
import AppIconButton from '../ui/AppIconButton.vue'
import AppInput from '../ui/AppInput.vue'
import AppListToolbar from '../ui/AppListToolbar.vue'
import AppTable from '../ui/AppTable.vue'
import AppTd from '../ui/AppTd.vue'
import AppTh from '../ui/AppTh.vue'

type ManagedUser = Omit<Account, 'expiresAt'> & { createdAt: string | null }

const users = ref<ManagedUser[]>([])
const sortDirection = ref<'ascending' | 'descending' | null>(null)
const sortedUsers = computed(() => {
  if (!sortDirection.value) return users.value
  const factor = sortDirection.value === 'ascending' ? 1 : -1
  return [...users.value].sort((left, right) => {
    if (!left.createdAt) return right.createdAt ? 1 : 0
    if (!right.createdAt) return -1
    return (Date.parse(left.createdAt) - Date.parse(right.createdAt)) * factor
  })
})

function toggleCreatedSort(): void {
  sortDirection.value = sortDirection.value === 'ascending' ? 'descending' : 'ascending'
}

const loading = ref(false)
const listError = ref('')
const mode = ref<'create' | 'view' | 'edit' | 'delete' | null>(null)
const titles = { create: '添加用户', view: '查看用户', edit: '编辑用户', delete: '删除用户' }
const selectedUser = ref<ManagedUser | null>(null)
const userPath = computed(() =>
  selectedUser.value ? `/api/users/${selectedUser.value.id}` : '/api/users',
)
const detail = ref<ManagedUser | null>(null)
let detailRequest: AbortController | undefined
const saving = ref(false)
const addButton = ref<InstanceType<typeof AppButton> | null>(null)
let focusAddOnClose = false
const dialogError = ref('')
const inputClass = 'flex flex-col gap-4 text-12/18 text-ink'
const dialogFooterClass = 'flex h-60 items-center justify-end gap-8 border-t border-line px-20'
const fields = reactive({ username: '', name: '', password: '', phone: '', email: '' })

function openEditor(user: ManagedUser | null = null): void {
  selectedUser.value = user
  Object.assign(fields, {
    username: user?.username ?? '',
    name: user?.name ?? '',
    password: '',
    phone: user?.phone ?? '',
    email: user?.email ?? '',
  })
  dialogError.value = ''
  mode.value = user ? 'edit' : 'create'
}

function closeDialog(): void {
  if (saving.value) return
  detailRequest?.abort()
  fields.password = ''
  mode.value = null
}

async function openDetails(user: ManagedUser): Promise<void> {
  detail.value = null
  dialogError.value = ''
  mode.value = 'view'

  const controller = new AbortController()
  detailRequest = controller
  try {
    const response = await apiRequest(`/api/users/${user.id}`, { signal: controller.signal })
    if (!response.ok) throw new Error()
    const value: ManagedUser = await response.json()
    if (!controller.signal.aborted) detail.value = value
  } catch {
    if (!controller.signal.aborted) dialogError.value = '无法读取用户详情，请重试。'
  } finally {
    if (detailRequest === controller) detailRequest = undefined
  }
}

function confirmDelete(user: ManagedUser): void {
  selectedUser.value = user
  dialogError.value = ''
  mode.value = 'delete'
}

function restoreFocus(): void {
  if (!focusAddOnClose) return
  focusAddOnClose = false
  addButton.value?.$el.focus()
}

async function deleteUser(): Promise<void> {
  const user = selectedUser.value
  if (saving.value || !user) return
  saving.value = true
  dialogError.value = ''
  try {
    const response = await apiRequest(userPath.value, { method: 'DELETE' })
    if (!response.ok) throw new Error()
    users.value = users.value.filter((entry) => entry.id !== user.id)
    focusAddOnClose = true
    mode.value = null
  } catch {
    dialogError.value = '无法删除用户，请重试。'
  } finally {
    saving.value = false
  }
}

async function saveUser(): Promise<void> {
  if (saving.value) return
  const creating = mode.value === 'create'
  const fieldsToSave = {
    username: fields.username.trim(),
    name: fields.name.trim(),
    phone: fields.phone.trim() || null,
    email: fields.email.trim() || null,
  }
  const fallback = creating ? '无法添加用户，请重试。' : '无法保存用户，请重试。'
  saving.value = true
  dialogError.value = ''
  try {
    const response = await apiRequest(userPath.value, {
      method: creating ? 'POST' : 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(
        creating ? { ...fieldsToSave, password: fields.password } : fieldsToSave,
      ),
    })
    if (!response.ok) {
      const problem = await response.json().catch(() => null)
      const errors = Object.values(problem?.errors ?? {})
        .flat()
        .filter((message): message is string => typeof message === 'string')
      dialogError.value =
        typeof problem?.detail === 'string' ? problem.detail : errors.join(' ') || fallback
      return
    }
    const saved: ManagedUser = await response.json()
    if (creating) users.value.push(saved)
    else users.value = users.value.map((user) => (user.id === saved.id ? saved : user))
    fields.password = ''
    mode.value = null
  } catch {
    dialogError.value = fallback
  } finally {
    saving.value = false
  }
}

async function loadUsers(): Promise<void> {
  if (loading.value) return
  loading.value = true
  listError.value = ''
  try {
    const response = await apiRequest('/api/users')
    if (!response.ok) throw new Error('无法读取用户，请重试。')
    users.value = await response.json()
  } catch {
    listError.value = '无法读取用户，请重试。'
  } finally {
    loading.value = false
  }
}

onMounted(loadUsers)
onUnmounted(() => detailRequest?.abort())
</script>

<template>
  <section id="page-users" class="flex h-full min-h-0 w-full flex-col overflow-hidden bg-white">
    <h1 class="sr-only">用户管理</h1>
    <AppListToolbar>
      <AppButton
        ref="addButton"
        variant="primary"
        :disabled="loading"
        aria-haspopup="dialog"
        data-testid="add-managed-user"
        @click="openEditor()"
      >
        添加用户
      </AppButton>
      <template #right>
        <AppIconButton label="刷新" tone="toolbar" :disabled="loading" @click="loadUsers">
          <AppIcon name="i-refresh" />
        </AppIconButton>
      </template>
    </AppListToolbar>
    <AppTable
      caption="用户管理列表"
      label="用户管理表格"
      width="100%"
      :min-width="880"
      scroll-y
      focus-ring
    >
      <thead>
        <tr>
          <AppTh>用户名</AppTh>
          <AppTh>姓名</AppTh>
          <AppTh class="w-88">角色</AppTh>
          <AppTh>手机号</AppTh>
          <AppTh>邮箱</AppTh>
          <AppTh data-sort="created" :aria-sort="sortDirection ?? undefined" class="w-145">
            <button
              class="inline-flex min-h-24 items-center gap-3 text-left"
              type="button"
              aria-label="按创建时间排序"
              @click="toggleCreatedSort"
            >
              创建时间<AppIcon name="i-sort" icon-class="h-12 w-8 text-primary" />
            </button>
          </AppTh>
          <AppTh class="w-145">最近登录时间</AppTh>
          <AppTh class="w-146">操作</AppTh>
        </tr>
      </thead>
      <tbody>
        <tr v-for="user in sortedUsers" :key="user.id">
          <th
            scope="row"
            class="h-48 overflow-hidden border-r border-b border-line-soft bg-white px-8 text-left text-12/18 font-semibold text-ellipsis whitespace-nowrap text-ink"
            :title="user.username"
          >
            {{ user.username }}
          </th>
          <AppTd>{{ user.name }}</AppTd>
          <AppTd>{{ user.role === 'Administrator' ? '管理员' : '普通用户' }}</AppTd>
          <AppTd>{{ user.phone || '—' }}</AppTd>
          <AppTd :title="user.email ?? undefined">{{ user.email || '—' }}</AppTd>
          <AppTd data-testid="user-created-at">
            <time v-if="user.createdAt" :datetime="user.createdAt">
              {{ new Date(user.createdAt).toLocaleString('zh-CN') }}
            </time>
            <span v-else>—</span>
          </AppTd>
          <AppTd>
            <time v-if="user.lastLoginAt" :datetime="user.lastLoginAt">
              {{ new Date(user.lastLoginAt).toLocaleString('zh-CN') }}
            </time>
            <span v-else>尚未登录</span>
          </AppTd>
          <AppTd>
            <div class="flex items-center gap-8">
              <AppButton
                variant="text"
                :aria-label="`查看：${user.username}`"
                :disabled="loading"
                aria-haspopup="dialog"
                @click="openDetails(user)"
                >查看</AppButton
              >
              <template v-if="user.role === 'User'">
                <AppButton
                  variant="text"
                  :aria-label="`编辑：${user.username}`"
                  :disabled="loading"
                  aria-haspopup="dialog"
                  @click="openEditor(user)"
                  >编辑</AppButton
                >
                <AppButton
                  variant="text"
                  :aria-label="`删除：${user.username}`"
                  :disabled="loading"
                  aria-haspopup="dialog"
                  @click="confirmDelete(user)"
                  >删除</AppButton
                >
              </template>
            </div>
          </AppTd>
        </tr>
      </tbody>
      <template #overlay>
        <AppEmptyState v-if="loading" role="status" description="正在加载用户…" />
        <AppEmptyState v-else-if="listError" role="alert" :description="listError" />
        <AppEmptyState v-else-if="!users.length" description="暂无用户" />
      </template>
      <template #footer>
        <div
          class="flex h-44 shrink-0 items-center justify-end border-t border-line-soft px-12 text-12/18 text-body"
        >
          共 {{ users.length }} 条
        </div>
      </template>
    </AppTable>

    <AppDialog
      id="managed-user-dialog"
      :open="mode !== null"
      :closable="!saving"
      backdrop="dim"
      aria-labelledby="user-dialog-title"
      :aria-describedby="mode === 'delete' ? 'user-delete-description' : undefined"
      class="fixed inset-0 m-auto w-520 max-w-[calc(100%_-_40px)] overflow-hidden rounded-8 border-0 bg-white p-0 text-ink shadow-dialog"
      @close="closeDialog"
      @closed="restoreFocus"
    >
      <header v-if="mode" class="flex h-52 items-center border-b border-line px-20">
        <h2 id="user-dialog-title" class="text-dialog-title font-semibold">{{ titles[mode] }}</h2>
      </header>
      <div v-if="mode === 'view'">
        <div class="px-24 py-20">
          <p v-if="dialogError" class="text-danger" role="alert">{{ dialogError }}</p>
          <dl
            v-else-if="detail"
            class="grid grid-cols-[max-content_minmax(0,1fr)] gap-x-20 gap-y-12 text-12/18 [&_dt]:text-muted [&_dd]:[overflow-wrap:anywhere]"
          >
            <dt>用户名</dt>
            <dd>{{ detail.username }}</dd>
            <dt>姓名</dt>
            <dd>{{ detail.name }}</dd>
            <dt>角色</dt>
            <dd>{{ detail.role === 'Administrator' ? '管理员' : '普通用户' }}</dd>
            <dt>手机号</dt>
            <dd>{{ detail.phone || '—' }}</dd>
            <dt>邮箱</dt>
            <dd>{{ detail.email || '—' }}</dd>
            <dt>最近登录时间</dt>
            <dd>
              <time v-if="detail.lastLoginAt" :datetime="detail.lastLoginAt">
                {{ new Date(detail.lastLoginAt).toLocaleString('zh-CN') }}
              </time>
              <span v-else>尚未登录</span>
            </dd>
          </dl>
          <p v-else role="status">正在加载用户详情…</p>
        </div>
        <footer :class="dialogFooterClass">
          <AppButton autofocus @click="closeDialog">关闭</AppButton>
        </footer>
      </div>
      <form
        v-else-if="mode === 'delete'"
        method="post"
        :action="userPath"
        @submit.prevent="deleteUser"
      >
        <div class="flex flex-col gap-12 px-24 py-20">
          <p id="user-delete-description">
            确定删除用户 <strong>{{ selectedUser?.username }}</strong
            >？该用户将无法登录，用户名保留 7 天。
          </p>
          <p v-if="dialogError" class="text-danger" role="alert">{{ dialogError }}</p>
        </div>
        <footer :class="dialogFooterClass">
          <AppButton :disabled="saving" autofocus @click="closeDialog">取消</AppButton>
          <AppButton variant="text" type="submit" :disabled="saving">
            <span class="text-danger-deep">{{ saving ? '删除中…' : '确认删除' }}</span>
          </AppButton>
        </footer>
      </form>
      <form
        v-else-if="mode === 'create' || mode === 'edit'"
        method="post"
        :action="userPath"
        @submit.prevent="saveUser"
      >
        <div class="flex max-h-[calc(100dvh_-_156px)] flex-col gap-12 overflow-y-auto px-24 py-20">
          <div :class="inputClass">
            <label for="managed-username"
              >用户名<span aria-hidden="true" class="text-danger"> *</span></label
            >
            <AppInput
              id="managed-username"
              v-model="fields.username"
              name="username"
              required
              maxlength="20"
              autocapitalize="none"
              spellcheck="false"
              autocomplete="off"
              :disabled="saving"
              autofocus
            />
          </div>
          <div :class="inputClass">
            <label for="managed-name"
              >姓名<span aria-hidden="true" class="text-danger"> *</span></label
            >
            <AppInput
              id="managed-name"
              v-model="fields.name"
              name="name"
              required
              maxlength="20"
              autocomplete="off"
              :disabled="saving"
            />
          </div>
          <div v-if="mode === 'create'" :class="inputClass">
            <label for="managed-password"
              >密码<span aria-hidden="true" class="text-danger"> *</span></label
            >
            <AppInput
              id="managed-password"
              v-model="fields.password"
              name="password"
              type="password"
              required
              minlength="6"
              maxlength="20"
              autocomplete="new-password"
              aria-describedby="managed-password-hint"
              :disabled="saving"
            />
            <small id="managed-password-hint" class="text-12/18 text-muted">6–20 个字符</small>
          </div>
          <div :class="inputClass">
            <label for="managed-phone">手机号</label>
            <AppInput
              id="managed-phone"
              v-model="fields.phone"
              name="phone"
              type="tel"
              maxlength="20"
              autocomplete="off"
              :disabled="saving"
            />
          </div>
          <div :class="inputClass">
            <label for="managed-email">邮箱</label>
            <AppInput
              id="managed-email"
              v-model="fields.email"
              name="email"
              type="email"
              autocomplete="off"
              :disabled="saving"
            />
          </div>
          <p v-if="dialogError" class="text-danger" role="alert">{{ dialogError }}</p>
        </div>
        <footer :class="dialogFooterClass">
          <AppButton :disabled="saving" @click="closeDialog">取消</AppButton>
          <AppButton variant="primary" type="submit" :disabled="saving">
            {{ saving ? '保存中…' : '保存' }}
          </AppButton>
        </footer>
      </form>
    </AppDialog>
  </section>
</template>
