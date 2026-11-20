<script setup lang="ts">
import { ref } from 'vue'
import AppCard from '../../../ui/AppCard.vue'
import AppIcon from '../../../ui/AppIcon.vue'
import AppSegmented from '../../../ui/AppSegmented.vue'
import { performancePeriods, performanceStats, type PerformanceStat } from './dashboardMocks'

const props = withDefaults(
  defineProps<{
    periods?: string[]
    stats?: PerformanceStat[]
  }>(),
  {
    periods: () => performancePeriods,
    stats: () => performanceStats,
  },
)

const period = ref(props.periods[0] ?? '')
</script>

<template>
  <AppCard data-testid="dashboard-card-trend" class="col-span-12 flex h-460 flex-col">
    <div class="flex h-54 flex-none min-w-0 items-center justify-between gap-12 px-16 pt-16">
      <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
        业绩走势<AppIcon
          name="i-chev-right"
          :size="12"
          class="text-title-caret [stroke-width:1.8]"
        />
      </div>
      <div class="flex min-w-0 flex-nowrap items-center gap-8 whitespace-nowrap">
        <AppIcon
          name="i-info"
          :size="13"
          class="ml-2 flex-none text-placeholder [stroke-width:1.5]"
        />
        <AppSegmented
          v-model="period"
          :options="periods"
          aria-label="业绩周期"
          :padding-x="7"
          :min-width="36"
          option-class="[@media(max-width:1500px)]:px-6 [@media(max-width:1500px)]:min-w-32"
        />
        <button
          type="button"
          class="inline-flex h-28 items-center justify-center gap-6 rounded-4 border border-input bg-white px-8 text-12 whitespace-nowrap text-ink hover:border-input-hover"
        >
          <span>2026-11-11 - 2026-11-11</span
          ><AppIcon name="i-calendar" :size="14" class="text-date-icon" />
        </button>
        <button
          type="button"
          title="放大"
          aria-label="放大"
          class="inline-flex size-28 items-center justify-center rounded-4 text-muted hover:bg-page hover:text-primary"
        >
          <AppIcon name="i-expand" />
        </button>
        <button
          type="button"
          title="设置"
          aria-label="设置"
          class="inline-flex size-28 items-center justify-center rounded-4 text-muted hover:bg-page hover:text-primary"
        >
          <AppIcon name="i-settings" />
        </button>
      </div>
    </div>
    <div
      id="performance-trend-description"
      class="mx-16 mt-16 grid h-104 flex-none grid-cols-8 gap-12"
    >
      <div
        v-for="(stat, index) in stats"
        :key="stat.label"
        class="flex min-w-0 cursor-pointer flex-col justify-between rounded-4 py-10 pr-16"
        :class="
          index === 0
            ? 'border-l-4 border-accent bg-white pl-12 shadow-card-hover-lg'
            : 'bg-page-soft pl-16 hover:bg-white hover:shadow-card-hover'
        "
      >
        <div class="flex items-center gap-2 overflow-hidden text-12/16 whitespace-nowrap text-body">
          {{ stat.label
          }}<AppIcon
            v-if="index > 0"
            name="i-info"
            :size="13"
            class="ml-2 flex-none text-placeholder"
          />
        </div>
        <div class="mt-4 text-20/26 font-medium whitespace-nowrap text-ink tabular-nums">
          {{ stat.value }}
        </div>
        <div class="mt-2 flex flex-col gap-1 text-12/15 text-muted">
          <span
            v-for="comparison in stat.comparisons"
            :key="comparison.label"
            class="flex justify-between gap-8"
          >
            {{ comparison.label }}
            <b class="font-normal text-body" :style="{ color: comparison.color }">{{
              comparison.value
            }}</b>
          </span>
        </div>
      </div>
    </div>
    <div class="relative mx-16 mb-16 mt-10 min-h-0 flex-1 border-t border-trend-divider pt-8">
      <div class="flex h-20 items-center justify-end gap-16 text-12 text-muted">
        <span class="flex items-center gap-5 whitespace-nowrap"
          ><span class="h-2 w-14 rounded-1 bg-primary"></span>销售额</span
        >
        <span class="flex items-center gap-5 whitespace-nowrap"
          ><span class="h-2 w-14 rounded-1 bg-legend-alt"></span>订单量</span
        >
      </div>
      <svg
        data-testid="trend-chart"
        class="block h-[calc(100%-20px)] w-full min-h-0"
        viewBox="0 0 1542 224"
        preserveAspectRatio="none"
        role="img"
        aria-label="销售额与订单量业绩走势图"
        aria-describedby="performance-trend-description"
      >
        <g class="[stroke-dasharray:3_3] stroke-chart-grid [stroke-width:1]">
          <line x1="54" y1="20" x2="1518" y2="20"></line>
          <line x1="54" y1="68" x2="1518" y2="68"></line>
          <line x1="54" y1="116" x2="1518" y2="116"></line>
          <line x1="54" y1="164" x2="1518" y2="164"></line>
          <line x1="54" y1="205" x2="1518" y2="205"></line>
        </g>
        <g class="fill-chart-axis font-sans text-11">
          <text x="8" y="24">$40k</text>
          <text x="8" y="72">$30k</text>
          <text x="8" y="120">$20k</text>
          <text x="8" y="168">$10k</text>
          <text x="8" y="209">$0</text>
          <text x="54" y="222">00:00</text>
          <text x="280" y="222">04:00</text>
          <text x="510" y="222">08:00</text>
          <text x="740" y="222">12:00</text>
          <text x="970" y="222">16:00</text>
          <text x="1200" y="222">20:00</text>
          <text x="1490" y="222">24:00</text>
        </g>
        <path
          class="fill-chart-fill"
          d="M54 186 C110 178 144 160 198 163 S280 142 335 146 S414 161 470 149 S552 121 610 128 S690 146 744 128 S826 100 884 110 S966 134 1020 111 S1100 83 1160 92 S1246 108 1300 78 S1400 58 1454 52 S1500 42 1518 36 L1518 205 L54 205 Z"
          stroke="none"
        ></path>
        <path
          class="fill-none stroke-chart-line [stroke-width:2] [stroke-linecap:round] [stroke-linejoin:round]"
          d="M54 186 C110 178 144 160 198 163 S280 142 335 146 S414 161 470 149 S552 121 610 128 S690 146 744 128 S826 100 884 110 S966 134 1020 111 S1100 83 1160 92 S1246 108 1300 78 S1400 58 1454 52 S1500 42 1518 36"
        ></path>
        <path
          class="fill-none stroke-chart-line-alt [stroke-width:1.6] [stroke-dasharray:4_4] [stroke-linecap:round] [stroke-linejoin:round]"
          d="M54 196 C120 191 162 177 215 182 S310 164 366 170 S448 181 507 165 S596 141 653 151 S736 164 794 153 S878 125 934 138 S1018 156 1070 140 S1148 116 1204 124 S1296 137 1355 116 S1454 96 1518 90"
        ></path>
        <line
          class="[stroke-dasharray:3_3] stroke-chart-point-border [stroke-width:1]"
          x1="1020"
          y1="24"
          x2="1020"
          y2="205"
        ></line>
        <circle
          class="fill-white stroke-chart-line [stroke-width:2]"
          cx="1020"
          cy="111"
          r="3.5"
        ></circle>
        <g>
          <rect
            class="fill-white stroke-chart-tooltip-border drop-shadow-chart-tooltip [stroke-width:1]"
            x="1018"
            y="44"
            width="156"
            height="54"
            rx="4"
          ></rect>
          <text class="fill-chart-tooltip-title font-sans text-11" x="1030" y="62">
            11-12 16:00
          </text>
          <text class="fill-chart-tooltip-value font-sans text-12 font-semibold" x="1030" y="82">
            $24,860.00
          </text>
          <text class="fill-chart-tooltip-title font-sans text-11" x="1030" y="95">
            较昨日同时段 +18.2%
          </text>
        </g>
      </svg>
    </div>
  </AppCard>
</template>
