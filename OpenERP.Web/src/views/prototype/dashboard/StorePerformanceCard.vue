<script setup lang="ts">
import AppCard from '../../../ui/AppCard.vue'
import AppIcon from '../../../ui/AppIcon.vue'
</script>

<template>
  <AppCard data-testid="dashboard-card-store-performance" class="col-span-3 flex h-400 flex-col">
    <div class="flex h-54 flex-none items-start justify-between gap-12 px-16 pt-16">
      <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
        店铺绩效
      </div>
      <button
        type="button"
        class="inline-flex items-center gap-4 text-12 whitespace-nowrap text-primary hover:text-primary-hover"
      >
        详情<AppIcon name="i-chev-right" :size="12" />
      </button>
    </div>
    <div class="min-h-0 flex-1 px-16 pb-16">
      <div class="mb-8 text-12 text-muted">报表日期：2026-11-11</div>
      <div class="mt-2 mb-12 grid grid-cols-2 gap-8">
        <div class="rounded-4 border border-line-soft bg-page-soft px-10 py-8">
          <label class="block text-11/[16.5px] text-muted">异常店铺数</label>
          <strong class="mt-1 block text-20/26 font-medium text-ink tabular-nums">2</strong>
        </div>
        <div class="rounded-4 border border-line-soft bg-page-soft px-10 py-8">
          <label class="block text-11/[16.5px] text-muted">平均目标达成</label>
          <strong class="mt-1 block text-20/26 font-medium text-ink tabular-nums">96.4%</strong>
        </div>
      </div>
      <table class="w-full table-fixed border-collapse">
        <thead>
          <tr>
            <th
              scope="col"
              class="h-32 border-b border-line-soft bg-page-soft px-8 text-left text-12 font-normal text-muted"
            >
              店铺
            </th>
            <th
              scope="col"
              class="h-32 border-b border-line-soft bg-page-soft px-8 text-right text-12 font-normal text-muted"
            >
              异常
            </th>
            <th
              scope="col"
              class="h-32 border-b border-line-soft bg-page-soft px-8 text-right text-12 font-normal text-muted"
            >
              目标值
            </th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td class="h-42 border-b border-line-soft px-8 text-12 text-body">
              <span class="flex min-w-0 items-center gap-6"
                ><i class="size-6 flex-none rounded-full bg-primary"></i>Aurora US</span
              >
            </td>
            <td
              class="h-42 border-b border-line-soft px-8 text-right text-12 text-body tabular-nums"
            >
              1
            </td>
            <td
              class="h-42 border-b border-line-soft px-8 text-right text-12 text-body tabular-nums"
            >
              95%
            </td>
          </tr>
          <tr>
            <td class="h-42 border-b border-line-soft px-8 text-12 text-body">
              <span class="flex min-w-0 items-center gap-6"
                ><i class="size-6 flex-none rounded-full bg-chart-2"></i>NovaTech DE</span
              >
            </td>
            <td
              class="h-42 border-b border-line-soft px-8 text-right text-12 text-body tabular-nums"
            >
              1
            </td>
            <td
              class="h-42 border-b border-line-soft px-8 text-right text-12 text-body tabular-nums"
            >
              97%
            </td>
          </tr>
          <tr>
            <td class="h-42 border-b border-line-soft px-8 text-12 text-body">
              <span class="flex min-w-0 items-center gap-6"
                ><i class="size-6 flex-none rounded-full bg-chart-3"></i>LumoLife JP</span
              >
            </td>
            <td
              class="h-42 border-b border-line-soft px-8 text-right text-12 text-body tabular-nums"
            >
              0
            </td>
            <td
              class="h-42 border-b border-line-soft px-8 text-right text-12 text-body tabular-nums"
            >
              94%
            </td>
          </tr>
          <tr>
            <td class="h-42 px-8 text-12 text-body">
              <span class="flex min-w-0 items-center gap-6"
                ><i class="size-6 flex-none rounded-full bg-chart-4"></i>PeakOut US</span
              >
            </td>
            <td class="h-42 px-8 text-right text-12 text-body tabular-nums">0</td>
            <td class="h-42 px-8 text-right text-12 text-body tabular-nums">93%</td>
          </tr>
        </tbody>
      </table>
    </div>
  </AppCard>
</template>
