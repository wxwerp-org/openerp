<script setup lang="ts">
import { useNativeDialog } from '../composables/useNativeDialog'

const props = defineProps<{ open: boolean }>()
const dialog = useNativeDialog(() => props.open)
void dialog

const emit = defineEmits<{ close: [] }>()
</script>

<template>
  <dialog
    id="noticeLayer"
    ref="dialog"
    class="notice-layer"
    :class="{ open }"
    aria-labelledby="noticeTitle"
    @close="emit('close')"
  >
    <div id="noticeMask" class="notice-mask" @click="emit('close')"></div>
    <div class="notice-dialog" aria-labelledby="noticeTitle">
      <div id="noticeTitle" class="notice-header">【订单列表】优化公告</div>
      <div class="notice-body">
        <div class="notice-content">
          <span class="notice-tag">优化</span>
          <div>
            <div class="notice-item-title">商品图片支持配置取Listing图片 或 本地产品图片</div>
            <div class="notice-line">
              当前的设置为：优先显示本地图片图片，如需修改，请前往
              <a class="notice-link" href="#">业务配置&gt;基础&gt;图片显示</a>
            </div>
            <div class="notice-note">
              *此配置同时影响：订单列表 / 售后订单 / 首页 / 销量分析 / 销量统计 / 产品表现 / ASIN360
            </div>
          </div>
        </div>
      </div>
      <div class="notice-footer">
        <button id="closeNotice" class="notice-ok" type="button" @click="emit('close')">
          知道了
        </button>
      </div>
    </div>
  </dialog>
</template>
