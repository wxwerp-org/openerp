<script setup lang="ts">
import { computed, inject } from 'vue'
import { tableWrapKey } from './tableContext'

const props = withDefaults(
  defineProps<{
    numeric?: boolean
    sticky?: 'none' | 'left' | 'right'
  }>(),
  {
    numeric: false,
    sticky: 'none',
  },
)

const wrap = inject(
  tableWrapKey,
  computed(() => false),
)

const classes = computed(() => [
  'border-b border-line-soft bg-white text-12/18 text-ellipsis text-body',
  wrap.value
    ? 'h-auto overflow-visible py-14 align-top [overflow-wrap:anywhere] whitespace-normal [text-overflow:clip]'
    : 'h-48 overflow-hidden whitespace-nowrap',
  props.numeric ? 'text-right tabular-nums' : '',
  props.sticky === 'left'
    ? 'sticky left-0 z-[4] w-28 border-r border-line-soft pl-8 pr-0'
    : props.sticky === 'right'
      ? 'sticky right-0 z-[4] border-l border-line-soft px-12'
      : 'border-r border-line-soft px-8',
  'last:border-r-0',
])
</script>

<template>
  <td :data-numeric="String(numeric)" :data-sticky="sticky" :class="classes">
    <slot />
  </td>
</template>
