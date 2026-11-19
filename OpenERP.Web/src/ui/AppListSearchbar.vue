<script setup lang="ts">
withDefaults(
  defineProps<{
    as?: 'search' | 'form'
    label: string
  }>(),
  {
    as: 'search',
  },
)
</script>

<template>
  <component
    :is="as"
    :aria-label="label"
    class="flex h-50 flex-none items-start gap-6 overflow-hidden border-b border-line bg-white px-20 pt-10"
  >
    <slot />
  </component>
</template>
