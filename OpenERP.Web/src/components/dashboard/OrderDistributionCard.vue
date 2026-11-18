<script setup lang="ts">
import { ref } from 'vue'
import SegmentedControl from '../controls/SegmentedControl.vue'
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
  <section class="card span-6 order-dist-card">
    <div class="order-dist-head">
      <div class="card-title">
        订单分布<svg class="ic title-caret" aria-hidden="true"><use href="#i-chev-right" /></svg>
      </div>
      <div class="order-dist-head-right">
        <SegmentedControl v-model="orderType" :options="types" aria-label="订单类型" />
        <SegmentedControl v-model="period" :options="periods" aria-label="订单周期" />
      </div>
    </div>
    <div class="order-dist-body">
      <div class="donut-wrap">
        <svg
          class="donut-chart"
          viewBox="0 0 166 166"
          role="img"
          aria-label="各站点订单分布环形图"
          aria-describedby="order-distribution-description"
        >
          <circle cx="83" cy="83" r="63" fill="none" stroke="#EFF4FB" stroke-width="17"></circle>
          <circle
            v-for="segment in segments"
            :key="segment.name"
            cx="83"
            cy="83"
            r="63"
            fill="none"
            :stroke="segment.color"
            stroke-width="17"
            pathLength="100"
            :stroke-dasharray="`${segment.percentage} ${100 - segment.percentage}`"
            :stroke-dashoffset="segment.offset"
            transform="rotate(-90 83 83)"
          ></circle>
        </svg>
        <div class="donut-center">
          <strong class="tnum">{{ total }}</strong
          ><span>总订单量</span>
        </div>
      </div>
      <div id="order-distribution-description" class="dist-list">
        <div v-for="segment in segments" :key="segment.name" class="dist-row">
          <div class="dist-row-top">
            <span
              ><span class="legend-color" :style="{ background: segment.color }"></span>
              {{ segment.name }}</span
            ><b>{{ segment.percentage }}% · {{ segment.count }}</b>
          </div>
          <div class="dist-bar">
            <span :style="{ width: `${segment.percentage}%`, background: segment.color }"></span>
          </div>
        </div>
        <div class="dist-footer">
          <span
            >退货订单 <b>{{ returnCount }}</b> 笔</span
          ><span
            >退货率 <b>{{ returnRate }}</b></span
          ><span
            >平均客单价 <b>{{ averageOrderValue }}</b></span
          >
        </div>
      </div>
    </div>
  </section>
</template>
