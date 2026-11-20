<script setup lang="ts">
import AppCard from '../../ui/AppCard.vue'
import { platformLabel } from '../../reports/orderSnapshot'
import { formatCount } from '../../workspace/workspaceData'

const props = defineProps<{
  totals: { platform: string; orderCount: number }[]
  totalOrders: number
}>()

const emit = defineEmits<{ select: [platform: string] }>()

const colors = [
  'var(--color-primary)',
  'var(--color-accent)',
  'var(--color-success)',
  'var(--color-warning)',
  'var(--color-danger)',
  'var(--color-status-unknown)',
]

const radius = 54
const circumference = 2 * Math.PI * radius

function ratio(orderCount: number): number {
  return props.totalOrders > 0 ? orderCount / props.totalOrders : 0
}

function dash(ratioValue: number): string {
  return `${ratioValue * circumference} ${circumference}`
}

function offset(index: number): number {
  let previous = 0
  for (let cursor = 0; cursor < index; cursor += 1)
    previous += ratio(props.totals[cursor]!.orderCount)
  return -previous * circumference
}

function percentLabel(orderCount: number): string {
  return `${Math.round(ratio(orderCount) * 100)}%`
}

function color(index: number): string {
  return colors[index % colors.length]!
}
</script>

<template>
  <AppCard class="flex h-296 flex-col px-16 pb-16 pt-14" data-testid="workspace-platform-card">
    <div class="flex flex-none items-center justify-between">
      <h2 class="text-13/20 font-semibold text-ink">平台分布</h2>
      <span class="text-11 text-muted">按订单数占比</span>
    </div>
    <div v-if="totalOrders > 0" class="flex min-h-0 flex-1 items-center gap-16">
      <svg viewBox="0 0 140 140" class="size-140 flex-none" role="img" aria-label="平台订单占比">
        <circle
          cx="70"
          cy="70"
          :r="radius"
          fill="none"
          stroke="var(--color-page)"
          :stroke-width="16"
        />
        <g transform="rotate(-90 70 70)">
          <circle
            v-for="(item, index) in totals"
            :key="item.platform"
            cx="70"
            cy="70"
            :r="radius"
            fill="none"
            :stroke="color(index)"
            :stroke-width="16"
            :stroke-dasharray="dash(ratio(item.orderCount))"
            :stroke-dashoffset="offset(index)"
          />
        </g>
        <text
          x="70"
          y="66"
          text-anchor="middle"
          class="fill-ink text-16 font-semibold"
          style="font-size: 16px"
        >
          {{ formatCount(totalOrders) }}
        </text>
        <text x="70" y="84" text-anchor="middle" style="font-size: 11px" class="fill-muted">
          订单总数
        </text>
      </svg>
      <ul class="m-0 flex min-w-0 flex-1 list-none flex-col gap-6 p-0">
        <li v-for="(item, index) in totals" :key="item.platform">
          <button
            type="button"
            class="flex w-full items-center gap-6 rounded-4 px-4 py-2 text-left hover:bg-primary-faint"
            :data-platform="item.platform"
            @click="emit('select', item.platform)"
          >
            <span
              class="size-8 flex-none rounded-2"
              :style="{ backgroundColor: color(index) }"
              aria-hidden="true"
            ></span>
            <span class="min-w-0 flex-1 truncate text-12 text-body">{{
              platformLabel(item.platform)
            }}</span>
            <span class="text-12 tabular-nums text-body-strong">{{
              formatCount(item.orderCount)
            }}</span>
            <span class="w-32 text-right text-11 tabular-nums text-muted">{{
              percentLabel(item.orderCount)
            }}</span>
          </button>
        </li>
      </ul>
    </div>
    <div v-else class="flex flex-1 items-center justify-center text-12 text-muted">
      暂无订单数据
    </div>
  </AppCard>
</template>
