<script setup lang="ts">
import { ref } from 'vue'
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'
import AppSegmented from '../../ui/AppSegmented.vue'
import { profitPeriods, profitSummary, profitTypes, type ProfitSummary } from './dashboardMocks'

const props = withDefaults(
  defineProps<{
    types?: string[]
    periods?: string[]
    summary?: ProfitSummary
  }>(),
  {
    types: () => profitTypes,
    periods: () => profitPeriods,
    summary: () => profitSummary,
  },
)

const profitType = ref(props.types[0] ?? '')
const period = ref(props.periods[1] ?? props.periods[0] ?? '')
</script>

<template>
  <AppCard data-testid="dashboard-card-profit" class="col-span-6 flex h-400 flex-col">
    <div class="flex h-54 flex-none items-start justify-between px-16 pt-16">
      <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
        结算利润<AppIcon
          name="i-chev-right"
          :size="12"
          class="text-title-caret [stroke-width:1.8]"
        />
      </div>
      <div class="flex flex-nowrap items-center gap-8">
        <AppSegmented v-model="profitType" :options="types" aria-label="利润类型" />
        <AppSegmented v-model="period" :options="periods" aria-label="利润周期" />
      </div>
    </div>
    <div class="flex min-h-0 flex-1 flex-row gap-18 px-16 pt-2 pb-16">
      <div id="profit-chart-description" class="flex w-190 flex-none flex-col justify-between">
        <div>
          <div class="text-12 text-muted">{{ summary.label }}</div>
          <div class="mt-2 text-20/28 font-medium text-ink tabular-nums">
            {{ summary.total
            }}<small class="ml-3 text-12/28 font-normal text-muted">{{ summary.currency }}</small>
          </div>
          <div class="mt-8 flex gap-12">
            <div class="flex flex-col gap-2">
              <span class="text-11/[16.5px] text-muted">结算毛利率</span
              ><b class="text-12 font-medium text-success">{{ summary.margin }}</b>
            </div>
            <div class="flex flex-col gap-2">
              <span class="text-11/[16.5px] text-muted">订单利润</span
              ><b class="text-12 font-medium text-danger">{{ summary.orderProfit }}</b>
            </div>
          </div>
        </div>
        <div class="mt-16 flex items-center gap-12">
          <span class="text-muted">利润趋势</span
          ><span class="text-11/[16.5px] text-success">{{ summary.change }}</span>
        </div>
      </div>
      <div class="flex min-w-0 flex-1 flex-col justify-end border-l border-line-soft pl-16">
        <div class="flex h-18 items-center justify-end gap-14 text-11/[16.5px] text-muted">
          <span><i class="inline-block size-7 flex-none rounded-2 bg-primary"></i>结算利润</span
          ><span
            ><i class="inline-block size-7 flex-none rounded-2 bg-chart-order-bar"></i
            >订单利润</span
          >
        </div>
        <svg
          data-testid="profit-chart"
          class="block h-170 w-full"
          viewBox="0 0 360 170"
          preserveAspectRatio="none"
          role="img"
          aria-label="结算利润与订单利润柱状趋势图"
          aria-describedby="profit-chart-description"
        >
          <g class="[stroke-dasharray:3_3] stroke-chart-grid [stroke-width:1]">
            <line x1="18" y1="24" x2="352" y2="24" />
            <line x1="18" y1="67" x2="352" y2="67" />
            <line x1="18" y1="110" x2="352" y2="110" />
            <line x1="18" y1="153" x2="352" y2="153" />
          </g>
          <g class="fill-chart-axis font-sans" font-size="9">
            <text x="4" y="27">$2k</text>
            <text x="4" y="70">$1.5k</text>
            <text x="4" y="113">$1k</text>
            <text x="4" y="156">$0</text>
          </g>
          <g class="fill-chart-line">
            <rect x="38" y="96" width="20" height="57" rx="2"></rect>
            <rect x="78" y="78" width="20" height="75" rx="2"></rect>
            <rect x="118" y="88" width="20" height="65" rx="2"></rect>
            <rect x="158" y="62" width="20" height="91" rx="2"></rect>
            <rect x="198" y="72" width="20" height="81" rx="2"></rect>
            <rect x="238" y="48" width="20" height="105" rx="2"></rect>
            <rect x="278" y="58" width="20" height="95" rx="2"></rect>
          </g>
          <g class="fill-chart-order-bar">
            <rect x="60" y="110" width="10" height="43" rx="2"></rect>
            <rect x="100" y="98" width="10" height="55" rx="2"></rect>
            <rect x="140" y="108" width="10" height="45" rx="2"></rect>
            <rect x="180" y="86" width="10" height="67" rx="2"></rect>
            <rect x="220" y="98" width="10" height="55" rx="2"></rect>
            <rect x="260" y="74" width="10" height="79" rx="2"></rect>
            <rect x="300" y="86" width="10" height="67" rx="2"></rect>
          </g>
          <g class="fill-muted font-sans" font-size="9" text-anchor="middle">
            <text x="53" y="168">11-06</text>
            <text x="93" y="168">11-07</text>
            <text x="133" y="168">11-08</text>
            <text x="173" y="168">11-09</text>
            <text x="213" y="168">11-10</text>
            <text x="253" y="168">11-11</text>
            <text x="293" y="168">11-12</text>
          </g>
          <polyline
            points="48,81 88,67 128,73 168,54 208,61 248,40 288,47"
            class="fill-none stroke-chart-line [stroke-width:1.5]"
          ></polyline>
        </svg>
        <div class="mt-6 flex justify-between text-11/[16.5px] text-muted">
          <span>{{ summary.note }}</span
          ><span>币种：{{ summary.currency }}</span>
        </div>
      </div>
    </div>
  </AppCard>
</template>
