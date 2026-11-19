<script setup lang="ts">
withDefaults(
  defineProps<{
    as?: string
  }>(),
  {
    as: 'section',
  },
)
</script>

<template>
  <component :is="as" class="min-w-0 overflow-hidden rounded-6 bg-white">
    <slot />
  </component>
</template>
