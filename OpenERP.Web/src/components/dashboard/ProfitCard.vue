<script setup lang="ts">
import { ref } from 'vue'
import SegmentedControl from '../controls/SegmentedControl.vue'
import { profitPeriods, profitSummary, profitTypes, type ProfitSummary } from './dashboardMocks'

const props = withDefaults(defineProps<{
  types?: string[]
  periods?: string[]
  summary?: ProfitSummary
}>(), {
  types: () => profitTypes,
  periods: () => profitPeriods,
  summary: () => profitSummary,
})

const profitType = ref(props.types[0] ?? '')
const period = ref(props.periods[1] ?? props.periods[0] ?? '')
</script>

<template>
  <section class="card span-6 profit-card">
    <div class="profit-head">
      <div class="card-title">结算利润<svg class="ic title-caret" aria-hidden="true"><use href="#i-chev-right"/></svg></div>
      <div class="profit-head-right">
        <SegmentedControl v-model="profitType" :options="types" aria-label="利润类型" />
        <SegmentedControl v-model="period" :options="periods" aria-label="利润周期" />
      </div>
    </div>
    <div class="profit-body">
      <div id="profit-chart-description" class="profit-summary">
        <div>
          <div class="profit-total-label">{{ summary.label }}</div>
          <div class="profit-total tnum">{{ summary.total }}<small>{{ summary.currency }}</small></div>
          <div class="profit-deltas">
            <div><span>结算毛利率</span><b>{{ summary.margin }}</b></div>
            <div><span>订单利润</span><b class="down">{{ summary.orderProfit }}</b></div>
          </div>
        </div>
        <div class="profit-tabs"><span class="muted">利润趋势</span><span style="color:var(--success);font-size:11px">{{ summary.change }}</span></div>
      </div>
      <div class="profit-chart">
        <div class="profit-chart-legend"><span><i class="legend-color"></i>结算利润</span><span><i class="legend-color" style="background:#BFD8FF"></i>订单利润</span></div>
        <svg viewBox="0 0 360 170" preserveAspectRatio="none" role="img" aria-label="结算利润与订单利润柱状趋势图" aria-describedby="profit-chart-description">
          <g stroke="#ECF0F5" stroke-width="1" stroke-dasharray="3 3"><line x1="18" y1="24" x2="352" y2="24"/><line x1="18" y1="67" x2="352" y2="67"/><line x1="18" y1="110" x2="352" y2="110"/><line x1="18" y1="153" x2="352" y2="153"/></g>
          <g fill="#A6ABB4" font-family="Verdana" font-size="9"><text x="4" y="27">$2k</text><text x="4" y="70">$1.5k</text><text x="4" y="113">$1k</text><text x="4" y="156">$0</text></g>
          <g fill="#005BF5">
            <rect x="38" y="96" width="20" height="57" rx="2"></rect><rect x="78" y="78" width="20" height="75" rx="2"></rect><rect x="118" y="88" width="20" height="65" rx="2"></rect><rect x="158" y="62" width="20" height="91" rx="2"></rect><rect x="198" y="72" width="20" height="81" rx="2"></rect><rect x="238" y="48" width="20" height="105" rx="2"></rect><rect x="278" y="58" width="20" height="95" rx="2"></rect>
          </g>
          <g fill="#BFD8FF">
            <rect x="60" y="110" width="10" height="43" rx="2"></rect><rect x="100" y="98" width="10" height="55" rx="2"></rect><rect x="140" y="108" width="10" height="45" rx="2"></rect><rect x="180" y="86" width="10" height="67" rx="2"></rect><rect x="220" y="98" width="10" height="55" rx="2"></rect><rect x="260" y="74" width="10" height="79" rx="2"></rect><rect x="300" y="86" width="10" height="67" rx="2"></rect>
          </g>
          <g fill="#888C94" font-family="Verdana" font-size="9" text-anchor="middle"><text x="53" y="168">11-06</text><text x="93" y="168">11-07</text><text x="133" y="168">11-08</text><text x="173" y="168">11-09</text><text x="213" y="168">11-10</text><text x="253" y="168">11-11</text><text x="293" y="168">11-12</text></g>
          <polyline points="48,81 88,67 128,73 168,54 208,61 248,40 288,47" fill="none" stroke="#005BF5" stroke-width="1.5"></polyline>
        </svg>
        <div class="profit-note"><span>{{ summary.note }}</span><span>币种：{{ summary.currency }}</span></div>
      </div>
    </div>
  </section>
</template>
