<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    scope?: 'col' | 'row'
    numeric?: boolean
    sticky?: 'none' | 'left' | 'right'
    /** 订单卡片表头沿用旧 `--page-soft` 底色。 */
    surface?: 'white' | 'page-soft'
  }>(),
  {
    scope: 'col',
    numeric: false,
    sticky: 'none',
    surface: 'white',
  },
)

const classes = computed(() => [
  'sticky top-0 h-40 overflow-hidden border-b border-b-line text-12/18 font-semibold text-ellipsis whitespace-nowrap text-ink',
  props.surface === 'page-soft' ? 'bg-page-soft' : 'bg-white',
  props.sticky === 'none' ? 'z-[3]' : 'z-[6]',
  props.numeric ? 'text-right tabular-nums' : 'text-left',
  props.sticky === 'left'
    ? 'left-0 z-[6] w-28 border-r border-r-line-soft pr-0'
    : props.sticky === 'right'
      ? 'right-0 z-[6] border-l border-l-line-soft px-12'
      : 'border-r border-r-line-soft px-8',
  'last:border-r-0',
])
</script>

<template>
  <th :scope="scope" :data-numeric="String(numeric)" :data-sticky="sticky" :class="classes">
    <slot />
  </th>
</template>
