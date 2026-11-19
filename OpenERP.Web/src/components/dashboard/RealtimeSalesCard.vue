<script setup lang="ts">
import { ref } from 'vue'
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
  <section class="card span-6 realtime-card">
    <div class="card-head">
      <div class="card-title">
        实时销量<svg class="ic title-caret" aria-hidden="true"><use href="#i-chev-right" /></svg>
      </div>
      <div class="realtime-sales-controls">
        <span class="tiny-info"
          >今日：各站点的当天
          <svg class="ic info-dot" style="width: 13px; height: 13px" aria-hidden="true">
            <use href="#i-info" /></svg
        ></span>
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
    <div class="realtime-body realtime-sales-grid">
      <div v-for="metric in metrics" :key="metric.label" class="realtime-stat">
        <div class="realtime-label">{{ metric.label }}</div>
        <div class="realtime-value tnum">{{ metric.value }}</div>
        <div class="realtime-yest">
          {{ metric.previousLabel }} <span class="num">{{ metric.previous }}</span>
          <span class="up">{{ metric.change }}</span>
        </div>
      </div>
    </div>
  </section>
</template>
