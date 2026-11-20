<script setup lang="ts">
import InitGuideCard from './dashboard/InitGuideCard.vue'
import RealtimeSalesCard from './dashboard/RealtimeSalesCard.vue'
import RealtimeAdsCard from './dashboard/RealtimeAdsCard.vue'
import PerformanceTrendCard from './dashboard/PerformanceTrendCard.vue'
import RankingCard from './dashboard/RankingCard.vue'
import TodoCard from './dashboard/TodoCard.vue'
import OperationsTodoCard from './dashboard/OperationsTodoCard.vue'
import OrderDistributionCard from './dashboard/OrderDistributionCard.vue'
import StorePerformanceCard from './dashboard/StorePerformanceCard.vue'
import ReviewSummaryCard from './dashboard/ReviewSummaryCard.vue'
import InventoryOverviewCard from './dashboard/InventoryOverviewCard.vue'
import ReplenishmentCard from './dashboard/ReplenishmentCard.vue'
import LogisticsBoardCard from './dashboard/LogisticsBoardCard.vue'
import ProfitCard from './dashboard/ProfitCard.vue'
import AppButton from '../../ui/AppButton.vue'
import AppControl from '../../ui/AppControl.vue'
import AppIcon from '../../ui/AppIcon.vue'

const headerIconButtonClass =
  'inline-flex size-28 items-center justify-center rounded-4 text-body hover:bg-page hover:text-primary'
</script>

<template>
  <section
    id="page-dashboard"
    data-testid="page-dashboard"
    class="flex h-full min-h-0 w-full flex-col bg-page"
  >
    <h1 class="sr-only">工作台</h1>
    <div
      data-testid="dash-header"
      class="relative z-[5] flex h-52 min-w-0 flex-none items-center gap-6 bg-white px-20 shadow-header"
    >
      <div class="flex min-w-0 flex-none items-center gap-6">
        <AppControl size="lg" muted>全部国家</AppControl>
        <AppControl size="lg" muted>全部店铺</AppControl>
        <AppControl size="lg" muted>Listing负责人</AppControl>
        <AppControl size="lg">USD</AppControl>
        <AppButton size="lg" variant="soft">重置</AppButton>
        <button
          type="button"
          class="inline-flex h-32 shrink-0 items-center justify-center gap-5 rounded-4 border border-transparent bg-transparent px-8 text-12/30 whitespace-nowrap text-ink-muted hover:bg-primary-faint"
        >
          核对说明
        </button>
      </div>
      <div class="ml-auto flex min-w-0 items-center gap-8">
        <span
          class="inline-flex h-28 items-center gap-5 rounded-4 bg-page px-10 text-12 whitespace-nowrap text-ink hover:bg-hover-pill"
          ><span class="text-muted">北京</span><span class="tabular-nums">11-12 15:47</span></span
        >
        <span
          class="inline-flex h-28 items-center gap-5 rounded-4 bg-page px-10 text-12 whitespace-nowrap text-ink hover:bg-hover-pill"
          ><span class="text-muted">英国</span><span class="tabular-nums">11-12 07:47</span></span
        >
        <span
          class="inline-flex h-28 items-center gap-5 rounded-4 bg-page px-10 text-12 whitespace-nowrap text-ink hover:bg-hover-pill"
          ><span class="text-muted">美东</span><span class="tabular-nums">11-12 02:47</span></span
        >
        <span
          class="inline-flex h-28 items-center gap-5 rounded-4 bg-page px-10 text-12 whitespace-nowrap text-ink hover:bg-hover-pill [@media(max-width:1500px)]:hidden"
          ><span class="text-muted">太平洋</span><span class="tabular-nums">11-11 23:47</span></span
        >
        <span class="mx-2 h-16 w-px flex-none bg-divider"></span>
        <button
          type="button"
          class="inline-flex h-28 items-center gap-4 rounded-4 px-8 whitespace-nowrap text-primary hover:bg-page hover:text-primary"
        >
          时钟<AppIcon name="i-chev-down" />
        </button>
        <span class="mx-2 h-16 w-px flex-none bg-divider"></span>
        <AppButton variant="soft">旧版</AppButton>
        <span class="mx-2 h-16 w-px flex-none bg-divider"></span>
        <button
          type="button"
          class="inline-flex h-28 items-center gap-4 rounded-4 px-8 whitespace-nowrap text-body hover:bg-page hover:text-primary"
        >
          初始化引导<AppIcon name="i-chev-down" class="rotate-180" />
        </button>
        <span class="mx-2 h-16 w-px flex-none bg-divider"></span>
        <button type="button" title="刷新" aria-label="刷新" :class="headerIconButtonClass">
          <AppIcon name="i-refresh" />
        </button>
        <button type="button" title="帮助" aria-label="帮助" :class="headerIconButtonClass">
          <AppIcon name="i-help" />
        </button>
        <button
          type="button"
          title="自定义布局"
          aria-label="自定义布局"
          :class="headerIconButtonClass"
        >
          <AppIcon name="i-layout" />
        </button>
      </div>
    </div>
    <div class="min-h-0 flex-1 overflow-x-hidden overflow-y-auto bg-page">
      <div data-testid="dash-grid" class="grid grid-cols-12 items-stretch gap-12 p-16">
        <InitGuideCard />
        <RealtimeSalesCard />
        <RealtimeAdsCard />
        <PerformanceTrendCard />
        <RankingCard />
        <TodoCard />
        <OperationsTodoCard />
        <OrderDistributionCard />
        <StorePerformanceCard />
        <ReviewSummaryCard />
        <InventoryOverviewCard />
        <ReplenishmentCard />
        <LogisticsBoardCard />
        <ProfitCard />
      </div>
    </div>
  </section>
</template>
