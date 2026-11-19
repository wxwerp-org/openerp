<script setup lang="ts">
import { useRouter } from 'vue-router'
import AppButton from '../ui/AppButton.vue'
import AppIcon from '../ui/AppIcon.vue'
import AppTag from '../ui/AppTag.vue'

const router = useRouter()

function goHome(): void {
  void router.push('/')
}
</script>

<template>
  <section class="flex h-full min-h-0 w-full flex-col overflow-auto bg-page">
    <div class="min-h-full w-full bg-page p-16">
      <div
        class="flex items-start justify-between rounded-6 border border-line bg-white px-24 py-20"
      >
        <div>
          <h1 class="text-20/28 font-semibold text-ink">页面不存在</h1>
          <p class="mt-4 text-12/18 text-muted">地址可能有误，或页面已被移除。</p>
        </div>
        <AppTag tone="gray">404</AppTag>
      </div>
      <div
        class="mt-12 flex min-h-440 flex-col items-center justify-center gap-8 rounded-6 border border-line bg-white"
      >
        <AppIcon name="i-warning" :size="38" class="text-empty-icon-muted [stroke-width:1.2]" />
        <strong class="text-14/[21px] font-semibold text-body">找不到这个页面</strong>
        <span class="text-12/18 text-muted">请检查链接，或返回首页继续操作。</span>
        <AppButton variant="primary" type="button" @click="goHome">返回首页</AppButton>
      </div>
    </div>
  </section>
</template>
