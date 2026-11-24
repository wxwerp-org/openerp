<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, useId, watch } from 'vue'
import AppButton from '../../ui/AppButton.vue'
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'
import AppInput from '../../ui/AppInput.vue'
import AppSegmented from '../../ui/AppSegmented.vue'
import type { ReportTrend } from '../../reports/orderSnapshot'
import { formatMoney, formatShortDate, localDateString } from '../../workspace/workspaceData'
import {
  buildTrendComparison,
  buildTrendGeometry,
  resolveTrendState,
  trendOrderTotal,
  trendRangeLabel,
  trendSalesTotal,
  validateTrendRange,
  type TrendPreset,
  type TrendRange,
} from '../../workspace/workspaceDerivation'

const props = defineProps<{
  trend: ReportTrend
  /** 当前已展示数据对应的周期；请求失败时保持旧周期。 */
  preset: TrendPreset
  /** 最近一次应用的自定义区间，用于重新打开面板时回填。 */
  customRange: TrendRange | null
}>()

const emit = defineEmits<{
  'preset-change': [preset: TrendPreset]
  'custom-apply': [range: TrendRange]
  open: []
}>()

const uid = useId()
const descriptionId = `${uid}-trend-description`
const customPanelId = `${uid}-trend-custom`
const customErrorId = `${uid}-trend-custom-error`
const customToggle = ref<HTMLButtonElement | null>(null)
const customOpen = ref(false)
const periodOptions = ['近 7 天', '近 30 天', '本季度', '本年']
const presetLabels: Record<TrendPreset, string> = {
  last7: '近 7 天',
  last30: '近 30 天',
  quarter: '本季度',
  year: '本年',
  custom: '',
}
const presetByLabel: Record<string, Exclude<TrendPreset, 'custom'>> = {
  '近 7 天': 'last7',
  '近 30 天': 'last30',
  本季度: 'quarter',
  本年: 'year',
}
const period = computed({
  get: () => presetLabels[props.preset],
  set: (value: string) => {
    customOpen.value = false
    const preset = presetByLabel[value]
    // 始终上报点击：父组件负责去重，并用请求版本让在途的其它周期失效。
    if (preset) emit('preset-change', preset)
  },
})

const range = computed<TrendRange>(() => ({ start: props.trend.start, end: props.trend.end }))
const rangeLabel = computed(() => trendRangeLabel(props.preset, range.value))

const state = computed(() => resolveTrendState(props.trend))
const comparison = computed(() => buildTrendComparison(props.trend))
const orderTotal = computed(() => trendOrderTotal(props.trend))
const salesTotal = computed(() => trendSalesTotal(props.trend))
const windowLabel = computed(() => {
  // 短周期用 MM-DD；季度/本年/自定义可能跨年，短格式会把两个不同年份显示成同一日期。
  if (props.preset === 'last7' || props.preset === 'last30')
    return `${formatShortDate(props.trend.start)} - ${formatShortDate(props.trend.end)}`
  return `${props.trend.start} - ${props.trend.end}`
})
const footnote = computed(() => {
  const parts = [
    props.trend.currency
      ? `销售额为 ERP 成本币种（${props.trend.currency}），仅统计已同步订单`
      : '销售额仅统计已同步订单',
    `${rangeLabel.value}按订单创建日期分桶`,
  ]
  if (props.trend.missingCreatedCount > 0)
    parts.push(`另有 ${props.trend.missingCreatedCount} 单无创建日期未计入`)
  if (state.value === 'degraded') parts.push('暂无可用金额，存量订单重新同步后可补齐')
  return `${parts.join('；')}。`
})

/** 原生日期输入的 max 与应用校验共用同一个「今天」，跨过本地午夜后同步刷新。 */
const today = ref(localDateString())
const customStart = ref('')
const customEnd = ref('')
const customError = computed(() =>
  customOpen.value ? validateTrendRange({ start: customStart.value, end: customEnd.value }) : null,
)

function refreshToday(): void {
  today.value = localDateString()
}

/** 打开时回填最近一次应用的自定义区间，首次打开取当前图表窗口。 */
function toggleCustom(): void {
  if (!customOpen.value) {
    refreshToday()
    customStart.value = props.customRange?.start ?? props.trend.start
    customEnd.value = props.customRange?.end ?? props.trend.end
  }
  customOpen.value = !customOpen.value
}

function closeCustom(focusToggle: boolean): void {
  customOpen.value = false
  if (!focusToggle) return
  void nextTick(() => customToggle.value?.focus())
}

function applyCustom(): void {
  if (customError.value) return
  closeCustom(true)
  emit('custom-apply', { start: customStart.value, end: customEnd.value })
}

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
onMounted(() => {
  observeChartArea()
  window.addEventListener('focus', refreshToday)
  document.addEventListener('visibilitychange', refreshToday)
})
onUnmounted(() => {
  observer?.disconnect()
  window.removeEventListener('focus', refreshToday)
  document.removeEventListener('visibilitychange', refreshToday)
})

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
  () =>
    `${rangeLabel.value}经营走势图，共 ${props.trend.days} 天；使用左右方向键查看每日数据，Escape 关闭提示`,
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
  return `${rangeLabel.value}共 ${orderTotal.value} 单，${sales}`
})
</script>

<template>
  <AppCard class="col-span-12 flex h-460 flex-col" data-testid="workspace-trend-card">
    <div
      class="flex min-h-54 flex-none flex-wrap items-start justify-between gap-x-12 gap-y-8 px-16 pt-16"
    >
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
      <div class="flex min-w-0 flex-wrap items-center justify-end gap-8 whitespace-nowrap">
        <span class="hidden text-12 text-muted tabular-nums sm:inline" data-testid="trend-window">
          {{ windowLabel }}
        </span>
        <AppSegmented
          v-model="period"
          :options="periodOptions"
          aria-label="经营走势周期"
          :padding-x="7"
          :min-width="52"
          option-class="[@media(max-width:1500px)]:px-6 [@media(max-width:1500px)]:min-w-44"
        />
        <button
          type="button"
          class="inline-flex h-28 shrink-0 items-center gap-4 rounded-4 border bg-white px-7 text-12/26 whitespace-nowrap hover:text-primary"
          :class="preset === 'custom' ? 'border-primary text-primary' : 'border-input text-body'"
          :aria-expanded="customOpen ? 'true' : 'false'"
          aria-label="自定义统计区间"
          data-testid="trend-custom-toggle"
          @click="toggleCustom()"
        >
          <AppIcon name="i-calendar" :size="12" /><span class="hidden sm:inline">自定义</span>
        </button>
      </div>
    </div>

    <div
      v-if="customOpen"
      :id="customPanelId"
      class="flex flex-wrap items-end gap-x-10 gap-y-6 px-16 pt-8"
      data-testid="trend-custom-panel"
    >
      <label class="flex flex-col gap-2 text-11/16 text-muted">
        开始日期
        <AppInput
          v-model="customStart"
          type="date"
          size="sm"
          class="w-124"
          :max="customEnd || today"
          :invalid="customError !== null"
          :aria-describedby="customError ? customErrorId : undefined"
          aria-label="自定义区间开始日期"
          data-testid="trend-custom-start"
        />
      </label>
      <label class="flex flex-col gap-2 text-11/16 text-muted">
        结束日期
        <AppInput
          v-model="customEnd"
          type="date"
          size="sm"
          class="w-124"
          :min="customStart || undefined"
          :max="today"
          :invalid="customError !== null"
          :aria-describedby="customError ? customErrorId : undefined"
          aria-label="自定义区间结束日期"
          data-testid="trend-custom-end"
        />
      </label>
      <div class="flex items-center gap-8 pb-1">
        <AppButton
          :disabled="customError !== null"
          data-testid="trend-custom-apply"
          @click="applyCustom()"
        >
          应用
        </AppButton>
        <button type="button" class="text-12 text-muted hover:text-ink" @click="customOpen = false">
          取消
        </button>
      </div>
      <p
        v-if="customError"
        :id="customErrorId"
        class="w-full text-11/16 text-danger"
        role="alert"
        data-testid="trend-custom-error"
      >
        {{ customError }}
      </p>
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

    <div class="relative mx-16 mt-8 mb-2 flex min-h-0 flex-1 flex-col border-t border-page">
      <div
        v-if="state !== 'empty'"
        class="flex h-20 flex-none items-center justify-end gap-16 px-2 text-12 text-muted"
      >
        <span v-if="showSales" class="flex items-center gap-5 whitespace-nowrap">
          <span class="h-2 w-14 rounded-1" style="background: var(--color-primary)"></span>销售额
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
          <g v-if="showSales" class="fill-placeholder font-sans text-11">
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
          <g class="fill-placeholder font-sans text-11">
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
              class="fill-tag-blue-bg"
              :d="segment"
            ></path>
            <path
              v-for="(segment, index) in geometry.sales.lineSegments"
              :key="`sales-${index}`"
              class="fill-none stroke-primary [stroke-width:2] [stroke-linecap:round] [stroke-linejoin:round]"
              :d="segment"
            ></path>
          </g>
          <g v-if="showSales">
            <circle
              v-for="marker in geometry.sales.markers"
              :key="`marker-${marker.x}`"
              class="fill-primary stroke-white [stroke-width:1]"
              :cx="marker.x"
              :cy="marker.y"
              r="2.5"
            ></circle>
          </g>
          <path
            class="fill-none stroke-chart-line-alt [stroke-width:1.6] [stroke-dasharray:4_4] [stroke-linecap:round] [stroke-linejoin:round]"
            :d="geometry.orders.line"
          ></path>
          <g v-if="geometry.orders.markers.length > 0">
            <circle
              v-for="marker in geometry.orders.markers"
              :key="`orders-marker-${marker.x}`"
              class="fill-chart-line-alt stroke-white [stroke-width:1]"
              :cx="marker.x"
              :cy="marker.y"
              r="2.5"
            ></circle>
          </g>
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
              class="fill-white stroke-primary [stroke-width:2]"
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
          class="pointer-events-none absolute z-[2] rounded-4 border border-line bg-white px-10 py-6 drop-shadow-chart-tooltip"
          :style="{
            left: `${tooltip.left}px`,
            top: `${tooltip.top}px`,
            width: `${tooltip.width}px`,
          }"
          data-testid="trend-tooltip"
        >
          <div class="text-11/16 text-muted tabular-nums">{{ tooltip.date }}</div>
          <div
            v-if="showSales"
            class="mt-2 text-12 font-semibold whitespace-nowrap text-ink tabular-nums"
          >
            {{ tooltip.sales }}
          </div>
          <div class="mt-1 text-11/16 text-muted">
            订单
            <b class="font-medium text-ink tabular-nums">{{ tooltip.orders }}</b> 单
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
