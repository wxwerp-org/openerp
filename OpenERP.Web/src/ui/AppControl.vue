<script setup lang="ts">
import { computed } from 'vue'
import AppIcon from './AppIcon.vue'

const props = withDefaults(
  defineProps<{
    as?: 'button' | 'summary'
    variant?: 'default' | 'date-range'
    muted?: boolean
    caret?: boolean
    icon?: string
  }>(),
  {
    as: 'button',
    variant: 'default',
    muted: false,
    caret: true,
    icon: undefined,
  },
)

const isDateRange = computed(() => props.variant === 'date-range')
const mutedState = computed(() => props.muted || isDateRange.value)
const iconName = computed(
  () => props.icon ?? (isDateRange.value ? 'i-calendar' : props.caret ? 'i-chev-down' : ''),
)
const iconSize = computed(() => (isDateRange.value ? 14 : 10))
const iconClass = computed(() => (isDateRange.value ? 'text-placeholder' : 'text-caret'))
const classes = computed(() => [
  'inline-flex h-[var(--control-h)] shrink-0 items-center rounded-4 border border-input bg-white px-10 text-12/28 whitespace-nowrap hover:border-input-hover',
  isDateRange.value ? 'gap-8' : 'gap-6',
  mutedState.value ? 'text-placeholder' : 'text-ink',
])
</script>

<template>
  <component
    :is="as"
    :type="as === 'button' ? 'button' : undefined"
    :data-muted="String(mutedState)"
    :class="classes"
  >
    <slot />
    <AppIcon v-if="iconName" :name="iconName" :size="iconSize" :class="iconClass" />
  </component>
</template>
