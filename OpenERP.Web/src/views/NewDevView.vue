<script setup lang="ts">
import AppIcon from '../ui/AppIcon.vue'
import AppTag from '../ui/AppTag.vue'
</script>

<template>
  <section id="page-newdev" class="flex h-full min-h-0 w-full flex-col bg-page">
    <div class="min-h-full w-full bg-page p-16">
      <div
        class="flex items-start justify-between rounded-6 border border-line bg-white px-24 py-20"
      >
        <div>
          <h1 class="text-20/28 font-semibold text-ink">新品开发</h1>
          <p class="mt-4 text-12/18 text-muted">从选品调研到立项跟进，保持与业务同一套视觉规范。</p>
        </div>
        <AppTag tone="blue">原型页签</AppTag>
      </div>
      <div
        class="mt-12 flex min-h-440 flex-col items-center justify-center gap-8 rounded-6 border border-line bg-white"
      >
        <AppIcon name="i-box" :size="38" class="text-empty-icon-muted [stroke-width:1.2]" />
        <strong class="text-14/[21px] font-semibold text-body">新品开发模块</strong>
        <span class="text-12/18 text-muted">
          当前原型聚焦首页、产品、订单与设置，此处仅保留页签占位。
        </span>
      </div>
    </div>
  </section>
</template>
