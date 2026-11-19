<script setup lang="ts">
import { computed } from 'vue'
import AppIcon from './AppIcon.vue'

const props = withDefaults(
  defineProps<{
    tone?: 'default' | 'amber' | 'red'
    icon?: string
    title?: string
    description?: string
  }>(),
  {
    tone: 'default',
    icon: undefined,
    title: undefined,
    description: undefined,
  },
)

const toneClass: Record<'default' | 'amber' | 'red', string> = {
  default: 'border-line bg-notice-bg',
  amber: 'border-tag-amber-border bg-tag-amber-bg',
  red: 'border-tag-red-border bg-tag-red-bg',
}

const iconClass = computed(
  () =>
    ({
      default: 'text-muted',
      amber: 'text-warning',
      red: 'text-danger',
    })[props.tone],
)
</script>

<template>
  <div
    :data-tone="tone"
    class="flex min-w-0 items-start gap-8 rounded-4 border px-12 py-10 text-12/18 text-ink"
    :class="toneClass[tone]"
  >
    <AppIcon v-if="icon" :name="icon" :size="16" class="mt-1 shrink-0" :class="iconClass" />
    <div class="min-w-0">
      <p v-if="title" class="font-semibold [overflow-wrap:anywhere]">{{ title }}</p>
      <p v-if="description" class="mt-2 text-muted [overflow-wrap:anywhere]">{{ description }}</p>
      <slot />
      <slot name="actions" />
    </div>
  </div>
</template>
