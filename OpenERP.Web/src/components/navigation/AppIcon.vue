<script setup lang="ts">
const props = withDefaults(defineProps<{
  name: string
  iconClass?: string
  size?: number
}>(), {
  iconClass: '',
  size: 0,
})
</script>

<template>
  <svg
    class="ic"
    :class="props.iconClass"
    :style="props.size ? { width: `${props.size}px`, height: `${props.size}px` } : undefined"
    aria-hidden="true"
  >
    <use :href="`#${props.name}`" />
  </svg>
</template>
