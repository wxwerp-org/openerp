<script setup lang="ts">
import { ref } from 'vue'
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'
import AppSegmented from '../../ui/AppSegmented.vue'

const period = ref('今日')
const metrics = [
  { label: '销量', value: '1,286', previousLabel: '昨日全天', previous: '1,142', change: '+12.6%' },
  { label: '订单量', value: '842', previousLabel: '昨日全天', previous: '769', change: '+9.5%' },
  {
    label: '销售额',
    value: '$24,860.00',
    previousLabel: '昨日全天',
    previous: '$21,580.00',
    change: '+15.2%',
  },
  {
    label: '净销售额',
    value: '$18,420.00',
    previousLabel: '昨日全天',
    previous: '$16,510.00',
    change: '+11.6%',
  },
]
</script>

<template>
  <AppCard data-testid="dashboard-card-realtime-sales" class="col-span-6 flex h-139 flex-col">
    <div class="flex flex-none items-start justify-between gap-12 px-16 pt-16">
      <div class="flex min-w-0 items-center gap-8">
        <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
          实时销量<AppIcon
            name="i-chev-right"
            :size="12"
            class="text-title-caret [stroke-width:1.8]"
          />
        </div>
      </div>
      <div class="flex items-center gap-8 whitespace-nowrap">
        <span class="inline-flex items-center gap-3 text-12 text-muted whitespace-nowrap">
          今日：各站点的当天
          <AppIcon name="i-info" :size="13" class="ml-2 text-placeholder" />
        </span>
        <AppSegmented
          v-model="period"
          :options="['今日', '昨日', '近24小时']"
          aria-label="实时销量周期"
          size="md"
          :line-height="26"
          color="body"
          :padding-x="8"
          :min-width="44"
        />
      </div>
    </div>
    <div class="grid min-h-0 grid-cols-4 gap-x-16 px-16 pt-16">
      <div
        v-for="(metric, index) in metrics"
        :key="metric.label"
        class="min-w-0 pr-12"
        :class="index === 0 ? '' : 'border-l border-toolbar pl-16'"
      >
        <div class="truncate text-12/16 text-body">{{ metric.label }}</div>
        <div
          class="mt-5 text-20/26 font-medium tracking-[-0.2px] whitespace-nowrap text-ink tabular-nums"
        >
          {{ metric.value }}
        </div>
        <div class="mt-3 text-12/16 whitespace-nowrap text-placeholder">
          {{ metric.previousLabel }} <span class="text-muted">{{ metric.previous }}</span>
          <span class="text-success">{{ metric.change }}</span>
        </div>
      </div>
    </div>
  </AppCard>
</template>
