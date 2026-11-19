<script setup lang="ts">
withDefaults(
  defineProps<{
    label: string
    type?: 'button' | 'submit' | 'reset'
    disabled?: boolean
  }>(),
  {
    type: 'button',
    disabled: false,
  },
)
</script>

<template>
  <button
    :type="type"
    :disabled="disabled"
    :aria-label="label"
    :title="label"
    class="inline-flex h-[var(--control-h)] w-[var(--control-h)] shrink-0 items-center justify-center rounded-4 bg-transparent text-body hover:bg-icon-hover hover:text-ink disabled:cursor-default disabled:text-btn-disabled-text disabled:hover:bg-transparent disabled:hover:text-btn-disabled-text [&_svg]:size-16"
  >
    <slot />
  </button>
</template>
