<script setup lang="ts">
import AppDialog from '../ui/AppDialog.vue'

defineProps<{ open: boolean }>()

const emit = defineEmits<{ close: [] }>()
</script>

<template>
  <AppDialog
    id="noticeLayer"
    :open="open"
    layer="notice"
    backdrop="none"
    aria-labelledby="noticeTitle"
    class="bg-white"
    @close="emit('close')"
  >
    <div id="noticeMask" class="absolute inset-0 bg-mask-overlay" @click="emit('close')"></div>
    <div
      class="absolute left-1/2 top-120 w-700 -translate-x-1/2 overflow-hidden rounded-4 bg-white shadow-notice"
    >
      <div
        id="noticeTitle"
        class="flex h-56 items-center bg-dialog-notice-header px-24 text-24/56 font-bold text-white"
      >
        【订单列表】优化公告
      </div>
      <div class="h-180 overflow-hidden p-24 text-12/22 text-body-strong">
        <div class="flex items-start gap-12">
          <span class="h-18 flex-none rounded-2 border border-success px-4 text-12/16 text-success"
            >优化</span
          >
          <div>
            <div class="text-14/22 font-bold text-ink">
              商品图片支持配置取Listing图片 或 本地产品图片
            </div>
            <div class="mt-4 text-12/22 text-body-strong">
              当前的设置为：优先显示本地图片图片，如需修改，请前往
              <a class="text-primary underline underline-offset-2" href="#"
                >业务配置&gt;基础&gt;图片显示</a
              >
            </div>
            <div class="mt-4 text-12/22 text-placeholder">
              *此配置同时影响：订单列表 / 售后订单 / 首页 / 销量分析 / 销量统计 / 产品表现 / ASIN360
            </div>
          </div>
        </div>
      </div>
      <div
        class="flex items-center justify-center border-t border-dialog-notice-footer-line px-20 py-24"
      >
        <button
          id="closeNotice"
          type="button"
          class="inline-flex h-32 min-w-62 shrink-0 items-center justify-center rounded-4 border border-primary bg-white px-12 text-12/18 text-primary hover:bg-dialog-notice-btn-hover"
          @click="emit('close')"
        >
          知道了
        </button>
      </div>
    </div>
  </AppDialog>
</template>
