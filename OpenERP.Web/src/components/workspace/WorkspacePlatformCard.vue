<script setup lang="ts">
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'
import { platformLabel } from '../../reports/orderSnapshot'
import { formatCount } from '../../workspace/workspaceData'

const props = defineProps<{
  totals: { platform: string; orderCount: number }[]
  totalOrders: number
}>()

const emit = defineEmits<{ select: [platform: string]; open: [] }>()

/** 与原型订单分布卡一致：主蓝与图表辅助色共用同一调色板。 */
const colors = [
  'var(--color-chart-1)',
  'var(--color-chart-2)',
  'var(--color-chart-3)',
  'var(--color-chart-4)',
  'var(--color-accent)',
  'var(--color-success)',
  'var(--color-warning)',
  'var(--color-danger)',
]

function share(orderCount: number): number {
  return props.totalOrders > 0 ? (orderCount / props.totalOrders) * 100 : 0
}

function percentLabel(orderCount: number): string {
  return `${Math.round(share(orderCount))}%`
}

function dashArray(orderCount: number): string {
  return `${share(orderCount)} ${100 - share(orderCount)}`
}

function dashOffset(index: number): number {
  let previous = 0
  for (let cursor = 0; cursor < index; cursor += 1)
    previous += share(props.totals[cursor]!.orderCount)
  return -previous
}

function color(index: number): string {
  return colors[index % colors.length]!
}
</script>

<template>
  <AppCard class="flex h-296 flex-col" data-testid="workspace-platform-card">
    <div class="flex h-54 flex-none items-start justify-between gap-12 px-16 pt-16">
      <button
        type="button"
        class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink hover:text-primary"
        @click="emit('open')"
      >
        平台分布<AppIcon
          name="i-chev-right"
          :size="12"
          class="text-title-caret [stroke-width:1.8]"
        />
      </button>
      <span class="inline-flex items-center gap-3 text-12 whitespace-nowrap text-muted">
        按订单数占比<AppIcon name="i-info" :size="13" class="ml-2 text-placeholder" />
      </span>
    </div>
    <div v-if="totalOrders > 0" class="flex min-h-0 flex-1 items-center gap-14 px-16 pt-2 pb-16">
      <div class="relative flex w-166 flex-none flex-col items-center justify-center">
        <svg
          data-testid="workspace-platform-chart"
          class="block size-166"
          viewBox="0 0 166 166"
          role="img"
          aria-label="各平台订单分布环形图"
          aria-describedby="workspace-platform-description"
        >
          <circle
            cx="83"
            cy="83"
            r="63"
            class="fill-none stroke-donut-track [stroke-width:17]"
          ></circle>
          <g transform="rotate(-90 83 83)">
            <circle
              v-for="(item, index) in totals"
              :key="item.platform"
              cx="83"
              cy="83"
              r="63"
              class="fill-none [stroke-width:17]"
              :style="{ stroke: color(index) }"
              pathLength="100"
              :stroke-dasharray="dashArray(item.orderCount)"
              :stroke-dashoffset="dashOffset(index)"
            ></circle>
          </g>
        </svg>
        <div
          class="pointer-events-none absolute top-[48%] left-1/2 -translate-x-1/2 -translate-y-1/2 text-center"
        >
          <strong class="block text-20/26 font-medium text-ink tabular-nums">{{
            formatCount(totalOrders)
          }}</strong>
          <span class="mt-1 block text-11/15 text-muted">订单总数</span>
        </div>
      </div>
      <ul
        id="workspace-platform-description"
        role="list"
        class="m-0 flex max-h-full min-w-0 flex-1 list-none flex-col gap-10 overflow-y-auto p-0 py-2"
      >
        <li v-for="(item, index) in totals" :key="item.platform">
          <button
            type="button"
            class="flex w-full min-w-0 flex-col gap-5 rounded-4 text-left hover:bg-page-soft"
            :data-platform="item.platform"
            @click="emit('select', item.platform)"
          >
            <span class="flex items-center justify-between gap-8 text-12 text-body">
              <span class="flex min-w-0 items-center gap-6">
                <span
                  class="size-7 flex-none rounded-2"
                  :style="{ backgroundColor: color(index) }"
                  aria-hidden="true"
                ></span>
                <span class="truncate">{{ platformLabel(item.platform) }}</span>
              </span>
              <b class="font-medium whitespace-nowrap text-ink tabular-nums">
                {{ percentLabel(item.orderCount) }} · {{ formatCount(item.orderCount) }}
              </b>
            </span>
            <span class="h-6 overflow-hidden rounded-3 bg-progress-track" aria-hidden="true">
              <span
                class="block h-full rounded-3"
                :style="{ width: `${share(item.orderCount)}%`, background: color(index) }"
              ></span>
            </span>
          </button>
        </li>
      </ul>
    </div>
    <div v-else class="flex flex-1 items-center justify-center text-12 text-muted">
      暂无订单数据
    </div>
  </AppCard>
</template>
