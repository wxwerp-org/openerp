<!--
  长文本折叠：默认最多显示 3 行，超出时出现「展开 / 收起」；内容不超过行数时不显示按钮。
  订单详情等页面里名称、补充信息这类可能很长的文字统一用它，避免撑出大量换行。
-->
<template>
  <div class="app-clamp">
    <div
      ref="bodyRef"
      class="app-clamp__body"
      :class="{ 'is-clamped': !open }"
      :style="{ '--clamp-lines': lines }"
    >
      <slot />
    </div>
    <ElButton v-if="overflow" link type="primary" size="small" @click="open = !open">
      {{ open ? '收起' : '展开' }}
    </ElButton>
  </div>
</template>

<script setup lang="ts">
  defineOptions({ name: 'AppClamp' })

  withDefaults(defineProps<{ lines?: number }>(), { lines: 3 })

  const bodyRef = ref<HTMLElement | null>(null)
  const open = ref(false)
  const overflow = ref(false)

  // 折叠状态下用实际高度判断是否被截断；展开后保持按钮可见，以便再次收起
  function measure() {
    const el = bodyRef.value
    if (el && !open.value) overflow.value = el.scrollHeight > el.clientHeight + 1
  }

  useResizeObserver(bodyRef, measure)
  onMounted(measure)
</script>

<style scoped>
  .app-clamp {
    min-width: 0;
  }

  .app-clamp__body {
    overflow-wrap: anywhere;
  }

  .app-clamp__body.is-clamped {
    display: -webkit-box;
    overflow: hidden;
    -webkit-line-clamp: var(--clamp-lines, 3);
    -webkit-box-orient: vertical;
  }
</style>
