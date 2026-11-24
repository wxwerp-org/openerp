<script setup lang="ts">
import AppCard from '../../../ui/AppCard.vue'
import AppIcon from '../../../ui/AppIcon.vue'
import AppTag from '../../../ui/AppTag.vue'
</script>

<template>
  <AppCard data-testid="dashboard-card-review-summary" class="col-span-3 flex h-400 flex-col">
    <div class="flex h-54 flex-none items-start justify-between gap-12 px-16 pt-16">
      <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
        评论统计
      </div>
      <AppTag tone="green">近30天</AppTag>
    </div>
    <div class="min-h-0 flex-1 px-16 pb-16">
      <div class="mb-8 flex items-center gap-12 border-b border-line-soft pt-2 pb-12">
        <div>
          <div class="text-20/26 font-medium text-ink tabular-nums">4.8</div>
          <div class="mt-3 flex items-center gap-2 text-star">
            <AppIcon name="i-star" :size="12" class="fill-current stroke-none [stroke-width:1.2]" />
            <AppIcon name="i-star" :size="12" class="fill-current stroke-none [stroke-width:1.2]" />
            <AppIcon name="i-star" :size="12" class="fill-current stroke-none [stroke-width:1.2]" />
            <AppIcon name="i-star" :size="12" class="fill-current stroke-none [stroke-width:1.2]" />
            <AppIcon name="i-star" :size="12" class="fill-current stroke-none [stroke-width:1.2]" />
          </div>
          <div class="mt-2 text-11/[16.5px] text-muted">共 1,286 条评论</div>
        </div>
        <div class="ml-auto text-right text-11/18 text-muted">
          <span class="text-12 text-success">+0.2</span><br />较上月
        </div>
      </div>
      <div class="flex flex-col gap-5">
        <div class="grid grid-cols-[24px_1fr_28px] items-center gap-8 text-11/[16.5px] text-muted">
          <span>5 星</span>
          <div class="h-6 overflow-hidden rounded-3 bg-progress-track">
            <span class="block h-full rounded-3 bg-chart-line-alt" style="width: 82%"></span>
          </div>
          <b class="text-right font-normal text-body">82%</b>
        </div>
        <div class="grid grid-cols-[24px_1fr_28px] items-center gap-8 text-11/[16.5px] text-muted">
          <span>4 星</span>
          <div class="h-6 overflow-hidden rounded-3 bg-progress-track">
            <span class="block h-full rounded-3 bg-chart-line-alt" style="width: 12%"></span>
          </div>
          <b class="text-right font-normal text-body">12%</b>
        </div>
        <div class="grid grid-cols-[24px_1fr_28px] items-center gap-8 text-11/[16.5px] text-muted">
          <span>3 星</span>
          <div class="h-6 overflow-hidden rounded-3 bg-progress-track">
            <span class="block h-full rounded-3 bg-chart-line-alt" style="width: 3.5%"></span>
          </div>
          <b class="text-right font-normal text-body">4%</b>
        </div>
        <div class="grid grid-cols-[24px_1fr_28px] items-center gap-8 text-11/[16.5px] text-muted">
          <span>2 星</span>
          <div class="h-6 overflow-hidden rounded-3 bg-progress-track">
            <span class="block h-full rounded-3 bg-chart-line-alt" style="width: 1.5%"></span>
          </div>
          <b class="text-right font-normal text-body">1%</b>
        </div>
        <div class="grid grid-cols-[24px_1fr_28px] items-center gap-8 text-11/[16.5px] text-muted">
          <span>1 星</span>
          <div class="h-6 overflow-hidden rounded-3 bg-progress-track">
            <span class="block h-full rounded-3 bg-chart-line-alt" style="width: 1%"></span>
          </div>
          <b class="text-right font-normal text-body">1%</b>
        </div>
      </div>
      <div class="mt-10 rounded-4 bg-page-soft px-10 py-8 text-11/17 text-muted">
        <strong class="font-medium text-body">买家反馈高频词：</strong>质量好 · 发货快 · 便携 ·
        包装精致
      </div>
    </div>
  </AppCard>
</template>
