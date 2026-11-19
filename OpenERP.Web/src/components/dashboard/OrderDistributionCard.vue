<script setup lang="ts">
import { ref } from 'vue'
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'
import AppSegmented from '../../ui/AppSegmented.vue'
import {
  distributionSegments,
  orderPeriods,
  orderTypes,
  type DistributionSegment,
} from './dashboardMocks'

const props = withDefaults(
  defineProps<{
    types?: string[]
    periods?: string[]
    segments?: DistributionSegment[]
    total?: number
    returnCount?: number
    returnRate?: string
    averageOrderValue?: string
  }>(),
  {
    types: () => orderTypes,
    periods: () => orderPeriods,
    segments: () => distributionSegments,
    total: 842,
    returnCount: 20,
    returnRate: '2.4%',
    averageOrderValue: '$29.52',
  },
)

const orderType = ref(props.types[0] ?? '')
const period = ref(props.periods[2] ?? props.periods[0] ?? '')
</script>

<template>
  <AppCard data-testid="dashboard-card-order-distribution" class="col-span-6 flex h-400 flex-col">
    <div class="flex h-54 flex-none items-start justify-between gap-8 px-16 pt-16">
      <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
        订单分布<AppIcon
          name="i-chev-right"
          :size="12"
          class="text-title-caret [stroke-width:1.8]"
        />
      </div>
      <div class="flex items-center gap-8">
        <AppSegmented v-model="orderType" :options="types" aria-label="订单类型" />
        <AppSegmented v-model="period" :options="periods" aria-label="订单周期" />
      </div>
    </div>
    <div class="flex min-h-0 flex-1 gap-14 px-16 pt-2 pb-16">
      <div class="relative flex w-218 flex-none flex-col items-center justify-center">
        <svg
          data-testid="order-distribution-chart"
          class="block size-166"
          viewBox="0 0 166 166"
          role="img"
          aria-label="各站点订单分布环形图"
          aria-describedby="order-distribution-description"
        >
          <circle
            cx="83"
            cy="83"
            r="63"
            class="fill-none stroke-donut-track [stroke-width:17]"
          ></circle>
          <circle
            v-for="segment in segments"
            :key="segment.name"
            cx="83"
            cy="83"
            r="63"
            class="fill-none [stroke-width:17]"
            :stroke="segment.color"
            pathLength="100"
            :stroke-dasharray="`${segment.percentage} ${100 - segment.percentage}`"
            :stroke-dashoffset="segment.offset"
            transform="rotate(-90 83 83)"
          ></circle>
        </svg>
        <div
          class="pointer-events-none absolute top-[48%] left-1/2 -translate-x-1/2 -translate-y-1/2 text-center"
        >
          <strong class="block text-20/26 font-medium text-ink tabular-nums">{{ total }}</strong>
          <span class="mt-1 block text-11/15 text-muted">总订单量</span>
        </div>
      </div>
      <div
        id="order-distribution-description"
        class="flex min-w-0 flex-1 flex-col justify-center gap-12 py-4"
      >
        <div v-for="segment in segments" :key="segment.name" class="flex flex-col gap-5">
          <div class="flex justify-between gap-8 text-12 text-body">
            <span
              ><span
                class="inline-block size-7 flex-none rounded-2 bg-primary"
                :style="{ background: segment.color }"
              ></span>
              {{ segment.name }}</span
            ><b class="font-medium text-ink tabular-nums"
              >{{ segment.percentage }}% · {{ segment.count }}</b
            >
          </div>
          <div class="h-6 overflow-hidden rounded-3 bg-progress-track">
            <span
              class="block h-full rounded-3 bg-primary"
              :style="{ width: `${segment.percentage}%`, background: segment.color }"
            ></span>
          </div>
        </div>
        <div
          class="mt-4 flex items-center justify-between border-t border-line-soft pt-10 text-11/[16.5px] text-muted"
        >
          <span
            >退货订单 <b class="text-12 font-medium text-ink">{{ returnCount }}</b> 笔</span
          ><span
            >退货率 <b class="text-12 font-medium text-ink">{{ returnRate }}</b></span
          ><span
            >平均客单价 <b class="text-12 font-medium text-ink">{{ averageOrderValue }}</b></span
          >
        </div>
      </div>
    </div>
  </AppCard>
</template>
