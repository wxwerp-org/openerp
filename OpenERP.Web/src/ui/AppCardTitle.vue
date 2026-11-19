<script setup lang="ts">
withDefaults(
  defineProps<{
    as?: string
  }>(),
  {
    as: 'h2',
  },
)
</script>

<template>
  <component
    :is="as"
    class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink"
  >
    <slot />
  </component>
</template>
