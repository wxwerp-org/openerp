<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    label: string
    type?: 'button' | 'submit' | 'reset'
    disabled?: boolean
    /** 列表工具栏内沿用旧 `#4d5968`，其余位置用正文色。 */
    tone?: 'body' | 'toolbar'
  }>(),
  {
    type: 'button',
    disabled: false,
    tone: 'body',
  },
)

const toneClass = computed(() => (props.tone === 'toolbar' ? 'text-toolbar-icon' : 'text-body'))
</script>

<template>
  <button
    :type="type"
    :disabled="disabled"
    :aria-label="label"
    :title="label"
    class="inline-flex h-[var(--control-h)] w-[var(--control-h)] shrink-0 items-center justify-center rounded-4 bg-transparent hover:bg-icon-hover hover:text-ink disabled:cursor-default disabled:text-btn-disabled-text disabled:hover:bg-transparent disabled:hover:text-btn-disabled-text [&_svg]:size-16"
    :class="toneClass"
  >
    <slot />
  </button>
</template>
