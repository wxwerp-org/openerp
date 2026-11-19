<script setup lang="ts">
import { computed } from 'vue'
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
  <section class="card snapshot-dist-card">
    <div class="card-head">
      <div class="card-head-left">
        <div class="card-title">平台分布</div>
      </div>
    </div>
    <p v-if="loading" class="snapshot-loading">正在加载平台分布…</p>
    <figure v-else-if="rows.length" class="snapshot-dist-figure">
      <figcaption class="snapshot-dist-caption">按平台统计订单占比</figcaption>
      <div class="snapshot-dist-body">
        <div class="donut-wrap">
          <svg class="snapshot-donut-chart" viewBox="0 0 166 166" aria-hidden="true">
            <circle cx="83" cy="83" r="63" fill="none" stroke="#EFF4FB" stroke-width="17"></circle>
            <circle
              v-for="segment in segments"
              :key="segment.platform"
              cx="83"
              cy="83"
              r="63"
              fill="none"
              :stroke="segment.color"
              stroke-width="17"
              pathLength="100"
              :stroke-dasharray="`${segment.percentage * 100} ${100 - segment.percentage * 100}`"
              :stroke-dashoffset="segment.dashOffset"
              transform="rotate(-90 83 83)"
            ></circle>
          </svg>
          <div class="snapshot-donut-center">
            <strong class="tnum">{{ formatSnapshotQuantity(totalOrders) }}</strong
            ><span>总订单数</span>
          </div>
        </div>
        <div class="snapshot-dist-list">
          <div v-for="row in rows" :key="row.platform" class="snapshot-dist-row">
            <div class="snapshot-dist-row-top">
              <span
                ><span class="legend-color" :style="{ background: row.color }"></span
                >{{ row.label }}</span
              ><b
                >{{ formatPercentage(row.percentage) }} ·
                {{ formatSnapshotQuantity(row.orderCount) }}</b
              >
            </div>
            <div class="dist-bar">
              <span :style="{ width: `${row.percentage * 100}%`, background: row.color }"></span>
            </div>
          </div>
        </div>
      </div>
      <table class="visually-hidden">
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
    <p v-else class="snapshot-empty">暂无平台分布数据</p>
  </section>
</template>

<style scoped>
.snapshot-dist-card {
  container-type: inline-size;
  display: flex;
  flex-direction: column;
  padding-bottom: 16px;
}
.snapshot-dist-figure {
  margin: 0;
  padding: 8px 16px 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.snapshot-dist-caption {
  font-size: 12px;
  color: var(--muted);
}
.snapshot-dist-body {
  display: flex;
  align-items: center;
  gap: 14px;
  min-width: 0;
}
.snapshot-dist-card .donut-wrap {
  width: 100%;
}
.snapshot-donut-chart {
  width: 166px;
  height: 166px;
  display: block;
  flex: none;
}
.snapshot-donut-center {
  position: absolute;
  left: 50%;
  top: 48%;
  transform: translate(-50%, -50%);
  text-align: center;
  pointer-events: none;
}
.snapshot-donut-center strong {
  display: block;
  font-size: 20px;
  line-height: 26px;
  color: var(--text);
  font-weight: 500;
}
.snapshot-donut-center span {
  display: block;
  font-size: 11px;
  line-height: 15px;
  color: var(--muted);
  margin-top: 1px;
}
.snapshot-dist-list {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.snapshot-dist-row {
  display: flex;
  flex-direction: column;
  gap: 5px;
}
.snapshot-dist-row-top {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  color: var(--body);
  font-size: 12px;
}
.snapshot-dist-row-top b {
  font-weight: 500;
  color: var(--text);
  font-variant-numeric: tabular-nums;
}
.snapshot-empty,
.snapshot-loading {
  margin: 0;
  padding: 24px 16px 8px;
  text-align: center;
  font-size: 12px;
  color: var(--muted);
}
@container (inline-size < 480px) {
  .snapshot-dist-body {
    flex-direction: column;
    align-items: stretch;
  }
  .snapshot-donut-chart {
    margin: 0 auto;
  }
}
</style>
