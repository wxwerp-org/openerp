<script setup lang="ts">
withDefaults(
  defineProps<{
    as?: string
  }>(),
  {
    as: 'div',
  },
)
</script>

<template>
  <component
    :is="as"
    class="flex h-46 flex-none items-start gap-6 overflow-hidden bg-toolbar px-12 pt-8"
  >
    <div class="flex min-w-0 flex-1 items-center gap-6 overflow-hidden">
      <slot />
    </div>
    <div v-if="$slots.right" class="ml-auto flex shrink-0 items-center gap-6">
      <slot name="right" />
    </div>
  </component>
</template>
