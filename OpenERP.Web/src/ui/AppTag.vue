<script setup lang="ts">
withDefaults(
  defineProps<{
    tone?: 'default' | 'red' | 'green' | 'blue' | 'amber' | 'gray'
  }>(),
  {
    tone: 'default',
  },
)

const toneClass: Record<'default' | 'red' | 'green' | 'blue' | 'amber' | 'gray', string> = {
  default: 'border-transparent',
  red: 'border-tag-red-border bg-danger-faint text-danger',
  green: 'border-tag-green-border bg-tag-green-bg text-success',
  blue: 'border-tag-blue-border bg-tag-blue-bg text-primary',
  amber: 'border-tag-amber-border bg-tag-amber-bg text-warning',
  gray: 'border-tag-gray-border bg-tag-gray-bg text-muted',
}
</script>

<template>
  <span
    :data-tone="tone"
    class="inline-flex h-18 items-center justify-center whitespace-nowrap rounded-2 border px-5 text-11/16"
    :class="toneClass[tone]"
  >
    <slot />
  </span>
</template>
