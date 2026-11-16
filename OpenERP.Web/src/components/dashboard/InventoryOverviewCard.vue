<script setup lang="ts">
import { ref } from 'vue'
import SegmentedControl from '../controls/SegmentedControl.vue'
import { inventoryMetrics, warehouseStocks, type WarehouseStock } from './dashboardMocks'

const props = withDefaults(defineProps<{
  metrics?: string[]
  warehouses?: WarehouseStock[]
  inStockTotal?: string
  inTransitTotal?: string
  grandTotal?: string
}>(), {
  metrics: () => inventoryMetrics,
  warehouses: () => warehouseStocks,
  inStockTotal: '8,420',
  inTransitTotal: '1,260',
  grandTotal: '9,680',
})

const metric = ref(props.metrics[0] ?? '')
</script>

<template>
  <section class="card span-6 inventory-card">
    <div class="inventory-head">
      <div class="card-title">库存概览<svg class="ic title-caret" aria-hidden="true"><use href="#i-chev-right"/></svg></div>
      <div class="inventory-tools">
        <SegmentedControl v-model="metric" :options="metrics" aria-label="库存指标" />
        <button class="trend-icon-btn" type="button" title="刷新" aria-label="刷新"><svg class="ic" aria-hidden="true"><use href="#i-refresh"/></svg></button>
        <button class="trend-icon-btn" type="button" title="设置" aria-label="设置"><svg class="ic" aria-hidden="true"><use href="#i-settings"/></svg></button>
      </div>
    </div>
    <div class="inventory-body">
      <div class="inventory-summary">
        <div class="card-subtip">总库存量</div>
        <div class="inventory-total tnum">{{ grandTotal }}<small>件</small></div>
        <div class="stock-bar"><span class="s1"></span><span class="s2"></span><span class="s3"></span><span class="s4"></span></div>
        <div class="inventory-legend">
          <span v-for="warehouse in warehouses" :key="warehouse.name"><i class="legend-color" :style="{ background: warehouse.color }"></i>{{ warehouse.name }} <b>{{ warehouse.total }}</b></span>
        </div>
        <div class="inventory-kpis">
          <div class="inventory-kpi"><span>在库</span><b class="tnum">{{ inStockTotal }} <span style="font-weight:400;color:var(--muted)">件</span></b></div>
          <div class="inventory-kpi"><span>在途</span><b class="tnum">{{ inTransitTotal }} <span style="font-weight:400;color:var(--muted)">件</span></b></div>
        </div>
      </div>
      <div class="inventory-table-wrap" role="region" tabindex="0" aria-label="库存仓库数据表格">
        <table class="stock-table">
          <caption class="visually-hidden">各仓库库存数量</caption>
          <thead><tr><th scope="col">仓库类型</th><th scope="col">在库</th><th scope="col">在途</th><th scope="col">可售天数</th><th scope="col">合计</th></tr></thead>
          <tbody>
            <tr v-for="warehouse in warehouses" :key="warehouse.name">
              <td><span class="warehouse-label"><i class="wh-dot" :style="{ background: warehouse.color }"></i>{{ warehouse.name }}</span></td>
              <td>{{ warehouse.inStock }}</td><td>{{ warehouse.inTransit }}</td><td>{{ warehouse.days }}</td><td>{{ warehouse.total }}</td>
            </tr>
          </tbody>
          <tfoot><tr><td>合计</td><td>{{ inStockTotal }}</td><td>{{ inTransitTotal }}</td><td>—</td><td>{{ grandTotal }}</td></tr></tfoot>
        </table>
      </div>
    </div>
  </section>
</template>
