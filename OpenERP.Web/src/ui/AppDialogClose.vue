<script setup lang="ts">
import AppIcon from './AppIcon.vue'

withDefaults(
  defineProps<{
    /** 关闭按钮 id：旧页面/测试用 `#closeProfile` 等挂点。 */
    id?: string
    disabled?: boolean
  }>(),
  {
    id: undefined,
    disabled: false,
  },
)

const emit = defineEmits<{
  click: []
}>()
</script>

<template>
  <button
    :id="id"
    type="button"
    :disabled="disabled"
    title="关闭"
    aria-label="关闭"
    class="flex h-24 w-26 shrink-0 items-center justify-center rounded-3 text-body hover:bg-page hover:text-ink"
    @click="emit('click')"
  >
    <AppIcon name="i-close" :size="14" />
  </button>
</template>
