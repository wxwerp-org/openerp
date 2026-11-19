<script setup lang="ts">
import { computed, ref } from 'vue'
import AppCard from '../../ui/AppCard.vue'
import AppSegmented from '../../ui/AppSegmented.vue'
import type { OrderSnapshotReport } from '../../reports/orderSnapshot'
import {
  formatSnapshotQuantity,
  shopRanking,
  type SnapshotMetric,
} from '../../reports/orderSnapshotPresentation'

const props = defineProps<{
  groups: OrderSnapshotReport['groups']
  loading?: boolean
}>()

const metricOptions: Array<{ value: SnapshotMetric; label: string }> = [
  { value: 'itemQuantity', label: '商品件数' },
  { value: 'orderCount', label: '订单数' },
]

const metric = ref<SnapshotMetric>('itemQuantity')
const metricLabels = computed(() => metricOptions.map((option) => option.label))
const metricLabel = computed(
  () => metricOptions.find((option) => option.value === metric.value)?.label ?? '',
)
const selectedMetricLabel = computed({
  get: () => metricLabel.value,
  set: (label: string) => {
    const option = metricOptions.find((item) => item.label === label)
    if (option) metric.value = option.value
  },
})

const rows = computed(() => shopRanking(props.groups, metric.value))
</script>

<template>
  <AppCard data-testid="report-shop-ranking" class="@container flex flex-col pb-16">
    <div class="flex h-54 min-w-0 flex-none items-start justify-between gap-12 px-16 pt-16">
      <div class="flex min-w-0 items-center gap-8">
        <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
          店铺排行
        </div>
      </div>
      <div class="flex min-w-0 flex-nowrap items-center gap-10">
        <AppSegmented
          v-model="selectedMetricLabel"
          :options="metricLabels"
          aria-label="排行指标"
          option-class="pointer-coarse:min-h-44"
        />
      </div>
    </div>
    <p v-if="loading" class="m-0 px-16 pt-24 pb-8 text-center text-12 text-muted">
      正在加载店铺排行…
    </p>
    <figure v-else-if="rows.length" class="m-0 flex min-h-0 flex-col gap-10 px-16 pt-8">
      <figcaption class="text-12 text-muted">按{{ metricLabel }}取前 5 家店铺</figcaption>
      <ul class="flex flex-col gap-10">
        <li
          v-for="(row, index) in rows"
          :key="row.shop.key"
          class="grid grid-cols-[16px_minmax(64px,1fr)_minmax(72px,2fr)_48px] items-center gap-10 @max-[400px]:grid-cols-[16px_minmax(0,1fr)_48px]"
        >
          <span
            class="w-16 flex-none text-center text-12 font-bold"
            :class="index < 3 ? 'text-primary' : 'text-muted'"
            >{{ index + 1 }}</span
          >
          <span class="text-12 wrap-break-word text-body">{{ row.shop.label }}</span>
          <span
            class="h-6 overflow-hidden rounded-3 bg-progress-track @max-[400px]:col-span-full @max-[400px]:row-start-2"
            ><span
              class="block h-full rounded-3 bg-primary"
              :style="{ width: `${row.barPercentage * 100}%` }"
            ></span
          ></span>
          <b class="text-end text-12 font-medium text-ink tabular-nums">{{
            formatSnapshotQuantity(row[metric])
          }}</b>
        </li>
      </ul>
      <table class="sr-only">
        <caption>
          店铺{{
            metricLabel
          }}排行（前 5）
        </caption>
        <thead>
          <tr>
            <th scope="col">排名</th>
            <th scope="col">店铺</th>
            <th scope="col">{{ metricLabel }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, index) in rows" :key="row.shop.key">
            <td>{{ index + 1 }}</td>
            <td>{{ row.shop.label }}</td>
            <td>{{ formatSnapshotQuantity(row[metric]) }}</td>
          </tr>
        </tbody>
      </table>
    </figure>
    <p v-else class="m-0 px-16 pt-24 pb-8 text-center text-12 text-muted">暂无店铺排行数据</p>
  </AppCard>
</template>
