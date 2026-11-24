<script setup lang="ts">
import { ref } from 'vue'
import AppButton from '../../../ui/AppButton.vue'
import AppCard from '../../../ui/AppCard.vue'
import AppIcon from '../../../ui/AppIcon.vue'
import AppSegmented from '../../../ui/AppSegmented.vue'
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
  <AppCard data-testid="dashboard-card-ranking" class="col-span-12 flex h-430 flex-col">
    <div class="flex h-50 flex-none items-start justify-between px-16 pt-16">
      <div class="flex min-w-0 items-center gap-8">
        <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
          排行榜<AppIcon
            name="i-chev-right"
            :size="12"
            class="text-title-caret [stroke-width:1.8]"
          />
        </div>
      </div>
      <div class="flex min-w-0 flex-nowrap items-center gap-10">
        <button
          type="button"
          title="刷新"
          aria-label="刷新"
          class="inline-flex size-28 items-center justify-center rounded-4 text-muted hover:bg-page hover:text-primary"
        >
          <AppIcon name="i-refresh" />
        </button>
        <button
          type="button"
          title="放大"
          aria-label="放大"
          class="inline-flex size-28 items-center justify-center rounded-4 text-muted hover:bg-page hover:text-primary"
        >
          <AppIcon name="i-expand" />
        </button>
        <button
          type="button"
          title="设置"
          aria-label="设置"
          class="inline-flex size-28 items-center justify-center rounded-4 text-muted hover:bg-page hover:text-primary"
        >
          <AppIcon name="i-settings" />
        </button>
      </div>
    </div>
    <div class="flex min-h-30 flex-none items-center gap-6 overflow-hidden px-16 pb-10">
      <div class="flex min-w-0 flex-1 items-center gap-6 overflow-hidden">
        <AppSegmented
          v-model="dimension"
          :options="dimensions"
          aria-label="排行商品维度"
          :line-height="28"
          color="ink"
          :min-width="0"
        />
        <button
          type="button"
          class="inline-flex h-28 flex-none items-center gap-6 rounded-4 border border-input bg-white px-8 text-12/26 whitespace-nowrap text-ink hover:border-input-hover"
        >
          店铺<AppIcon name="i-chev-down" :size="10" class="text-caret" />
        </button>
        <div
          class="inline-flex h-28 w-84 flex-none items-center rounded-4 border border-input bg-white focus-within:border-primary"
        >
          <input
            aria-label="ASIN"
            placeholder="ASIN"
            class="h-26 min-w-0 border-0 border-current bg-transparent px-10 py-0 text-12 text-ink outline-none placeholder:text-placeholder"
          />
        </div>
        <button
          type="button"
          class="inline-flex h-28 flex-none items-center gap-6 rounded-4 border border-input bg-white px-8 text-12/26 whitespace-nowrap text-ink hover:border-input-hover"
        >
          模糊<AppIcon name="i-chev-down" :size="10" class="text-caret" />
        </button>
        <div
          class="inline-flex h-28 max-w-210 min-w-120 flex-1 items-center rounded-4 border border-input bg-white focus-within:border-primary"
        >
          <input
            aria-label="搜索排行榜"
            placeholder="搜索 ASIN / SKU / 品名"
            class="h-26 min-w-0 border-0 border-current bg-transparent px-10 py-0 text-12 text-ink outline-none placeholder:text-placeholder"
          />
          <AppIcon name="i-search" :size="10" class="mr-9 flex-none text-caret opacity-80" />
        </div>
        <AppButton>更多筛选</AppButton>
      </div>
      <div class="flex flex-none items-center gap-8">
        <AppSegmented
          v-model="period"
          :options="periods"
          aria-label="排行周期"
          option-class="[@media(max-width:1500px)]:px-6 [@media(max-width:1500px)]:min-w-33"
        />
        <button
          type="button"
          class="inline-flex h-28 items-center justify-center gap-6 rounded-4 border border-input bg-white px-8 text-12 whitespace-nowrap text-ink hover:border-input-hover"
        >
          <span>2026-10-13 - 2026-11-11</span
          ><AppIcon name="i-calendar" :size="14" class="text-date-icon" />
        </button>
      </div>
    </div>
    <div
      data-testid="ranking-scroll"
      role="region"
      tabindex="0"
      aria-label="排行榜数据表格"
      class="mx-16 mb-16 flex-1 min-h-0 overflow-auto rounded-4 border border-line"
    >
      <table
        data-testid="ranking-table"
        class="w-1560 table-fixed border-separate border-spacing-0 bg-white"
      >
        <caption class="sr-only">
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
            <th
              scope="col"
              class="h-40 border-b border-b-line border-r border-r-line-soft bg-page px-10 text-left text-12/18 font-semibold whitespace-nowrap text-ink last:border-r-0"
            >
              商品
            </th>
            <th
              scope="col"
              class="h-40 border-b border-b-line border-r border-r-line-soft bg-page px-10 text-left text-12/18 font-semibold whitespace-nowrap text-ink last:border-r-0"
            >
              MSKU / SKU
            </th>
            <th
              scope="col"
              class="h-40 border-b border-b-line border-r border-r-line-soft bg-page px-10 text-right text-12/18 font-semibold whitespace-nowrap text-ink tabular-nums last:border-r-0"
            >
              销量
            </th>
            <th
              scope="col"
              class="h-40 border-b border-b-line border-r border-r-line-soft bg-page px-10 text-right text-12/18 font-semibold whitespace-nowrap text-ink tabular-nums last:border-r-0"
            >
              订单量
            </th>
            <th
              scope="col"
              class="h-40 border-b border-b-line border-r border-r-line-soft bg-page px-10 text-right text-12/18 font-semibold whitespace-nowrap text-ink tabular-nums last:border-r-0"
            >
              销售额
            </th>
            <th
              scope="col"
              class="h-40 border-b border-b-line border-r border-r-line-soft bg-page px-10 text-right text-12/18 font-semibold whitespace-nowrap text-ink tabular-nums last:border-r-0"
            >
              净销售额
            </th>
            <th
              scope="col"
              class="h-40 border-b border-b-line border-r border-r-line-soft bg-page px-10 text-right text-12/18 font-semibold whitespace-nowrap text-ink tabular-nums last:border-r-0"
            >
              FBA可售
            </th>
            <th
              scope="col"
              class="h-40 border-b border-b-line border-r border-r-line-soft bg-page px-10 text-right text-12/18 font-semibold whitespace-nowrap text-ink tabular-nums last:border-r-0"
            >
              7天日均
            </th>
            <th
              scope="col"
              class="h-40 border-b border-b-line border-r border-r-line-soft bg-page px-10 text-right text-12/18 font-semibold whitespace-nowrap text-ink tabular-nums last:border-r-0"
            >
              结算毛利率
            </th>
            <th
              scope="col"
              class="h-40 border-b border-b-line border-r border-r-line-soft bg-page px-10 text-right text-12/18 font-semibold whitespace-nowrap text-ink tabular-nums last:border-r-0"
            >
              退款额
            </th>
            <th
              scope="col"
              class="h-40 border-b border-b-line border-r border-r-line-soft bg-page px-10 text-right text-12/18 font-semibold whitespace-nowrap text-ink tabular-nums last:border-r-0"
            >
              ACOS
            </th>
            <th
              scope="col"
              class="h-40 border-b border-b-line border-r border-r-line-soft bg-page px-10 text-right text-12/18 font-semibold whitespace-nowrap text-ink tabular-nums last:border-r-0"
            >
              天维度趋势
            </th>
          </tr>
        </thead>
        <tbody class="[&>tr:last-child>td]:border-b-0">
          <tr v-for="row in rows" :key="row.rank" class="group">
            <td
              class="h-48 border-b border-b-line-soft bg-white px-10 align-middle whitespace-nowrap text-12 text-body group-hover:bg-row-hover"
            >
              <div class="flex min-w-0 items-center gap-9">
                <span
                  class="w-16 flex-none text-center text-12 font-bold"
                  :class="row.rank <= 3 ? 'text-primary' : 'text-muted'"
                  >{{ row.rank }}</span
                >
                <div class="size-30 flex-none overflow-hidden rounded-4 bg-page">
                  <svg class="size-30" viewBox="0 0 30 30" aria-hidden="true">
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
                <div class="min-w-0">
                  <div
                    class="max-w-180 overflow-hidden text-ellipsis whitespace-nowrap text-12/16 text-ink"
                  >
                    {{ row.title }}
                  </div>
                  <div class="mt-1 text-11/15 text-muted">{{ row.meta }}</div>
                </div>
              </div>
            </td>
            <td
              class="h-48 border-b border-b-line-soft bg-white px-10 align-middle whitespace-nowrap text-12 text-body group-hover:bg-row-hover"
            >
              {{ row.sku }}
            </td>
            <td
              class="h-48 border-b border-b-line-soft bg-white px-10 text-right align-middle whitespace-nowrap text-12 text-body tabular-nums group-hover:bg-row-hover"
            >
              {{ row.sales }}
            </td>
            <td
              class="h-48 border-b border-b-line-soft bg-white px-10 text-right align-middle whitespace-nowrap text-12 text-body tabular-nums group-hover:bg-row-hover"
            >
              {{ row.orders }}
            </td>
            <td
              class="h-48 border-b border-b-line-soft bg-white px-10 text-right align-middle whitespace-nowrap text-12 text-body tabular-nums group-hover:bg-row-hover"
            >
              {{ row.revenue }}
            </td>
            <td
              class="h-48 border-b border-b-line-soft bg-white px-10 text-right align-middle whitespace-nowrap text-12 text-body tabular-nums group-hover:bg-row-hover"
            >
              {{ row.netRevenue }}
            </td>
            <td
              class="h-48 border-b border-b-line-soft bg-white px-10 text-right align-middle whitespace-nowrap text-12 text-body tabular-nums group-hover:bg-row-hover"
            >
              {{ row.fba }}
            </td>
            <td
              class="h-48 border-b border-b-line-soft bg-white px-10 text-right align-middle whitespace-nowrap text-12 text-body tabular-nums group-hover:bg-row-hover"
            >
              {{ row.dailyAverage }}
            </td>
            <td
              class="h-48 border-b border-b-line-soft bg-white px-10 text-right align-middle whitespace-nowrap text-12 text-body tabular-nums group-hover:bg-row-hover"
            >
              {{ row.margin }}
            </td>
            <td
              class="h-48 border-b border-b-line-soft bg-white px-10 text-right align-middle whitespace-nowrap text-12 text-body tabular-nums group-hover:bg-row-hover"
            >
              {{ row.refund }}
            </td>
            <td
              class="h-48 border-b border-b-line-soft bg-white px-10 text-right align-middle whitespace-nowrap text-12 text-body tabular-nums group-hover:bg-row-hover"
            >
              {{ row.acos }}
            </td>
            <td
              class="h-48 border-b border-b-line-soft bg-white px-10 align-middle whitespace-nowrap text-12 text-body group-hover:bg-row-hover"
            >
              <svg class="h-22 w-54" viewBox="0 0 54 22" aria-hidden="true">
                <path :d="row.sparkPath" class="fill-none stroke-primary [stroke-width:1.4]"></path>
              </svg>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppCard>
</template>
