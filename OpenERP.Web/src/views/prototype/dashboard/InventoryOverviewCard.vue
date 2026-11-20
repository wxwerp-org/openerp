<script setup lang="ts">
import { ref } from 'vue'
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'
import AppSegmented from '../../ui/AppSegmented.vue'
import { inventoryMetrics, warehouseStocks, type WarehouseStock } from './dashboardMocks'

const props = withDefaults(
  defineProps<{
    metrics?: string[]
    warehouses?: WarehouseStock[]
    inStockTotal?: string
    inTransitTotal?: string
    grandTotal?: string
  }>(),
  {
    metrics: () => inventoryMetrics,
    warehouses: () => warehouseStocks,
    inStockTotal: '8,420',
    inTransitTotal: '1,260',
    grandTotal: '9,680',
  },
)

const metric = ref(props.metrics[0] ?? '')
</script>

<template>
  <AppCard data-testid="dashboard-card-inventory" class="col-span-6 flex h-400 flex-col">
    <div class="flex h-54 flex-none items-start justify-between px-16 pt-16">
      <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
        库存概览<AppIcon
          name="i-chev-right"
          :size="12"
          class="text-title-caret [stroke-width:1.8]"
        />
      </div>
      <div class="flex items-center gap-8">
        <AppSegmented v-model="metric" :options="metrics" aria-label="库存指标" />
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
          title="设置"
          aria-label="设置"
          class="inline-flex size-28 items-center justify-center rounded-4 text-muted hover:bg-page hover:text-primary"
        >
          <AppIcon name="i-settings" />
        </button>
      </div>
    </div>
    <div class="flex min-h-0 flex-1 gap-16 px-16 pb-16">
      <div class="flex w-198 flex-none flex-col pt-2">
        <div class="text-12 text-muted">总库存量</div>
        <div class="mt-2 text-20/28 font-medium text-ink tabular-nums">
          {{ grandTotal }}<small class="ml-4 text-11/28 font-normal text-muted">件</small>
        </div>
        <div class="mt-12 flex h-8 overflow-hidden rounded-4 bg-stock-track">
          <span class="block h-full w-[63%] bg-primary"></span
          ><span class="block h-full w-[21%] bg-chart-2"></span
          ><span class="block h-full w-[11%] bg-chart-3"></span
          ><span class="block h-full w-[5%] bg-stock-segment-4"></span>
        </div>
        <div class="mt-8 grid grid-cols-2 gap-x-8 gap-y-6">
          <span
            v-for="warehouse in warehouses"
            :key="warehouse.name"
            class="flex items-center gap-5 text-11/[16.5px] whitespace-nowrap text-body"
            ><i
              class="inline-block size-7 flex-none rounded-2 bg-primary"
              :style="{ background: warehouse.color }"
            ></i
            >{{ warehouse.name }}
            <b class="ml-auto font-medium text-ink tabular-nums">{{ warehouse.total }}</b></span
          >
        </div>
        <div class="mt-16 flex flex-col gap-8">
          <div
            class="flex h-50 items-center justify-between rounded-4 border border-line-soft bg-row-soft px-10"
          >
            <span class="text-11/[16.5px] text-muted">在库</span
            ><b class="text-14/[21px] font-semibold text-ink tabular-nums"
              >{{ inStockTotal }} <span class="text-11/[16.5px] font-normal text-muted">件</span></b
            >
          </div>
          <div
            class="flex h-50 items-center justify-between rounded-4 border border-line-soft bg-row-soft px-10"
          >
            <span class="text-11/[16.5px] text-muted">在途</span
            ><b class="text-14/[21px] font-semibold text-ink tabular-nums"
              >{{ inTransitTotal }}
              <span class="text-11/[16.5px] font-normal text-muted">件</span></b
            >
          </div>
        </div>
      </div>
      <div
        data-testid="inventory-scroll"
        role="region"
        tabindex="0"
        aria-label="库存仓库数据表格"
        class="min-w-0 flex-1 overflow-auto rounded-4 border border-line-soft"
      >
        <table data-testid="inventory-table" class="w-full table-fixed border-collapse">
          <caption class="sr-only">
            各仓库库存数量
          </caption>
          <thead>
            <tr>
              <th
                scope="col"
                class="h-40 border-b border-line-soft bg-page-soft px-12 text-left text-12 font-semibold whitespace-nowrap text-ink"
              >
                仓库类型
              </th>
              <th
                scope="col"
                class="h-40 border-b border-line-soft bg-page-soft px-12 text-right text-12 font-semibold whitespace-nowrap text-ink"
              >
                在库
              </th>
              <th
                scope="col"
                class="h-40 border-b border-line-soft bg-page-soft px-12 text-right text-12 font-semibold whitespace-nowrap text-ink"
              >
                在途
              </th>
              <th
                scope="col"
                class="h-40 border-b border-line-soft bg-page-soft px-12 text-right text-12 font-semibold whitespace-nowrap text-ink"
              >
                可售天数
              </th>
              <th
                scope="col"
                class="h-40 border-b border-line-soft bg-page-soft px-12 text-right text-12 font-semibold whitespace-nowrap text-ink"
              >
                合计
              </th>
            </tr>
          </thead>
          <tbody class="[&>tr:last-child>td]:border-b-0">
            <tr v-for="warehouse in warehouses" :key="warehouse.name" class="group">
              <td
                class="h-48 border-b border-line-soft px-12 text-left text-12 text-ink tabular-nums group-hover:bg-row-hover-soft"
              >
                <span class="flex items-center gap-6"
                  ><i
                    class="size-6 flex-none rounded-2 bg-primary"
                    :style="{ background: warehouse.color }"
                  ></i
                  >{{ warehouse.name }}</span
                >
              </td>
              <td
                class="h-48 border-b border-line-soft px-12 text-right text-12 text-body tabular-nums group-hover:bg-row-hover-soft"
              >
                {{ warehouse.inStock }}
              </td>
              <td
                class="h-48 border-b border-line-soft px-12 text-right text-12 text-body tabular-nums group-hover:bg-row-hover-soft"
              >
                {{ warehouse.inTransit }}
              </td>
              <td
                class="h-48 border-b border-line-soft px-12 text-right text-12 text-body tabular-nums group-hover:bg-row-hover-soft"
              >
                {{ warehouse.days }}
              </td>
              <td
                class="h-48 border-b border-line-soft px-12 text-right text-12 text-body tabular-nums group-hover:bg-row-hover-soft"
              >
                {{ warehouse.total }}
              </td>
            </tr>
          </tbody>
          <tfoot>
            <tr>
              <td
                class="h-48 border-b border-line-soft bg-page-soft px-12 text-left text-12 font-semibold text-ink tabular-nums"
              >
                合计
              </td>
              <td
                class="h-48 border-b border-line-soft bg-page-soft px-12 text-right text-12 font-semibold text-ink tabular-nums"
              >
                {{ inStockTotal }}
              </td>
              <td
                class="h-48 border-b border-line-soft bg-page-soft px-12 text-right text-12 font-semibold text-ink tabular-nums"
              >
                {{ inTransitTotal }}
              </td>
              <td
                class="h-48 border-b border-line-soft bg-page-soft px-12 text-right text-12 font-semibold text-ink tabular-nums"
              >
                —
              </td>
              <td
                class="h-48 border-b border-line-soft bg-page-soft px-12 text-right text-12 font-semibold text-ink tabular-nums"
              >
                {{ grandTotal }}
              </td>
            </tr>
          </tfoot>
        </table>
      </div>
    </div>
  </AppCard>
</template>
