<script setup lang="ts">
import { computed, ref } from 'vue'
import SegmentedControl from '../controls/SegmentedControl.vue'
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
  <section class="card snapshot-rank-card">
    <div class="card-head">
      <div class="card-head-left">
        <div class="card-title">店铺排行</div>
      </div>
      <div class="card-head-right">
        <SegmentedControl
          v-model="selectedMetricLabel"
          :options="metricLabels"
          aria-label="排行指标"
        />
      </div>
    </div>
    <p v-if="loading" class="snapshot-loading">正在加载店铺排行…</p>
    <figure v-else-if="rows.length" class="snapshot-rank-figure">
      <figcaption class="snapshot-rank-caption">按{{ metricLabel }}取前 5 家店铺</figcaption>
      <ul class="snapshot-rank-list">
        <li v-for="(row, index) in rows" :key="row.shop.key" class="snapshot-rank-row">
          <span class="rank-number" :class="{ top: index < 3 }">{{ index + 1 }}</span>
          <span class="snapshot-rank-shop">{{ row.shop.label }}</span>
          <span class="snapshot-rank-bar"
            ><span :style="{ width: `${row.barPercentage * 100}%` }"></span
          ></span>
          <b class="snapshot-rank-value">{{ formatSnapshotQuantity(row[metric]) }}</b>
        </li>
      </ul>
      <table class="visually-hidden">
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
    <p v-else class="snapshot-empty">暂无店铺排行数据</p>
  </section>
</template>

<style scoped>
.snapshot-rank-card {
  container-type: inline-size;
  display: flex;
  flex-direction: column;
  padding-bottom: 16px;
}
.snapshot-rank-figure {
  margin: 0;
  padding: 8px 16px 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.snapshot-rank-caption {
  font-size: 12px;
  color: var(--muted);
}
.snapshot-rank-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.snapshot-rank-row {
  display: grid;
  grid-template-columns: 16px minmax(64px, 1fr) minmax(72px, 2fr) 48px;
  align-items: center;
  gap: 10px;
}
.snapshot-rank-shop {
  font-size: 12px;
  color: var(--body);
  overflow-wrap: break-word;
}
.snapshot-rank-bar {
  height: 6px;
  border-radius: 3px;
  background: #eef3fa;
  overflow: hidden;
}
.snapshot-rank-bar > span {
  display: block;
  height: 100%;
  border-radius: 3px;
  background: var(--primary);
}
.snapshot-rank-value {
  font-weight: 500;
  font-size: 12px;
  color: var(--text);
  font-variant-numeric: tabular-nums;
  text-align: end;
}
.snapshot-empty,
.snapshot-loading {
  margin: 0;
  padding: 24px 16px 8px;
  text-align: center;
  font-size: 12px;
  color: var(--muted);
}
@container (inline-size < 400px) {
  .snapshot-rank-row {
    grid-template-columns: 16px minmax(0, 1fr) 48px;
  }
  .snapshot-rank-bar {
    grid-column: 1 / -1;
    grid-row: 2;
  }
}
@media (pointer: coarse) {
  .snapshot-rank-card :deep(.mini-seg button) {
    min-height: 44px;
  }
}
</style>
