<!--
  页面提示：小字号、小图标、带边框和圆角，作为独立的一块呈现。
  业务页统一用它，不再使用 ElAlert（见 docs/development.md「页面布局与间距规范」）。

  <AppNotice tone="warning">尚未配置上游 ERP 连接。<template #action><RouterLink to="/x">去设置</RouterLink></template></AppNotice>
-->
<template>
  <div class="app-notice" :class="`is-${tone}`" :role="tone === 'error' ? 'alert' : 'status'">
    <ArtSvgIcon :icon="icons[tone]" class="app-notice__icon" />
    <div class="app-notice__body"><slot /></div>
    <div v-if="$slots.action" class="app-notice__action"><slot name="action" /></div>
  </div>
</template>

<script setup lang="ts">
  defineOptions({ name: 'AppNotice' })

  withDefaults(defineProps<{ tone?: 'info' | 'success' | 'warning' | 'error' }>(), {
    tone: 'info'
  })

  const icons = {
    info: 'ri:information-line',
    success: 'ri:checkbox-circle-line',
    warning: 'ri:error-warning-line',
    error: 'ri:close-circle-line'
  } as const
</script>

<style scoped>
  .app-notice {
    --notice-color: var(--el-color-info);
    --notice-bg: var(--el-color-info-light-9);
    --notice-border: var(--el-color-info-light-7);

    display: flex;
    gap: 6px;
    align-items: center;
    padding: 5px 10px;
    font-size: 12px;
    line-height: 18px;
    color: var(--el-text-color-regular);
    background: var(--notice-bg);
    border: 1px solid var(--notice-border);
    border-radius: 6px;
  }

  .app-notice.is-success {
    --notice-color: var(--el-color-success);
    --notice-bg: var(--el-color-success-light-9);
    --notice-border: var(--el-color-success-light-7);
  }

  .app-notice.is-warning {
    --notice-color: var(--el-color-warning);
    --notice-bg: var(--el-color-warning-light-9);
    --notice-border: var(--el-color-warning-light-7);
  }

  .app-notice.is-error {
    --notice-color: var(--el-color-danger);
    --notice-bg: var(--el-color-danger-light-9);
    --notice-border: var(--el-color-danger-light-7);
  }

  .app-notice__icon {
    flex: none;
    font-size: 14px;
    color: var(--notice-color);
  }

  .app-notice__body {
    flex: 1;
    min-width: 0;
    overflow-wrap: anywhere;
  }

  .app-notice__action {
    flex: none;
  }

  .app-notice__action :deep(a),
  .app-notice__action :deep(.el-button) {
    font-size: 12px;
    color: var(--el-color-primary);
  }
</style>
