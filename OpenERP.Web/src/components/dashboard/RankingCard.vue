<script setup lang="ts">
import { ref } from 'vue'
import AppSegmented from '../../ui/AppSegmented.vue'
import { rankingDimensions, rankingPeriods, rankingRows, type RankRow } from './dashboardMocks'

const props = withDefaults(
  defineProps<{
    dimensions?: string[]
    periods?: string[]
    rows?: RankRow[]
  }>(),
  {
    dimensions: () => rankingDimensions,
    periods: () => rankingPeriods,
    rows: () => rankingRows,
  },
)

const dimension = ref(props.dimensions[0] ?? '')
const period = ref(props.periods[0] ?? '')
</script>

<template>
  <section class="card span-12 rank-card">
    <div class="rank-head">
      <div class="card-head-left">
        <div class="card-title">
          排行榜<svg class="ic title-caret" aria-hidden="true"><use href="#i-chev-right" /></svg>
        </div>
      </div>
      <div class="card-head-right">
        <button class="trend-icon-btn" type="button" title="刷新" aria-label="刷新">
          <svg class="ic" aria-hidden="true"><use href="#i-refresh" /></svg>
        </button>
        <button class="trend-icon-btn" type="button" title="放大" aria-label="放大">
          <svg class="ic" aria-hidden="true"><use href="#i-expand" /></svg>
        </button>
        <button class="trend-icon-btn" type="button" title="设置" aria-label="设置">
          <svg class="ic" aria-hidden="true"><use href="#i-settings" /></svg>
        </button>
      </div>
    </div>
    <div class="rank-controls">
      <div class="rank-controls-left">
        <AppSegmented
          v-model="dimension"
          :options="dimensions"
          aria-label="排行商品维度"
          :line-height="28"
          color="ink"
          :min-width="0"
        />
        <button class="control" type="button">
          店铺<svg class="ic caret" aria-hidden="true"><use href="#i-chev-down" /></svg>
        </button>
        <div class="field-box" style="width: 84px">
          <input aria-label="ASIN" placeholder="ASIN" />
        </div>
        <button class="control" type="button">
          模糊<svg class="ic caret" aria-hidden="true"><use href="#i-chev-down" /></svg>
        </button>
        <div class="field-box" style="flex: 1; max-width: 210px; min-width: 120px">
          <input aria-label="搜索排行榜" placeholder="搜索 ASIN / SKU / 品名" /><svg
            class="ic caret"
            style="opacity: 0.8"
            aria-hidden="true"
          >
            <use href="#i-search" />
          </svg>
        </div>
        <button class="btn" type="button">更多筛选</button>
      </div>
      <div class="rank-controls-right">
        <AppSegmented v-model="period" :options="periods" aria-label="排行周期" />
        <button class="date-range-btn" type="button">
          <span>2026-10-13 - 2026-11-11</span
          ><svg class="ic" aria-hidden="true"><use href="#i-calendar" /></svg>
        </button>
      </div>
    </div>
    <div class="rank-scroll" role="region" tabindex="0" aria-label="排行榜数据表格">
      <table class="rank-table">
        <caption class="visually-hidden">
          商品销量排行榜
        </caption>
        <colgroup>
          <col style="width: 270px" />
          <col style="width: 160px" />
          <col style="width: 100px" />
          <col style="width: 90px" />
          <col style="width: 100px" />
          <col style="width: 110px" />
          <col style="width: 120px" />
          <col style="width: 110px" />
          <col style="width: 100px" />
          <col style="width: 150px" />
          <col style="width: 100px" />
          <col style="width: 120px" />
        </colgroup>
        <thead>
          <tr>
            <th scope="col">商品</th>
            <th scope="col">MSKU / SKU</th>
            <th scope="col" class="num">销量</th>
            <th scope="col" class="num">订单量</th>
            <th scope="col" class="num">销售额</th>
            <th scope="col" class="num">净销售额</th>
            <th scope="col" class="num">FBA可售</th>
            <th scope="col" class="num">7天日均</th>
            <th scope="col" class="num">结算毛利率</th>
            <th scope="col" class="num">退款额</th>
            <th scope="col" class="num">ACOS</th>
            <th scope="col" class="num">天维度趋势</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in rows" :key="row.rank">
            <td>
              <div class="rank-product-cell">
                <span class="rank-number" :class="{ top: row.rank <= 3 }">{{ row.rank }}</span>
                <div class="rank-thumb">
                  <svg viewBox="0 0 30 30" style="width: 30px; height: 30px" aria-hidden="true">
                    <rect width="30" height="30" :fill="row.thumbnail.background" />
                    <path
                      :d="row.thumbnail.primaryPath"
                      fill="none"
                      :stroke="row.thumbnail.primaryStroke"
                      stroke-width="1.2"
                    />
                    <path
                      v-if="row.thumbnail.secondaryPath"
                      :d="row.thumbnail.secondaryPath"
                      :stroke="row.thumbnail.secondaryStroke"
                      stroke-width="1.2"
                    />
                  </svg>
                </div>
                <div class="rank-product-text">
                  <div class="rank-product-title">{{ row.title }}</div>
                  <div class="rank-product-meta">{{ row.meta }}</div>
                </div>
              </div>
            </td>
            <td>{{ row.sku }}</td>
            <td class="num">{{ row.sales }}</td>
            <td class="num">{{ row.orders }}</td>
            <td class="num">{{ row.revenue }}</td>
            <td class="num">{{ row.netRevenue }}</td>
            <td class="num">{{ row.fba }}</td>
            <td class="num">{{ row.dailyAverage }}</td>
            <td class="num">{{ row.margin }}</td>
            <td class="num">{{ row.refund }}</td>
            <td class="num">{{ row.acos }}</td>
            <td>
              <svg class="spark" viewBox="0 0 54 22" aria-hidden="true">
                <path :d="row.sparkPath" stroke-width="1.5" />
              </svg>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>
