<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { apiRequest } from '../../account/session'
import AppButton from '../../ui/AppButton.vue'
import AppCheckbox from '../../ui/AppCheckbox.vue'
import AppInput from '../../ui/AppInput.vue'
import AppPager from '../../ui/AppPager.vue'
import AppSelect from '../../ui/AppSelect.vue'

type ShopKey = { platform: string; sid: number }
type ShopOption = ShopKey & { name: string }
type ShopPage = {
  items: { platform: string; sid: string; name: string }[]
  page: number
  pageCount: number
}

const props = defineProps<{ userId: string; username: string }>()
const saving = defineModel<boolean>('saving', { required: true })
const emit = defineEmits<{ close: [] }>()

const path = computed(() => `/api/users/${props.userId}/shops`)
const selected = ref<ShopKey[]>([])
const knownShops = new Map<string, string>()
const shops = ref<ShopOption[]>([])
const platformOptions = ref<{ value: string; label: string }[]>([{ value: '', label: '全部平台' }])
const platform = ref('')
const draft = ref('')
const keyword = ref('')
const page = ref(1)
const pageCount = ref(0)
const assignmentsLoading = ref(true)
const assignmentsError = ref('')
const directoryLoading = ref(true)
const directoryError = ref('')
const saveError = ref('')
const lifetime = new AbortController()
let directoryRequest: AbortController | undefined

function key(shop: ShopKey): string {
  return `${shop.platform}/${shop.sid}`
}

function label(shop: ShopKey): string {
  const identifier = `${shop.platform} / ${shop.sid}`
  const name = knownShops.get(key(shop))
  return name ? `${name}（${identifier}）` : identifier
}

function isSelected(shop: ShopKey): boolean {
  return selected.value.some((entry) => key(entry) === key(shop))
}

function remove(shop: ShopKey): void {
  selected.value = selected.value.filter((entry) => key(entry) !== key(shop))
}

function changeSelection(shop: ShopOption, checked: boolean | (string | number)[]): void {
  if (checked === true && !isSelected(shop)) {
    selected.value.push({ platform: shop.platform, sid: shop.sid })
  } else if (checked === false) {
    remove(shop)
  }
}

async function loadAssignments(): Promise<void> {
  assignmentsLoading.value = true
  assignmentsError.value = ''
  try {
    const response = await apiRequest(path.value, { signal: lifetime.signal, cache: 'no-store' })
    if (!response.ok) throw new Error()
    selected.value = await response.json()
  } catch {
    if (!lifetime.signal.aborted) assignmentsError.value = '无法读取已分配店铺，请重试。'
  } finally {
    assignmentsLoading.value = false
  }
}

async function loadPlatforms(): Promise<void> {
  try {
    const response = await apiRequest('/api/erp/platforms', { signal: lifetime.signal })
    if (!response.ok) return
    const catalog: { items: { name: string; displayName: string }[] } = await response.json()
    platformOptions.value.push(
      ...catalog.items.map((item) => ({ value: item.name, label: item.displayName })),
    )
  } catch {
    // 平台列表不可用时仍可按关键字检索。
  }
}

async function loadShops(target: number): Promise<void> {
  directoryRequest?.abort()
  const controller = new AbortController()
  directoryRequest = controller
  directoryLoading.value = true
  directoryError.value = ''
  const query = new URLSearchParams({ page: String(target), pageSize: '20' })
  if (platform.value) query.set('platform', platform.value)
  if (keyword.value) query.set('keyword', keyword.value)
  try {
    const response = await apiRequest(`/api/erp/shops?${query}`, { signal: controller.signal })
    if (!response.ok) throw new Error()
    const result: ShopPage = await response.json()
    if (controller.signal.aborted) return
    shops.value = result.items.map((item) => ({ ...item, sid: Number(item.sid) }))
    for (const shop of shops.value) knownShops.set(key(shop), shop.name)
    page.value = result.page
    pageCount.value = result.pageCount
  } catch {
    if (!controller.signal.aborted) directoryError.value = '无法读取店铺目录，请重试。'
  } finally {
    if (!controller.signal.aborted) directoryLoading.value = false
  }
}

function search(): void {
  keyword.value = draft.value.trim()
  void loadShops(1)
}

function selectPlatform(value: string | number): void {
  platform.value = String(value)
  search()
}

async function save(): Promise<void> {
  if (saving.value || assignmentsLoading.value || assignmentsError.value) return
  saving.value = true
  saveError.value = ''
  try {
    const response = await apiRequest(path.value, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(selected.value),
    })
    if (response.ok) {
      emit('close')
      return
    }
    const problem: { detail?: string } | null = await response.json().catch(() => null)
    saveError.value = problem?.detail || '无法保存店铺分配，请重试。'
  } catch {
    saveError.value = '无法保存店铺分配，请重试。'
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  void loadAssignments()
  void loadPlatforms()
  void loadShops(1)
})
onUnmounted(() => {
  lifetime.abort()
  directoryRequest?.abort()
})
</script>

<template>
  <div
    class="flex max-h-[calc(100dvh_-_156px)] flex-col gap-16 overflow-y-auto px-24 py-20 text-12/18"
  >
    <p>
      为 <strong>{{ username }}</strong> 选择可查看和同步的店铺。
    </p>
    <fieldset class="rounded-4 border border-line px-12 pb-10" :disabled="saving">
      <legend class="px-4 font-semibold">已选店铺（{{ selected.length }}）</legend>
      <p v-if="assignmentsLoading" role="status" class="text-muted">正在加载已分配店铺…</p>
      <div v-else-if="assignmentsError" class="flex items-center gap-8">
        <p role="alert" class="text-danger">{{ assignmentsError }}</p>
        <AppButton @click="loadAssignments">重试</AppButton>
      </div>
      <p v-else-if="!selected.length" class="text-muted">尚未选择店铺</p>
      <ul v-else class="flex max-h-108 flex-col gap-4 overflow-y-auto">
        <li v-for="shop in selected" :key="key(shop)" class="flex justify-between gap-8">
          <span class="self-center">{{ label(shop) }}</span>
          <AppButton variant="text" :aria-label="`移除 ${label(shop)}`" @click="remove(shop)"
            >移除</AppButton
          >
        </li>
      </ul>
    </fieldset>
    <form
      class="flex flex-wrap items-center gap-8"
      role="search"
      aria-label="搜索店铺"
      @submit.prevent="search"
    >
      <AppSelect
        aria-label="店铺平台"
        size="sm"
        :model-value="platform"
        :options="platformOptions"
        :disabled="saving"
        @update:model-value="selectPlatform"
      />
      <AppInput
        v-model="draft"
        aria-label="店铺关键字"
        type="search"
        size="sm"
        placeholder="店名或 SID"
        :disabled="saving"
        autofocus
      />
      <AppButton type="submit" :disabled="saving">搜索</AppButton>
    </form>
    <fieldset
      class="m-0 min-w-0 border-0 p-0"
      :disabled="assignmentsLoading || Boolean(assignmentsError) || saving"
    >
      <legend class="mb-4 font-semibold">店铺目录</legend>
      <p v-if="directoryLoading" role="status" class="text-muted">正在加载店铺目录…</p>
      <div v-else-if="directoryError" class="flex items-center gap-8">
        <p role="alert" class="text-danger">{{ directoryError }}</p>
        <AppButton @click="loadShops(page)">重试</AppButton>
      </div>
      <p v-else-if="!shops.length" class="text-muted">没有匹配的店铺</p>
      <ul v-else class="flex max-h-184 flex-col overflow-y-auto rounded-4 border border-line px-12">
        <li
          v-for="shop in shops"
          :key="key(shop)"
          class="[&_label]:flex [&_label]:min-h-44 [&_label]:whitespace-normal"
        >
          <AppCheckbox
            :model-value="isSelected(shop)"
            :value="key(shop)"
            :label="label(shop)"
            class="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
            @update:model-value="changeSelection(shop, $event)"
          />
        </li>
      </ul>
      <AppPager
        v-if="pageCount > 1"
        :page="page"
        :page-count="pageCount"
        :disabled="directoryLoading || saving"
        label="可分配店铺分页"
        class="mt-8"
        @update:page="loadShops"
      />
    </fieldset>
    <p v-if="saveError" role="alert" class="text-danger">{{ saveError }}</p>
  </div>
  <form
    data-testid="save-shop-assignments"
    class="flex h-60 items-center justify-end gap-8 border-t border-line px-20"
    @submit.prevent="save"
  >
    <AppButton :disabled="saving" @click="emit('close')">取消</AppButton>
    <AppButton
      type="submit"
      variant="primary"
      :disabled="assignmentsLoading || Boolean(assignmentsError) || saving"
    >
      {{ saving ? '保存中…' : '保存' }}
    </AppButton>
  </form>
</template>
