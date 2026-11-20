<script setup lang="ts">
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'
</script>

<template>
  <AppCard data-testid="dashboard-card-realtime-ads" class="col-span-6 flex h-139 flex-col">
    <div class="flex flex-none items-start justify-between gap-12 px-16 pt-16">
      <div class="flex min-w-0 items-center gap-8">
        <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
          实时广告<AppIcon
            name="i-chev-right"
            :size="12"
            class="text-title-caret [stroke-width:1.8]"
          />
        </div>
      </div>
      <div class="flex min-w-0 flex-nowrap items-center gap-10">
        <span class="flex items-center gap-4 text-12 whitespace-nowrap text-muted">
          统计日期：11-11（站点）
          <AppIcon name="i-info" :size="13" class="ml-2 text-placeholder" />
        </span>
        <span class="flex items-center gap-4 text-12 whitespace-nowrap text-muted"
          >更新于 2 分钟前</span
        >
        <button
          type="button"
          title="广告设置"
          aria-label="广告设置"
          class="inline-flex size-28 items-center justify-center rounded-4 text-body hover:bg-page hover:text-primary"
        >
          <AppIcon name="i-settings" />
        </button>
      </div>
    </div>
    <div class="grid min-h-0 grid-cols-5 gap-x-16 px-16 pt-14">
      <div class="min-w-0 pr-8">
        <div class="truncate text-12/16 text-body">广告花费</div>
        <div
          class="mt-5 text-20/26 font-medium tracking-[-0.2px] whitespace-nowrap text-ink tabular-nums"
        >
          $3,264.00
        </div>
        <div class="mt-3 text-12/16 whitespace-nowrap text-placeholder">
          昨 <span class="text-muted">$2,980.00</span>
        </div>
      </div>
      <div class="min-w-0 border-l border-toolbar pr-8 pl-12">
        <div class="truncate text-12/16 text-body">广告销售额</div>
        <div
          class="mt-5 text-20/26 font-medium tracking-[-0.2px] whitespace-nowrap text-ink tabular-nums"
        >
          $18,420.00
        </div>
        <div class="mt-3 text-12/16 whitespace-nowrap text-placeholder">
          昨 <span class="text-muted">$16,530.00</span>
        </div>
      </div>
      <div class="min-w-0 border-l border-toolbar pr-8 pl-12">
        <div class="truncate text-12/16 text-body">广告订单量</div>
        <div
          class="mt-5 text-20/26 font-medium tracking-[-0.2px] whitespace-nowrap text-ink tabular-nums"
        >
          386
        </div>
        <div class="mt-3 text-12/16 whitespace-nowrap text-placeholder">
          昨 <span class="text-muted">342</span>
        </div>
      </div>
      <div class="min-w-0 border-l border-toolbar pr-8 pl-12">
        <div class="truncate text-12/16 text-body">ACOS</div>
        <div
          class="mt-5 text-20/26 font-medium tracking-[-0.2px] whitespace-nowrap text-ink tabular-nums"
        >
          17.7%
        </div>
        <div class="mt-3 text-12/16 whitespace-nowrap text-placeholder">
          昨 <span class="text-muted">18.0%</span>
        </div>
      </div>
      <div class="min-w-0 border-l border-toolbar pr-8 pl-12">
        <div class="truncate text-12/16 text-body">TACOS</div>
        <div
          class="mt-5 text-20/26 font-medium tracking-[-0.2px] whitespace-nowrap text-ink tabular-nums"
        >
          7.3%
        </div>
        <div class="mt-3 text-12/16 whitespace-nowrap text-placeholder">
          昨 <span class="text-muted">7.8%</span>
        </div>
      </div>
    </div>
  </AppCard>
</template>
