<script setup lang="ts">
import AppButton from '../../ui/AppButton.vue'
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'
</script>

<template>
  <AppCard data-testid="dashboard-card-operations-todo" class="col-span-6 flex h-400 flex-col">
    <div class="flex h-54 flex-none items-start justify-between px-16 pt-16">
      <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
        运营待办
      </div>
      <div class="flex items-center gap-10">
        <label class="flex items-center gap-6 whitespace-nowrap text-body"
          ><input type="checkbox" class="size-[13px] accent-primary" />仅看自己</label
        >
        <button
          type="button"
          class="flex h-28 min-w-120 items-center justify-between rounded-4 border border-input bg-white px-10 text-12 text-ink"
        >
          <span>2026 第 46 周</span>
          <AppIcon name="i-calendar" class="text-title-caret" />
        </button>
      </div>
    </div>
    <div class="mx-16 mt-2 grid grid-cols-7 border-b border-line-soft">
      <div
        class="flex h-46 cursor-pointer flex-col items-center justify-center gap-2 text-muted hover:bg-row-soft"
      >
        <span class="text-11/[16.5px]">日</span>
        <span class="text-12 text-body">11-08</span>
      </div>
      <div
        class="flex h-46 cursor-pointer flex-col items-center justify-center gap-2 text-muted hover:bg-row-soft"
      >
        <span class="text-11/[16.5px]">一</span>
        <span class="text-12 text-body">11-09</span>
      </div>
      <div
        class="flex h-46 cursor-pointer flex-col items-center justify-center gap-2 text-muted hover:bg-row-soft"
      >
        <span class="text-11/[16.5px]">二</span>
        <span class="text-12 text-body">11-10</span>
      </div>
      <div
        class="flex h-46 cursor-pointer flex-col items-center justify-center gap-2 text-muted hover:bg-row-soft"
      >
        <span class="text-11/[16.5px]">三</span>
        <span class="text-12 text-body">11-11</span>
      </div>
      <div
        class="flex h-46 cursor-pointer flex-col items-center justify-center gap-2 border-b-2 border-primary bg-week-active-bg text-primary"
      >
        <span class="text-11/[16.5px]">四</span>
        <span class="text-12 font-semibold text-primary">11-12</span>
      </div>
      <div
        class="flex h-46 cursor-pointer flex-col items-center justify-center gap-2 text-muted hover:bg-row-soft"
      >
        <span class="text-11/[16.5px]">五</span>
        <span class="text-12 text-body">11-13</span>
      </div>
      <div
        class="flex h-46 cursor-pointer flex-col items-center justify-center gap-2 text-muted hover:bg-row-soft"
      >
        <span class="text-11/[16.5px]">六</span>
        <span class="text-12 text-body">11-14</span>
      </div>
    </div>
    <div
      data-testid="operations-todo-empty"
      class="flex min-h-0 flex-1 flex-col items-center justify-center gap-8 text-muted"
    >
      <AppIcon name="i-clipboard" :size="36" class="text-empty-icon-muted [stroke-width:1.2]" />
      <span>暂无数据</span>
    </div>
    <div class="grid grid-cols-2 gap-16 px-16 pb-16">
      <AppButton size="lg">新建待办</AppButton>
      <AppButton size="lg">写日志</AppButton>
    </div>
  </AppCard>
</template>
