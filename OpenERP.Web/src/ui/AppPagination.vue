<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import AppInput from './AppInput.vue'
import AppPager from './AppPager.vue'
import AppSelect from './AppSelect.vue'

/**
 * 列表分页条：总数文字 + 翻页 + 每页条数 + 前往页码。
 *
 * 业务页只传页码、页大小和总页数，跳页的取整、越界收敛与加载中忽略失焦提交都在这里统一处理。
 */
const props = withDefaults(
  defineProps<{
    page: number
    pageCount: number
    pageSize: number
    /** 总数文字，例如「共 3 条」；缺省则不显示。 */
    totalText?: string
    pageSizeOptions?: number[]
    label?: string
    disabled?: boolean
    as?: 'nav' | 'div' | 'footer'
    paddingX?: 4 | 8 | 12 | 16
    focusRing?: boolean
  }>(),
  {
    totalText: undefined,
    pageSizeOptions: () => [20, 50, 100],
    label: '分页',
    disabled: false,
    as: 'nav',
    paddingX: 12,
    focusRing: false,
  },
)

const emit = defineEmits<{
  'update:page': [page: number]
  'update:pageSize': [size: number]
}>()

const jump = ref(String(props.page))
watch(
  () => props.page,
  (page) => {
    jump.value = String(page)
  },
)

const total = computed(() => Math.max(1, Math.floor(props.pageCount) || 1))
const sizeOptions = computed(() =>
  props.pageSizeOptions.map((size) => ({ value: size, label: `${size}条/页` })),
)

function onPageSize(value: string | number): void {
  const size = Number(value)
  if (props.pageSizeOptions.includes(size)) emit('update:pageSize', size)
}

function submitJump(): void {
  // 加载期间输入框被禁用会触发失焦，此时页数已回退，继续提交会把刚发出的跳页请求收敛回第 1 页。
  if (props.disabled) {
    jump.value = String(props.page)
    return
  }
  const requested = Number.parseInt(jump.value, 10)
  if (!Number.isInteger(requested)) {
    jump.value = String(props.page)
    return
  }
  const target = Math.min(Math.max(requested, 1), total.value)
  jump.value = String(target)
  if (target !== props.page) emit('update:page', target)
}
</script>

<template>
  <AppPager
    :as="as"
    :page="page"
    :page-count="pageCount"
    :show-page-list="pageCount > 0"
    :disabled="disabled"
    :label="label"
    :padding-x="paddingX"
    :focus-ring="focusRing"
    @update:page="emit('update:page', $event)"
  >
    <template v-if="totalText" #prefix>
      <span>{{ totalText }}</span>
    </template>
    <template #suffix>
      <AppSelect
        :model-value="pageSize"
        :options="sizeOptions"
        :disabled="disabled"
        size="sm"
        class="min-w-100"
        :focus-ring="focusRing"
        aria-label="每页条数"
        @update:model-value="onPageSize"
      />
      <span class="inline-flex items-center gap-7 pl-3 text-body">
        前往
        <AppInput
          v-model="jump"
          size="sm"
          class="w-46 [&_input]:text-center"
          :focus-ring="focusRing"
          inputmode="numeric"
          aria-label="前往页码"
          :disabled="disabled || pageCount < 1"
          @keydown.enter.prevent="submitJump"
          @blur="submitJump"
        />
        页
      </span>
    </template>
  </AppPager>
</template>
