<script setup lang="ts">
import AppCard from '../../../ui/AppCard.vue'
import AppIcon from '../../../ui/AppIcon.vue'
</script>

<template>
  <AppCard data-testid="dashboard-card-todo" class="col-span-6 flex h-400 flex-col">
    <div class="flex h-54 flex-none items-start justify-between gap-12 px-16 pt-16">
      <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
        待处理事项<AppIcon
          name="i-chev-right"
          :size="12"
          class="text-title-caret [stroke-width:1.8]"
        />
      </div>
      <div class="flex min-w-0 flex-nowrap items-center gap-10 text-muted">
        <span class="text-11/[16.5px]">更新于 15:47</span>
        <button
          type="button"
          title="刷新"
          aria-label="刷新"
          class="inline-flex size-28 items-center justify-center rounded-4 text-muted hover:bg-page hover:text-primary"
        >
          <AppIcon name="i-refresh" />
        </button>
      </div>
    </div>
    <div class="flex min-h-0 flex-1 flex-col px-16 pt-4 pb-16">
      <div class="mt-8">
        <div class="mb-8 flex items-center justify-between">
          <span class="text-14/20 font-bold text-black">运营</span>
          <button
            type="button"
            class="inline-flex items-center gap-4 text-12 whitespace-nowrap text-muted hover:text-primary"
          >
            查看全部<AppIcon name="i-chev-right" :size="12" />
          </button>
        </div>
        <div class="grid grid-cols-3 gap-8">
          <div
            class="flex h-56 min-w-0 items-center gap-8 rounded-4 border border-line-soft bg-page-soft px-10 hover:border-todo-hover-border hover:bg-white"
          >
            <span
              class="inline-flex size-24 flex-none items-center justify-center rounded-full bg-todo-warn-bg text-todo-warn"
            >
              <AppIcon name="i-warning" />
            </span>
            <span class="min-w-0 flex-1 truncate text-12 text-body">库存不足</span>
            <span class="flex-none text-14/20 font-semibold text-warning">6</span>
          </div>
          <div
            class="flex h-56 min-w-0 items-center gap-8 rounded-4 border border-line-soft bg-page-soft px-10 hover:border-todo-hover-border hover:bg-white"
          >
            <span
              class="inline-flex size-24 flex-none items-center justify-center rounded-full bg-white text-body"
            >
              <AppIcon name="i-box" />
            </span>
            <span class="min-w-0 flex-1 truncate text-12 text-body">库存冗余</span>
            <span class="flex-none text-14/20 font-semibold text-ink">4</span>
          </div>
          <div
            class="flex h-56 min-w-0 items-center gap-8 rounded-4 border border-line-soft bg-page-soft px-10 hover:border-todo-hover-border hover:bg-white"
          >
            <span
              class="inline-flex size-24 flex-none items-center justify-center rounded-full bg-danger-faint text-danger"
            >
              <AppIcon name="i-warning" />
            </span>
            <span class="min-w-0 flex-1 truncate text-12 text-body">被跟卖</span>
            <span class="flex-none text-14/20 font-semibold text-danger">2</span>
          </div>
          <div
            class="flex h-56 min-w-0 items-center gap-8 rounded-4 border border-line-soft bg-page-soft px-10 hover:border-todo-hover-border hover:bg-white"
          >
            <span
              class="inline-flex size-24 flex-none items-center justify-center rounded-full bg-todo-warn-bg text-todo-warn"
            >
              <AppIcon name="i-chev-down" />
            </span>
            <span class="min-w-0 flex-1 truncate text-12 text-body">丢失购物车</span>
            <span class="flex-none text-14/20 font-semibold text-warning">1</span>
          </div>
          <div
            class="flex h-56 min-w-0 items-center gap-8 rounded-4 border border-line-soft bg-page-soft px-10 hover:border-todo-hover-border hover:bg-white"
          >
            <span
              class="inline-flex size-24 flex-none items-center justify-center rounded-full bg-white text-body"
            >
              <AppIcon name="i-mail" />
            </span>
            <span class="min-w-0 flex-1 truncate text-12 text-body">待回复邮件</span>
            <span class="flex-none text-14/20 font-semibold text-ink">18</span>
          </div>
          <div
            class="flex h-56 min-w-0 items-center gap-8 rounded-4 border border-line-soft bg-page-soft px-10 hover:border-todo-hover-border hover:bg-white"
          >
            <span
              class="inline-flex size-24 flex-none items-center justify-center rounded-full bg-danger-faint text-danger"
            >
              <AppIcon name="i-file" />
            </span>
            <span class="min-w-0 flex-1 truncate text-12 text-body">发票上传失败</span>
            <span class="flex-none text-14/20 font-semibold text-danger">1</span>
          </div>
        </div>
      </div>
      <div class="mt-10 mb-4 h-px bg-line-soft"></div>
      <div class="mt-8">
        <div class="mb-8 flex items-center justify-between">
          <span class="text-14/20 font-bold text-black">审批</span>
          <button
            type="button"
            class="inline-flex items-center gap-4 text-12 whitespace-nowrap text-muted hover:text-primary"
          >
            查看全部<AppIcon name="i-chev-right" :size="12" />
          </button>
        </div>
        <div class="grid grid-cols-3 gap-8">
          <div
            class="flex h-56 min-w-0 items-center gap-8 rounded-4 border border-line-soft bg-page-soft px-10 hover:border-todo-hover-border hover:bg-white"
          >
            <span
              class="inline-flex size-24 flex-none items-center justify-center rounded-full bg-white text-body"
            >
              <AppIcon name="i-clipboard" />
            </span>
            <span class="min-w-0 flex-1 truncate text-12 text-body">待我处理</span>
            <span class="flex-none text-14/20 font-semibold text-ink">5</span>
          </div>
          <div
            class="flex h-56 min-w-0 items-center gap-8 rounded-4 border border-line-soft bg-page-soft px-10 hover:border-todo-hover-border hover:bg-white"
          >
            <span
              class="inline-flex size-24 flex-none items-center justify-center rounded-full bg-white text-body"
            >
              <AppIcon name="i-file" />
            </span>
            <span class="min-w-0 flex-1 truncate text-12 text-body">我发起</span>
            <span class="flex-none text-14/20 font-semibold text-ink">2</span>
          </div>
          <div
            class="flex h-56 min-w-0 items-center gap-8 rounded-4 border border-line-soft bg-page-soft px-10 hover:border-todo-hover-border hover:bg-white"
          >
            <span
              class="inline-flex size-24 flex-none items-center justify-center rounded-full bg-white text-body"
            >
              <AppIcon name="i-chev-left" />
            </span>
            <span class="min-w-0 flex-1 truncate text-12 text-body">已驳回</span>
            <span class="flex-none text-14/20 font-semibold text-ink">1</span>
          </div>
        </div>
      </div>
    </div>
  </AppCard>
</template>
