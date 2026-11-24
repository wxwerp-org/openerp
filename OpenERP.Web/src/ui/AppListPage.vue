<script setup lang="ts">
import AppListSearchbar from './AppListSearchbar.vue'
import AppListToolbar from './AppListToolbar.vue'

/**
 * 列表页壳：提示区 → 筛选条 → 工具条 → 通栏表格，与设计规范原型的列表页一致。
 *
 * 页面只往插槽里放内容：`header` 放提示与汇总，`filters` 放筛选项，`toolbar` 放主操作，
 * `toolbar-right` 放刷新等次要操作，默认插槽放 `AppTable`。页面标题只对读屏器可见，
 * 视觉上由页签承担，避免每个页面再画一个大标题。
 */
withDefaults(
  defineProps<{
    title: string
    filtersLabel?: string
    /** `form` 时回车或点击提交按钮会触发 `search`。 */
    filtersAs?: 'search' | 'form'
  }>(),
  {
    filtersLabel: '筛选',
    filtersAs: 'search',
  },
)

const emit = defineEmits<{
  search: []
}>()
</script>

<template>
  <section class="flex h-full min-h-0 w-full flex-col overflow-hidden bg-white">
    <h1 class="sr-only">{{ title }}</h1>
    <div v-if="$slots.header" class="flex-none px-12 pt-12">
      <slot name="header" />
    </div>
    <AppListSearchbar
      v-if="$slots.filters"
      :as="filtersAs"
      :label="filtersLabel"
      @submit.prevent="emit('search')"
    >
      <slot name="filters" />
    </AppListSearchbar>
    <AppListToolbar v-if="$slots.toolbar || $slots['toolbar-right']">
      <slot name="toolbar" />
      <template v-if="$slots['toolbar-right']" #right>
        <slot name="toolbar-right" />
      </template>
    </AppListToolbar>
    <slot />
  </section>
</template>
