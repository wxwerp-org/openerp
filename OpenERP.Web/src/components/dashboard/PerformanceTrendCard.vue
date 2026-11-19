<script setup lang="ts">
import { ref } from 'vue'
import AppSegmented from '../../ui/AppSegmented.vue'
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
  <section class="card span-12 trend-card">
    <div class="card-head">
      <div class="card-title">
        业绩走势<svg class="ic title-caret" aria-hidden="true"><use href="#i-chev-right" /></svg>
      </div>
      <div class="card-head-right trend-periods">
        <span class="info-dot" style="margin-right: 0"
          ><svg class="ic" aria-hidden="true"><use href="#i-info" /></svg
        ></span>
        <AppSegmented
          v-model="period"
          :options="periods"
          aria-label="业绩周期"
          :padding-x="7"
          :min-width="36"
          option-class="max-[1500px]:px-6 max-[1500px]:min-w-32"
        />
        <button class="date-range-btn" type="button">
          <span>2026-11-11 - 2026-11-11</span
          ><svg class="ic" aria-hidden="true"><use href="#i-calendar" /></svg>
        </button>
        <button class="trend-icon-btn" type="button" title="放大" aria-label="放大">
          <svg class="ic" aria-hidden="true"><use href="#i-expand" /></svg>
        </button>
        <button class="trend-icon-btn" type="button" title="设置" aria-label="设置">
          <svg class="ic" aria-hidden="true"><use href="#i-settings" /></svg>
        </button>
      </div>
    </div>
    <div id="performance-trend-description" class="trend-stats">
      <div
        v-for="(stat, index) in stats"
        :key="stat.label"
        class="trend-stat"
        :class="{ selected: index === 0 }"
      >
        <div class="stat-label">
          {{ stat.label
          }}<svg v-if="index > 0" class="ic info-dot" aria-hidden="true">
            <use href="#i-info" />
          </svg>
        </div>
        <div class="stat-value tnum">{{ stat.value }}</div>
        <div class="stat-compare">
          <span v-for="comparison in stat.comparisons" :key="comparison.label">
            {{ comparison.label }}
            <b :class="comparison.className" :style="{ color: comparison.color }">{{
              comparison.value
            }}</b>
          </span>
        </div>
      </div>
    </div>
    <div class="trend-chart-wrap">
      <div class="chart-legend">
        <span class="legend-item"><span class="legend-line"></span>销售额</span>
        <span class="legend-item"><span class="legend-line alt"></span>订单量</span>
      </div>
      <svg
        class="trend-chart"
        viewBox="0 0 1542 224"
        preserveAspectRatio="none"
        role="img"
        aria-label="销售额与订单量业绩走势图"
        aria-describedby="performance-trend-description"
      >
        <g class="chart-grid-line">
          <line x1="54" y1="20" x2="1518" y2="20"></line>
          <line x1="54" y1="68" x2="1518" y2="68"></line>
          <line x1="54" y1="116" x2="1518" y2="116"></line>
          <line x1="54" y1="164" x2="1518" y2="164"></line>
          <line x1="54" y1="205" x2="1518" y2="205"></line>
        </g>
        <g class="chart-axis-text">
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
          d="M54 186 C110 178 144 160 198 163 S280 142 335 146 S414 161 470 149 S552 121 610 128 S690 146 744 128 S826 100 884 110 S966 134 1020 111 S1100 83 1160 92 S1246 108 1300 78 S1400 58 1454 52 S1500 42 1518 36 L1518 205 L54 205 Z"
          fill="#F0F6FF"
          stroke="none"
        ></path>
        <path
          class="chart-sales-line"
          d="M54 186 C110 178 144 160 198 163 S280 142 335 146 S414 161 470 149 S552 121 610 128 S690 146 744 128 S826 100 884 110 S966 134 1020 111 S1100 83 1160 92 S1246 108 1300 78 S1400 58 1454 52 S1500 42 1518 36"
        ></path>
        <path
          class="chart-orders-line"
          d="M54 196 C120 191 162 177 215 182 S310 164 366 170 S448 181 507 165 S596 141 653 151 S736 164 794 153 S878 125 934 138 S1018 156 1070 140 S1148 116 1204 124 S1296 137 1355 116 S1454 96 1518 90"
        ></path>
        <line class="chart-active-line" x1="1020" y1="24" x2="1020" y2="205"></line>
        <circle class="chart-point" cx="1020" cy="111" r="3.5"></circle>
        <g>
          <rect class="chart-tip-box" x="1018" y="44" width="156" height="54" rx="4"></rect>
          <text class="chart-tip-title" x="1030" y="62">11-12 16:00</text>
          <text class="chart-tip-value" x="1030" y="82">$24,860.00</text>
          <text class="chart-tip-title" x="1030" y="95" fill="#888C94">较昨日同时段 +18.2%</text>
        </g>
      </svg>
    </div>
  </section>
</template>
