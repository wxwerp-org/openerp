<script setup lang="ts">
import AppButton from '../../ui/AppButton.vue'
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'
import type { ShopDirectorySummary } from '../../workspace/workspaceData'
import { formatDateTime } from '../../workspace/workspaceData'

defineProps<{
  summary: ShopDirectorySummary | null
  status: 'idle' | 'loading' | 'success' | 'error'
  error: string
  running: boolean
}>()

const emit = defineEmits<{ sync: []; open: []; retry: [] }>()
</script>

<template>
  <AppCard class="flex h-full min-h-120 flex-col" data-testid="workspace-health-card">
    <div class="flex h-54 flex-none items-start justify-between gap-12 px-16 pt-16">
      <button
        type="button"
        class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink hover:text-primary"
        @click="emit('open')"
      >
        授权健康<AppIcon
          name="i-chev-right"
          :size="12"
          class="text-title-caret [stroke-width:1.8]"
        />
      </button>
      <span class="text-12 whitespace-nowrap text-muted">本地目录</span>
    </div>

    <div
      v-if="status === 'error'"
      class="flex flex-1 flex-col items-start gap-8 px-16 pb-16 text-12"
    >
      <span class="text-danger">{{ error }}</span>
      <AppButton size="md" @click="emit('retry')">重试</AppButton>
    </div>

    <template v-else>
      <div class="grid grid-cols-3 gap-8 px-16 pt-6 text-center">
        <div>
          <div class="text-20/26 font-medium tabular-nums text-success">
            {{ summary ? summary.valid : '—' }}
          </div>
          <div class="mt-2 text-12/16 text-muted">有效</div>
        </div>
        <div>
          <div class="text-20/26 font-medium tabular-nums text-danger">
            {{ summary ? summary.invalid : '—' }}
          </div>
          <div class="mt-2 text-12/16 text-muted">无效</div>
        </div>
        <div>
          <div class="text-20/26 font-medium tabular-nums text-body-strong">
            {{ summary ? summary.unknown : '—' }}
          </div>
          <div class="mt-2 text-12/16 text-muted">未知</div>
        </div>
      </div>
      <div class="mt-auto flex items-center justify-between gap-8 px-16 pt-10 pb-16">
        <span class="text-12/16 text-muted">
          最近检查 {{ formatDateTime(summary?.lastSyncedAt ?? null) }}
        </span>
        <AppButton size="md" :disabled="running" @click="emit('sync')">
          {{ running ? '同步中…' : '去同步' }}
        </AppButton>
      </div>
    </template>
  </AppCard>
</template>
