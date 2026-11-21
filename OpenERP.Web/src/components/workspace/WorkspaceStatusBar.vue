<script setup lang="ts">
import AppIcon from '../../ui/AppIcon.vue'
import AppIconButton from '../../ui/AppIconButton.vue'
import { formatDateTime } from '../../workspace/workspaceData'

defineProps<{
  generatedAt: string | null
  lastSyncedAt: string | null
  showDirectory: boolean
  refreshing: boolean
  stale: boolean
}>()

const emit = defineEmits<{ refresh: [] }>()
</script>

<template>
  <!-- 原型顶栏形态：只放真实信息，不出现假筛选、假时钟等控件。 -->
  <div
    class="relative z-[5] flex min-h-52 min-w-0 flex-none flex-wrap items-center gap-x-8 gap-y-2 bg-white px-20 shadow-header"
    data-testid="workspace-status-bar"
  >
    <span
      class="inline-flex h-28 items-center gap-5 rounded-4 bg-page px-10 text-12 whitespace-nowrap"
    >
      <span class="text-muted">查询时间</span>
      <span class="tabular-nums text-ink" data-testid="report-generated-at">
        {{ formatDateTime(generatedAt) }}
      </span>
    </span>
    <template v-if="showDirectory">
      <span class="mx-2 h-16 w-px flex-none bg-divider"></span>
      <span
        class="inline-flex h-28 items-center gap-5 rounded-4 bg-page px-10 text-12 whitespace-nowrap"
      >
        <span class="text-muted">目录最近同步</span>
        <span class="tabular-nums text-ink" data-testid="directory-synced-at">
          {{ formatDateTime(lastSyncedAt) }}
        </span>
      </span>
    </template>
    <span
      v-if="stale"
      class="rounded-4 bg-notice-amber-bg px-8 py-2 text-11 text-notice-amber-text"
      role="status"
      data-testid="report-stale"
    >
      刷新失败，显示上次结果
    </span>
    <div class="ml-auto flex items-center gap-4">
      <span v-if="refreshing" class="text-11 text-muted">刷新中</span>
      <AppIconButton
        label="刷新工作台"
        tone="toolbar"
        data-testid="workspace-refresh"
        @click="emit('refresh')"
      >
        <AppIcon name="i-refresh" />
      </AppIconButton>
    </div>
  </div>
</template>
