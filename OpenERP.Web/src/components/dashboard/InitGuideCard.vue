<script setup lang="ts">
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'
</script>

<template>
  <AppCard data-testid="dashboard-card-init" class="col-span-12 flex h-141 flex-col">
    <div class="flex flex-none items-start justify-between gap-12 px-16 pt-16">
      <div class="flex min-w-0 items-center gap-8">
        <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
          初始化引导<AppIcon
            name="i-chev-right"
            :size="12"
            class="text-title-caret [stroke-width:1.8]"
          />
        </div>
      </div>
      <button
        type="button"
        class="inline-flex h-28 items-center gap-4 text-12 whitespace-nowrap text-body hover:text-primary"
      >
        收起<AppIcon name="i-chev-down" :size="12" class="rotate-180" />
      </button>
    </div>
    <div class="mx-16 mt-9 mb-16 flex h-78 min-w-0 gap-4">
      <div
        class="guide-step flex h-78 min-w-0 flex-1 items-center bg-page pr-20 pl-26 hover:bg-init-step-hover first:pl-16"
      >
        <svg class="mr-10 size-36 flex-none text-primary"><use href="#i-guide-shield" /></svg>
        <div class="min-w-0">
          <div class="flex items-center gap-4 text-14/20 font-bold whitespace-nowrap text-ink">
            基础数据授权
            <span
              class="h-16 rounded-2 bg-init-must-bg px-4 text-11/16 font-normal text-init-must-text"
              >必做</span
            >
          </div>
          <div class="mt-2 line-clamp-2 max-w-210 text-12/18 text-body">
            授权店铺客服邮箱等信息，开启数字化管理
          </div>
        </div>
      </div>
      <div
        class="guide-step flex h-78 min-w-0 flex-1 items-center bg-page pr-20 pl-26 hover:bg-init-step-hover"
      >
        <svg class="mr-10 size-36 flex-none text-primary"><use href="#i-guide-account" /></svg>
        <div class="min-w-0">
          <div class="flex items-center gap-4 text-14/20 font-bold whitespace-nowrap text-ink">
            账号权限配置
          </div>
          <div class="mt-2 line-clamp-2 max-w-210 text-12/18 text-body">
            分配具体账号权限，便于内部员工协作
          </div>
        </div>
      </div>
      <div
        class="guide-step flex h-78 min-w-0 flex-1 items-center bg-page pr-20 pl-26 hover:bg-init-step-hover"
      >
        <svg class="mr-10 size-36 flex-none text-primary"><use href="#i-guide-product" /></svg>
        <div class="min-w-0">
          <div class="flex items-center gap-4 text-14/20 font-bold whitespace-nowrap text-ink">
            产品信息维护
          </div>
          <div class="mt-2 line-clamp-2 max-w-210 text-12/18 text-body">
            录入本地产品并配对平台商品，实现数据匹配
          </div>
        </div>
      </div>
      <div
        class="guide-step flex h-78 min-w-0 flex-1 items-center bg-page pr-20 pl-26 hover:bg-init-step-hover"
      >
        <svg class="mr-10 size-36 flex-none text-primary"><use href="#i-guide-supply" /></svg>
        <div class="min-w-0">
          <div class="flex items-center gap-4 text-14/20 font-bold whitespace-nowrap text-ink">
            供应链管理
          </div>
          <div class="mt-2 line-clamp-2 max-w-210 text-12/18 text-body">
            各关键环节于一体，高效管理
          </div>
        </div>
      </div>
      <div
        class="guide-step flex h-78 min-w-0 flex-1 items-center bg-page pr-20 pl-28 hover:bg-init-step-hover"
      >
        <svg class="mr-8 size-30 flex-none text-init-more"><use href="#i-guide-more" /></svg>
        <div class="min-w-0">
          <div class="flex items-center gap-4 text-14/20 font-bold whitespace-nowrap text-ink">
            更多进阶功能
          </div>
          <div class="mt-2 line-clamp-2 max-w-210 text-12/18 text-body">
            智能补货建议、业财一体化管理等亮点功能
          </div>
        </div>
      </div>
    </div>
  </AppCard>
</template>

<style scoped>
/* 旧 .init-step 的箭头切角形状无法用单个工具类表达，仅保留这条几何规则；颜色与尺寸走工具类。 */
.guide-step {
  clip-path: polygon(0 0, calc(100% - 17px) 0, 100% 50%, calc(100% - 17px) 100%, 0 100%, 17px 50%);
}

.guide-step:first-child {
  clip-path: polygon(0 0, calc(100% - 17px) 0, 100% 50%, calc(100% - 17px) 100%, 0 100%);
}
</style>
