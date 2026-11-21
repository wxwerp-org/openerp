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
  /** 整卡链接的无障碍文案；默认按报表下钻描述。 */
  toLabel?: string
}>()

const cardClass = computed(() => [
  'group flex min-h-104 flex-col rounded-6 bg-white',
  props.to ? 'transition-colors hover:bg-page-soft' : '',
])
</script>

<template>
  <component
    :is="to ? RouterLink : 'div'"
    :to="to"
    :class="cardClass"
    :data-testid="testid"
    :aria-label="to ? `${label}：${toLabel ?? '查看报表'}` : undefined"
  >
    <div class="flex h-54 flex-none items-start gap-4 px-16 pt-16">
      <span
        class="flex min-w-0 items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink"
        :class="to ? 'group-hover:text-primary' : ''"
      >
        {{ label }}
        <AppIcon
          v-if="to"
          name="i-chev-right"
          :size="12"
          class="flex-none text-title-caret [stroke-width:1.8]"
        />
      </span>
    </div>
    <div class="flex min-h-0 flex-1 flex-col justify-end px-16 pb-14">
      <div
        class="truncate text-20/26 font-medium tracking-[-0.2px] whitespace-nowrap text-ink tabular-nums"
        data-testid="metric-value"
      >
        {{ value }}
      </div>
      <div v-if="detail" class="mt-2 truncate text-12/16 text-muted">{{ detail }}</div>
    </div>
  </component>
</template>
