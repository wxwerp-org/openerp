<script setup lang="ts">
import { computed, inject } from 'vue'
import { tableWrapKey } from './tableContext'

const props = withDefaults(
  defineProps<{
    numeric?: boolean
    sticky?: 'none' | 'left' | 'right'
    /** 少数字段（如用户名）沿用旧 `color: var(--text)` 而非正文色。 */
    tone?: 'body' | 'ink'
    /** 展开行等需要整格换底色时沿用旧 `--page-soft`。 */
    surface?: 'white' | 'page-soft'
  }>(),
  {
    numeric: false,
    sticky: 'none',
    tone: 'body',
    surface: 'white',
  },
)

const wrap = inject(
  tableWrapKey,
  computed(() => false),
)

const classes = computed(() => [
  'border-b border-b-line-soft text-12/18 text-ellipsis',
  props.surface === 'page-soft' ? 'bg-page-soft' : 'bg-white',
  props.tone === 'ink' ? 'text-ink' : 'text-body',
  wrap.value
    ? 'h-auto overflow-visible py-14 align-top [overflow-wrap:anywhere] whitespace-normal [text-overflow:clip]'
    : 'h-48 overflow-hidden whitespace-nowrap',
  props.numeric ? 'text-right tabular-nums' : '',
  props.sticky === 'left'
    ? 'sticky left-0 z-[4] w-28 border-r border-r-line-soft pr-0'
    : props.sticky === 'right'
      ? 'sticky right-0 z-[4] border-l border-l-line-soft px-12'
      : 'border-r border-r-line-soft px-8',
  'last:border-r-0',
])
</script>

<template>
  <td :data-numeric="String(numeric)" :data-sticky="sticky" :class="classes">
    <slot />
  </td>
</template>
