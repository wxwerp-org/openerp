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
  <div
    class="flex min-h-42 flex-none flex-wrap items-center gap-x-12 gap-y-2 border-b border-line bg-white px-16 py-4"
    data-testid="workspace-status-bar"
  >
    <span class="whitespace-nowrap text-12 text-muted">查询时间</span>
    <span class="text-12 tabular-nums text-body-strong" data-testid="report-generated-at">
      {{ formatDateTime(generatedAt) }}
    </span>
    <span
      v-if="stale"
      class="rounded-2 bg-notice-amber-bg px-6 py-1 text-11 text-notice-amber-text"
      role="status"
      data-testid="report-stale"
    >
      刷新失败，显示上次结果
    </span>
    <template v-if="showDirectory">
      <span class="hidden h-14 w-px bg-line sm:block"></span>
      <span class="whitespace-nowrap text-12 text-muted">目录最近同步</span>
      <span class="text-12 tabular-nums text-body-strong" data-testid="directory-synced-at">
        {{ formatDateTime(lastSyncedAt) }}
      </span>
    </template>
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
