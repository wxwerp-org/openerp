<script setup lang="ts">
import { computed, provide } from 'vue'
import { tableWrapKey } from './tableContext'

const props = withDefaults(
  defineProps<{
    caption: string
    label: string
    wrap?: boolean
    scrollY?: boolean
    width?: number | string
    minWidth?: number
    /** `list` 是列表页标准外壳；`plain` 只提供滚动区，用于弹窗等已有外框的容器。 */
    variant?: 'list' | 'plain'
    /** 聚焦环落在滚动区上：2px 主色、内缩 2px。 */
    focusRing?: boolean
  }>(),
  {
    wrap: false,
    scrollY: false,
    width: undefined,
    minWidth: undefined,
    variant: 'list',
    focusRing: false,
  },
)

provide(
  tableWrapKey,
  computed(() => props.wrap),
)

const isPlain = computed(() => props.variant === 'plain')

const tableStyle = computed(() => {
  const style: { width?: string; minWidth?: string } = {}
  if (props.width != null)
    style.width = typeof props.width === 'number' ? `${props.width}px` : props.width
  if (props.minWidth != null) style.minWidth = `${props.minWidth}px`
  return Object.keys(style).length ? style : undefined
})
</script>

<template>
  <div class="min-h-0 flex-1 bg-white" :class="isPlain ? '' : 'px-12 pb-18'">
    <div
      class="flex h-full min-h-0 flex-col overflow-hidden bg-white"
      :class="isPlain ? '' : 'rounded-b-4 border border-line'"
    >
      <div class="relative min-h-0 flex-1 overflow-hidden bg-white">
        <div
          class="app-table-scroll relative h-full overflow-x-auto bg-white"
          :class="[
            scrollY ? 'is-y overflow-y-auto' : 'overflow-y-hidden',
            focusRing
              ? 'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary'
              : '',
          ]"
          :data-scroll-y="String(scrollY)"
          tabindex="0"
          role="region"
          :aria-label="label"
        >
          <table
            class="app-table min-w-full table-fixed border-separate border-spacing-0 bg-white"
            :class="{ 'is-wrap': wrap }"
            :data-wrap="String(wrap)"
            :style="tableStyle"
          >
            <caption class="sr-only">
              {{
                caption
              }}
            </caption>
            <slot />
          </table>
          <slot name="scroll-end" />
        </div>
        <slot name="overlay" />
      </div>
      <slot name="footer" />
    </div>
  </div>
</template>

<style scoped>
.app-table-scroll::-webkit-scrollbar {
  width: 10px;
  height: 10px;
}

.app-table-scroll::-webkit-scrollbar-track {
  background: var(--color-page);
}

.app-table-scroll::-webkit-scrollbar-thumb {
  background: var(--color-scroll-thumb);
  border: 2px solid var(--color-page);
  border-radius: 8px;
}

.app-table :deep(tbody tr:hover td:not([data-surface='page-soft'])) {
  background: var(--color-row-hover);
}

.app-table.is-wrap :deep(tbody td) {
  height: auto;
  padding-top: 14px;
  padding-bottom: 14px;
  white-space: normal;
  overflow: visible;
  text-overflow: clip;
  vertical-align: top;
  overflow-wrap: anywhere;
}
</style>
