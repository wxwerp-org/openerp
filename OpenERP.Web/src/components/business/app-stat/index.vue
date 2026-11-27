<!--
  紧凑统计块：一行标签 + 一行数值 + 可选说明，放进 .page-grid 等分排列。
  业务页统一用它展示汇总数字，不再用 ElCard 自制大卡片。
-->
<template>
  <div class="app-stat">
    <div class="app-stat__label">{{ label }}</div>
    <div class="app-stat__value" :class="tone === 'default' ? '' : `is-${tone}`">
      <slot>{{ value }}</slot>
    </div>
    <div v-if="hint || $slots.hint" class="app-stat__hint">
      <slot name="hint">{{ hint }}</slot>
    </div>
  </div>
</template>

<script setup lang="ts">
  defineOptions({ name: 'AppStat' })

  withDefaults(
    defineProps<{
      label: string
      value?: string | number
      hint?: string
      tone?: 'default' | 'success' | 'danger'
    }>(),
    { value: '—', hint: '', tone: 'default' }
  )
</script>

<style scoped>
  .app-stat {
    min-width: 0;
    padding: 8px 12px;
    background: var(--default-box-color);
    border: 1px solid var(--el-border-color-lighter);
    border-radius: 6px;
  }

  .app-stat__label {
    font-size: 12px;
    line-height: 18px;
    color: var(--el-text-color-secondary);
  }

  .app-stat__value {
    overflow: hidden;
    font-size: 18px;
    font-weight: 600;
    line-height: 26px;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .app-stat__value.is-success {
    color: var(--el-color-success);
  }

  .app-stat__value.is-danger {
    color: var(--el-color-danger);
  }

  .app-stat__hint {
    overflow: hidden;
    font-size: 12px;
    line-height: 18px;
    color: var(--el-text-color-placeholder);
    text-overflow: ellipsis;
    white-space: nowrap;
  }
</style>
