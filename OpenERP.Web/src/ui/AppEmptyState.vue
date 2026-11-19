<script setup lang="ts">
import { computed } from 'vue'
import AppIcon from './AppIcon.vue'

const props = withDefaults(
  defineProps<{
    variant?: 'overlay' | 'block'
    title?: string
    description?: string
    icon?: string
  }>(),
  {
    variant: 'overlay',
    title: undefined,
    description: undefined,
    icon: undefined,
  },
)

const classes = computed(() => [
  'text-12/18 text-muted',
  props.variant === 'overlay'
    ? 'pointer-events-none absolute inset-x-0 top-40 bottom-8 z-[2] flex items-center justify-center gap-6 bg-transparent'
    : 'flex flex-col items-center justify-center gap-8 p-36 text-center',
])

const iconSize = computed(() => (props.variant === 'overlay' ? 24 : 36))
const iconClass = computed(() =>
  props.variant === 'overlay' ? 'text-empty-icon' : 'text-empty-icon-strong',
)
</script>

<template>
  <div :data-variant="variant" :class="classes">
    <slot>
      <AppIcon v-if="icon" :name="icon" :size="iconSize" :class="iconClass" />
      <strong v-if="title" class="text-13/20 font-medium text-body">{{ title }}</strong>
      <span v-if="description">{{ description }}</span>
    </slot>
  </div>
</template>
