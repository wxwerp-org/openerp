<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import AppIcon from '../../ui/AppIcon.vue'

const props = defineProps<{
  label: string
  value: string
  detail?: string
  testid: string
  to?: RouteLocationRaw
}>()

const isDateTime = computed(() => /^\d{4}-\d{2}-\d{2}/.test(props.value))

const cardClass = computed(() => [
  'flex h-104 flex-col justify-between rounded-6 bg-white px-16 py-14',
  props.to ? 'transition-colors hover:bg-page-soft' : '',
])
</script>

<template>
  <component
    :is="to ? RouterLink : 'div'"
    :to="to"
    :class="cardClass"
    :data-testid="testid"
    :aria-label="to ? `${label}：查看报表` : undefined"
  >
    <div class="flex items-center gap-4 text-12 text-muted">
      {{ label }}
      <AppIcon v-if="to" name="i-chev-right" :size="10" class="text-caret-soft" />
    </div>
    <div>
      <div
        class="font-semibold tabular-nums text-ink"
        :class="isDateTime ? 'text-18/26' : 'text-24/32'"
        data-testid="metric-value"
      >
        {{ value }}
      </div>
      <div v-if="detail" class="mt-2 text-11 text-muted">{{ detail }}</div>
    </div>
  </component>
</template>
