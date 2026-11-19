<script setup lang="ts">
import { computed } from 'vue'
import AppIcon from './AppIcon.vue'
import { buildPageList, type PageItem } from './pager'

const props = withDefaults(
  defineProps<{
    page: number
    pageCount: number
    label?: string
    showPageList?: boolean
    disabled?: boolean
    as?: 'nav' | 'div' | 'footer'
  }>(),
  {
    label: '分页',
    showPageList: true,
    disabled: false,
    as: 'nav',
  },
)

const emit = defineEmits<{
  'update:page': [page: number]
}>()

const pageButtonClass =
  'inline-flex h-28 w-28 shrink-0 items-center justify-center rounded-4 border border-transparent bg-white text-body hover:bg-page-btn-hover hover:text-primary disabled:cursor-default disabled:text-btn-disabled-text disabled:hover:bg-transparent disabled:hover:text-btn-disabled-text'

const total = computed(() => Math.max(1, Math.floor(props.pageCount) || 1))
const pages = computed<PageItem[]>(() => buildPageList(props.page, total.value))
const previousDisabled = computed(() => props.disabled || props.page <= 1)
const nextDisabled = computed(() => props.disabled || props.page >= total.value)

function go(page: number): void {
  if (props.disabled) return
  const target = Math.min(Math.max(Math.floor(page), 1), total.value)
  if (target !== props.page) emit('update:page', target)
}
</script>

<template>
  <component
    :is="as"
    :aria-label="as === 'nav' ? label : undefined"
    :data-disabled="String(disabled)"
    class="flex h-[var(--control-h)] flex-none items-center justify-end gap-8 bg-white px-4 text-12/18 text-body whitespace-nowrap"
  >
    <slot name="prefix" />
    <button
      type="button"
      aria-label="上一页"
      :disabled="previousDisabled"
      :class="pageButtonClass"
      @click="go(page - 1)"
    >
      <AppIcon name="i-chev-left" :size="12" />
    </button>
    <template v-if="showPageList">
      <template v-for="(item, index) in pages" :key="`${item}-${index}`">
        <span
          v-if="item === 'ellipsis'"
          class="inline-flex min-w-18 items-center justify-center text-num-muted"
        >
          …
        </span>
        <span
          v-else-if="item === page"
          aria-current="page"
          class="inline-flex h-28 min-w-28 items-center justify-center rounded-4 border border-page-num-border bg-nav-active px-6 text-primary tabular-nums"
        >
          {{ item }}
        </span>
        <button
          v-else
          type="button"
          :aria-label="`第 ${item} 页`"
          :disabled="disabled"
          :class="pageButtonClass"
          @click="go(item)"
        >
          {{ item }}
        </button>
      </template>
    </template>
    <button
      type="button"
      aria-label="下一页"
      :disabled="nextDisabled"
      :class="pageButtonClass"
      @click="go(page + 1)"
    >
      <AppIcon name="i-chev-right" :size="12" />
    </button>
    <slot name="suffix" />
  </component>
</template>
