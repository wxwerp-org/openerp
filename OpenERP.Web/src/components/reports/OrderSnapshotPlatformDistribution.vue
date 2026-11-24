<script setup lang="ts">
import { computed } from 'vue'
import AppCard from '../../ui/AppCard.vue'
import type { OrderSnapshotReport } from '../../reports/orderSnapshot'
import {
  formatSnapshotQuantity,
  platformDistribution,
} from '../../reports/orderSnapshotPresentation'

const props = defineProps<{
  groups: OrderSnapshotReport['groups']
  totalOrders: number
  loading?: boolean
}>()

const rows = computed(() => platformDistribution(props.groups, props.totalOrders))

const segments = computed(() => {
  let cumulative = 0
  return rows.value.map((row) => {
    const segment = { ...row, dashOffset: -cumulative }
    cumulative += row.percentage * 100
    return segment
  })
})

function formatPercentage(percentage: number): string {
  return `${(percentage * 100).toFixed(1)}%`
}
</script>

<template>
  <AppCard data-testid="report-platform-distribution" class="@container flex flex-col pb-16">
    <div class="flex h-54 min-w-0 flex-none items-start justify-between gap-12 px-16 pt-16">
      <div class="flex min-w-0 items-center gap-8">
        <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
          平台分布
        </div>
      </div>
    </div>
    <p v-if="loading" class="m-0 px-16 pt-24 pb-8 text-center text-12 text-muted">
      正在加载平台分布…
    </p>
    <figure v-else-if="rows.length" class="m-0 flex min-h-0 flex-col gap-10 px-16 pt-8">
      <figcaption class="text-12 text-muted">按平台统计订单占比</figcaption>
      <div
        class="flex min-w-0 items-center gap-14 @max-[480px]:flex-col @max-[480px]:items-stretch"
      >
        <div
          class="relative flex w-218 flex-none flex-col items-center justify-center @max-[480px]:w-full"
        >
          <svg
            data-testid="report-platform-chart"
            class="block size-166 @max-[480px]:mx-auto"
            viewBox="0 0 166 166"
            aria-hidden="true"
          >
            <circle
              cx="83"
              cy="83"
              r="63"
              class="fill-none stroke-progress-track [stroke-width:17]"
            ></circle>
            <circle
              v-for="segment in segments"
              :key="segment.platform"
              cx="83"
              cy="83"
              r="63"
              class="fill-none [stroke-width:17]"
              :style="{ stroke: segment.color }"
              pathLength="100"
              :stroke-dasharray="`${segment.percentage * 100} ${100 - segment.percentage * 100}`"
              :stroke-dashoffset="segment.dashOffset"
              transform="rotate(-90 83 83)"
            ></circle>
          </svg>
          <div
            class="pointer-events-none absolute top-[48%] left-1/2 -translate-x-1/2 -translate-y-1/2 text-center"
          >
            <strong class="block text-20/26 font-medium text-ink tabular-nums">{{
              formatSnapshotQuantity(totalOrders)
            }}</strong><span class="mt-1 block text-11/15 text-muted">总订单数</span>
          </div>
        </div>
        <div data-testid="report-platform-legend" class="flex min-w-0 flex-1 flex-col gap-12">
          <div v-for="row in rows" :key="row.platform" class="flex flex-col gap-5">
            <div class="flex justify-between gap-8 text-12 text-body">
              <span
                ><span
                  class="inline-block size-7 flex-none rounded-2"
                  :style="{ background: row.color }"
                ></span
                >{{ row.label }}</span
              ><b class="font-medium text-ink tabular-nums"
                >{{ formatPercentage(row.percentage) }} ·
                {{ formatSnapshotQuantity(row.orderCount) }}</b
              >
            </div>
            <div class="h-6 overflow-hidden rounded-3 bg-progress-track">
              <span
                class="block h-full rounded-3 bg-primary"
                :style="{ width: `${row.percentage * 100}%`, background: row.color }"
              ></span>
            </div>
          </div>
        </div>
      </div>
      <table class="sr-only">
        <caption>
          平台订单分布
        </caption>
        <thead>
          <tr>
            <th scope="col">平台</th>
            <th scope="col">订单数</th>
            <th scope="col">占比</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in rows" :key="row.platform">
            <td>{{ row.label }}</td>
            <td>{{ formatSnapshotQuantity(row.orderCount) }}</td>
            <td>{{ formatPercentage(row.percentage) }}</td>
          </tr>
        </tbody>
      </table>
    </figure>
    <p v-else class="m-0 px-16 pt-24 pb-8 text-center text-12 text-muted">暂无平台分布数据</p>
  </AppCard>
</template>
