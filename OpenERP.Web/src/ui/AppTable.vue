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
  }>(),
  {
    wrap: false,
    scrollY: false,
    width: undefined,
  },
)

provide(
  tableWrapKey,
  computed(() => props.wrap),
)

const tableStyle = computed(() => {
  if (props.width == null) return undefined
  return { width: typeof props.width === 'number' ? `${props.width}px` : props.width }
})
</script>

<template>
  <div class="min-h-0 flex-1 bg-white px-12 pb-18">
    <div
      class="flex h-full min-h-0 flex-col overflow-hidden rounded-b-4 border border-line bg-white"
    >
      <div class="relative min-h-0 flex-1 overflow-hidden bg-white">
        <div
          class="app-table-scroll relative h-full overflow-x-auto bg-white"
          :class="scrollY ? 'is-y overflow-y-auto' : 'overflow-y-hidden'"
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
  background: var(--color-scroll-track);
}

.app-table-scroll::-webkit-scrollbar-thumb {
  background: var(--color-scroll-thumb);
  border: 2px solid var(--color-scroll-track);
  border-radius: 8px;
}

.app-table tbody tr:hover td {
  background: var(--color-row-hover);
}

.app-table.is-wrap tbody td {
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
