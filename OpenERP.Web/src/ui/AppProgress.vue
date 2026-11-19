<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    value: number
    label: string
    min?: number
    max?: number
    color?: string
  }>(),
  {
    min: 0,
    max: 100,
    color: undefined,
  },
)

const ratio = computed(() => {
  const span = props.max - props.min
  if (span <= 0) return 0
  return Math.min(100, Math.max(0, ((props.value - props.min) / span) * 100))
})
</script>

<template>
  <div
    class="h-6 overflow-hidden rounded-3 bg-progress-track"
    role="progressbar"
    :aria-label="label"
    :aria-valuemin="min"
    :aria-valuemax="max"
    :aria-valuenow="value"
  >
    <span
      class="block h-full rounded-3"
      :style="{ width: `${ratio}%`, background: color ?? 'var(--color-primary)' }"
    />
  </div>
</template>
