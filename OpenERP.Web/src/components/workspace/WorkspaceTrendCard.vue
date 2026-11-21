<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, useId, watch } from 'vue'
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'
import AppSegmented from '../../ui/AppSegmented.vue'
import type { ReportTrend } from '../../reports/orderSnapshot'
import { formatMoney, formatShortDate } from '../../workspace/workspaceData'
import {
  buildTrendComparison,
  buildTrendGeometry,
  resolveTrendState,
  trendOrderTotal,
  trendSalesTotal,
} from '../../workspace/workspaceDerivation'

const props = defineProps<{ trend: ReportTrend }>()

const emit = defineEmits<{ 'period-change': [days: 7 | 30]; open: [] }>()

const uid = useId()
const descriptionId = `${uid}-trend-description`
const period = computed({
  get: () => (props.trend.days === 30 ? '近 30 天' : '近 7 天'),
  set: (value: string) => emit('period-change', value === '近 30 天' ? 30 : 7),
})

const state = computed(() => resolveTrendState(props.trend))
const comparison = computed(() => buildTrendComparison(props.trend))
const orderTotal = computed(() => trendOrderTotal(props.trend))
const salesTotal = computed(() => trendSalesTotal(props.trend))
const windowLabel = computed(() => {
  const first = props.trend.points[0]
  return first ? `${formatShortDate(first.date)} - ${formatShortDate(props.trend.end)}` : ''
})
const footnote = computed(() => {
  const parts = [
    props.trend.currency
      ? `销售额为 ERP 成本币种（${props.trend.currency}），仅统计已同步订单`
      : '销售额仅统计已同步订单',
    `近 ${props.trend.days} 天按订单创建日期分桶`,
  ]
  if (props.trend.missingCreatedCount > 0)
    parts.push(`另有 ${props.trend.missingCreatedCount} 单无创建日期未计入`)
  if (state.value === 'degraded') parts.push('暂无可用金额，存量订单重新同步后可补齐')
  return `${parts.join('；')}。`
})

const chartArea = ref<HTMLElement | null>(null)
const chartSize = ref({ width: 720, height: 240 })
let observer: ResizeObserver | null = null

/**
 * 空态不渲染图表节点，之后进入有数据态或切换周期时才创建；ResizeObserver 必须
 * 跟随模板引用重新绑定，否则会沿用默认尺寸并在窄屏裁掉右轴和末尾日期。
 */
function observeChartArea(): void {
  observer?.disconnect()
  observer = null
  const element = chartArea.value
  if (!element || typeof ResizeObserver === 'undefined') return
  observer = new ResizeObserver((entries) => {
    const rect = entries[0]?.contentRect
    if (!rect || rect.width <= 0 || rect.height <= 0) return
    chartSize.value = { width: Math.round(rect.width), height: Math.round(rect.height) }
  })
  observer.observe(element)
}

watch(chartArea, observeChartArea, { flush: 'post' })
onMounted(observeChartArea)
onUnmounted(() => observer?.disconnect())

const geometry = computed(() => buildTrendGeometry(props.trend, chartSize.value))
const showSales = computed(() => geometry.value.showSales)

const hoveredIndex = ref<number | null>(null)
const hovered = computed(() => {
  const index = hoveredIndex.value
  if (index === null) return null
  const point = props.trend.points[index]
  const chart = geometry.value.points[index]
  if (!point || !chart) return null
  return { point, chart }
})

const chartHint = computed(
  () => `经营走势图，共 ${props.trend.days} 天；使用左右方向键查看每日数据，Escape 关闭提示`,
)

/** 键盘等价交互：方向键逐日移动提示，Escape 关闭；指针离开时也清除。 */
function onKeydown(event: KeyboardEvent): void {
  const count = props.trend.points.length
  if (count === 0) return
  if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
    event.preventDefault()
    hoveredIndex.value = Math.min(count - 1, (hoveredIndex.value ?? -1) + 1)
    return
  }
  if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
    event.preventDefault()
    hoveredIndex.value = Math.max(0, (hoveredIndex.value ?? count) - 1)
    return
  }
  if (event.key === 'Escape') {
    hoveredIndex.value = null
  }
}

function onFocus(): void {
  if (hoveredIndex.value === null && props.trend.points.length > 0)
    hoveredIndex.value = props.trend.points.length - 1
}

function onBlur(): void {
  hoveredIndex.value = null
}

function handlePointerMove(event: PointerEvent): void {
  const geometryValue = geometry.value
  const target = event.currentTarget as HTMLElement
  const rect = target.getBoundingClientRect()
  const x = event.clientX - rect.left
  let nearest = 0
  let distance = Number.POSITIVE_INFINITY
  for (const point of geometryValue.points) {
    const current = Math.abs(point.x - x)
    if (current < distance) {
      distance = current
      nearest = point.index
    }
  }
  hoveredIndex.value = nearest
}

const tooltip = computed(() => {
  if (!hovered.value) return null
  const { point, chart } = hovered.value
  const width = 150
  const left = Math.min(Math.max(chart.x - width / 2, 4), geometry.value.width - width - 4)
  const anchor = chart.ySales ?? chart.yOrders
  const top = Math.max(4, anchor - 66)
  return {
    left,
    top,
    width,
    date: point.date,
    sales:
      point.salesAmount === null
        ? '金额未知'
        : formatMoney(point.salesAmount, props.trend.currency),
    orders: point.orderCount,
  }
})

const toneClass: Record<string, string> = {
  up: 'text-success',
  down: 'text-danger',
  flat: 'text-muted',
  none: 'text-muted',
}

const summaryText = computed(() => {
  const sales =
    showSales.value && salesTotal.value !== null
      ? `销售额 ${formatMoney(salesTotal.value, props.trend.currency)}`
      : '销售额不可用'
  return `近 ${props.trend.days} 天共 ${orderTotal.value} 单，${sales}`
})
</script>

<template>
  <AppCard class="col-span-12 flex h-460 flex-col" data-testid="workspace-trend-card">
    <div class="flex h-54 flex-none items-start justify-between gap-12 px-16 pt-16">
      <button
        type="button"
        class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink hover:text-primary"
        @click="emit('open')"
      >
        经营走势<AppIcon
          name="i-chev-right"
          :size="12"
          class="text-title-caret [stroke-width:1.8]"
        />
      </button>
      <div class="flex min-w-0 flex-nowrap items-center gap-8 whitespace-nowrap">
        <span class="text-12 text-muted tabular-nums" data-testid="trend-window">
          {{ windowLabel }}
        </span>
        <AppSegmented
          v-model="period"
          :options="['近 7 天', '近 30 天']"
          aria-label="经营走势周期"
          :padding-x="7"
          :min-width="52"
          option-class="[@media(max-width:1500px)]:px-6 [@media(max-width:1500px)]:min-w-44"
        />
      </div>
    </div>

    <div class="flex flex-none flex-wrap items-center gap-x-16 gap-y-4 px-16 pt-10">
      <span
        v-if="state !== 'degraded'"
        class="flex items-baseline gap-6"
        data-testid="trend-sales-comparison"
      >
        <span class="text-12/16 text-body">{{ comparison.sales.label }}</span>
        <b class="text-14/22 font-semibold tabular-nums" :class="toneClass[comparison.sales.tone]">
          {{ comparison.sales.text }}
        </b>
      </span>
      <span class="flex items-baseline gap-6" data-testid="trend-orders-comparison">
        <span class="text-12/16 text-body">{{ comparison.orders.label }}</span>
        <b class="text-14/22 font-semibold tabular-nums" :class="toneClass[comparison.orders.tone]">
          {{ comparison.orders.text }}
        </b>
      </span>
      <span
        v-if="state === 'degraded'"
        class="ml-auto rounded-4 bg-notice-amber-bg px-8 py-2 text-11 text-notice-amber-text"
        role="status"
        data-testid="trend-degraded"
      >
        暂无可用金额，仅显示订单数；存量订单重新同步后可补齐
      </span>
    </div>

    <div
      class="relative mx-16 mt-8 mb-2 flex min-h-0 flex-1 flex-col border-t border-trend-divider"
    >
      <div
        v-if="state !== 'empty'"
        class="flex h-20 flex-none items-center justify-end gap-16 px-2 text-12 text-muted"
      >
        <span v-if="showSales" class="flex items-center gap-5 whitespace-nowrap">
          <span class="h-2 w-14 rounded-1" style="background: var(--color-chart-line)"></span>销售额
        </span>
        <span class="flex items-center gap-5 whitespace-nowrap">
          <span class="h-2 w-14 rounded-1" style="background: var(--color-chart-line-alt)"></span
          >订单数
        </span>
      </div>

      <div
        v-if="state === 'empty'"
        class="flex flex-1 items-center justify-center text-12 text-muted"
      >
        暂无订单数据
      </div>
      <div
        v-else
        ref="chartArea"
        class="relative min-h-0 flex-1 rounded-4 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
        data-testid="workspace-trend-chart"
        tabindex="0"
        role="group"
        :aria-label="chartHint"
        @pointermove="handlePointerMove"
        @pointerleave="hoveredIndex = null"
        @focus="onFocus"
        @blur="onBlur"
        @keydown="onKeydown"
      >
        <svg
          class="block"
          :width="geometry.width"
          :height="geometry.height"
          role="img"
          :aria-label="`经营走势图：${summaryText}`"
          :aria-describedby="descriptionId"
        >
          <g class="[stroke-dasharray:3_3] stroke-chart-grid [stroke-width:1]">
            <line
              v-for="tick in showSales ? geometry.sales.ticks : geometry.orders.ticks"
              :key="`grid-${tick.y}`"
              :x1="geometry.plot.left"
              :y1="tick.y"
              :x2="geometry.plot.right"
              :y2="tick.y"
            ></line>
          </g>
          <g v-if="showSales" class="fill-chart-axis font-sans text-11">
            <text
              v-for="tick in geometry.sales.ticks"
              :key="`sales-${tick.y}`"
              :x="geometry.plot.left - 8"
              :y="tick.y + 4"
              text-anchor="end"
            >
              {{ tick.label }}
            </text>
          </g>
          <g class="fill-chart-axis font-sans text-11">
            <text
              v-for="tick in geometry.orders.ticks"
              :key="`orders-${tick.y}`"
              :x="geometry.plot.right + 8"
              :y="tick.y + 4"
            >
              {{ tick.label }}
            </text>
            <text
              v-for="tick in geometry.xTicks"
              :key="`x-${tick.index}`"
              :x="tick.x"
              :y="geometry.plot.bottom + 18"
              text-anchor="middle"
            >
              {{ tick.label }}
            </text>
          </g>
          <g v-if="showSales">
            <path
              v-for="(segment, index) in geometry.sales.areaSegments"
              :key="`area-${index}`"
              class="fill-chart-fill"
              :d="segment"
            ></path>
            <path
              v-for="(segment, index) in geometry.sales.lineSegments"
              :key="`sales-${index}`"
              class="fill-none stroke-chart-line [stroke-width:2] [stroke-linecap:round] [stroke-linejoin:round]"
              :d="segment"
            ></path>
          </g>
          <g v-if="showSales">
            <circle
              v-for="marker in geometry.sales.markers"
              :key="`marker-${marker.x}`"
              class="fill-chart-line stroke-white [stroke-width:1]"
              :cx="marker.x"
              :cy="marker.y"
              r="2.5"
            ></circle>
          </g>
          <path
            class="fill-none stroke-chart-line-alt [stroke-width:1.6] [stroke-dasharray:4_4] [stroke-linecap:round] [stroke-linejoin:round]"
            :d="geometry.orders.line"
          ></path>
          <g v-if="hovered">
            <line
              class="[stroke-dasharray:3_3] stroke-chart-point-border [stroke-width:1]"
              :x1="hovered.chart.x"
              :y1="geometry.plot.top"
              :x2="hovered.chart.x"
              :y2="geometry.plot.bottom"
            ></line>
            <circle
              v-if="hovered.chart.ySales !== null"
              class="fill-white stroke-chart-line [stroke-width:2]"
              :cx="hovered.chart.x"
              :cy="hovered.chart.ySales"
              r="3.5"
            ></circle>
            <circle
              class="fill-white stroke-chart-line-alt [stroke-width:2]"
              :cx="hovered.chart.x"
              :cy="hovered.chart.yOrders"
              r="3.5"
            ></circle>
          </g>
        </svg>
        <div
          v-if="tooltip"
          class="pointer-events-none absolute z-[2] rounded-4 border border-chart-tooltip-border bg-white px-10 py-6 drop-shadow-chart-tooltip"
          :style="{
            left: `${tooltip.left}px`,
            top: `${tooltip.top}px`,
            width: `${tooltip.width}px`,
          }"
          data-testid="trend-tooltip"
        >
          <div class="text-11/16 text-chart-tooltip-title tabular-nums">{{ tooltip.date }}</div>
          <div
            v-if="showSales"
            class="mt-2 text-12 font-semibold whitespace-nowrap text-chart-tooltip-value tabular-nums"
          >
            {{ tooltip.sales }}
          </div>
          <div class="mt-1 text-11/16 text-chart-tooltip-title">
            订单
            <b class="font-medium text-chart-tooltip-value tabular-nums">{{ tooltip.orders }}</b> 单
          </div>
        </div>
      </div>
    </div>

    <p :id="descriptionId" class="sr-only">{{ summaryText }}。{{ footnote }}</p>
    <table class="sr-only">
      <caption>
        经营走势数据
      </caption>
      <thead>
        <tr>
          <th scope="col">日期</th>
          <th scope="col">销售额</th>
          <th scope="col">订单数</th>
          <th scope="col">商品件数</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="point in trend.points" :key="point.date">
          <th scope="row">{{ point.date }}</th>
          <td>
            {{
              point.salesAmount === null
                ? '金额未知'
                : formatMoney(point.salesAmount, trend.currency)
            }}
          </td>
          <td>{{ point.orderCount }}</td>
          <td>{{ point.itemQuantity }}</td>
        </tr>
      </tbody>
    </table>
    <p class="mx-16 mb-14 flex-none text-11/16 text-muted" data-testid="trend-footnote">
      {{ footnote }}
    </p>
  </AppCard>
</template>
