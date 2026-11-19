<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    value: number
    label: string
    min?: number
    max?: number
    tone?: 'primary' | 'success' | 'warning' | 'danger'
  }>(),
  {
    min: 0,
    max: 100,
    tone: 'primary',
  },
)

const toneColor: Record<'primary' | 'success' | 'warning' | 'danger', string> = {
  primary: 'var(--color-primary)',
  success: 'var(--color-success)',
  warning: 'var(--color-warning)',
  danger: 'var(--color-danger)',
}

const clampedValue = computed(() => Math.min(Math.max(props.value, props.min), props.max))
const ratio = computed(() => {
  const span = props.max - props.min
  if (span <= 0) return 0
  return ((clampedValue.value - props.min) / span) * 100
})
</script>

<template>
  <div
    class="h-6 overflow-hidden rounded-3 bg-progress-track"
    role="progressbar"
    :aria-label="label"
    :aria-valuemin="min"
    :aria-valuemax="max"
    :aria-valuenow="clampedValue"
  >
    <span
      class="block h-full rounded-3"
      :style="{ width: `${ratio}%`, background: toneColor[tone] }"
    />
  </div>
</template>
