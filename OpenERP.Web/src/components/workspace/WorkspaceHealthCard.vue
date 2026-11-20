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
  <AppCard
    class="flex h-full min-h-120 flex-col px-16 pb-14 pt-14"
    data-testid="workspace-health-card"
  >
    <div class="flex flex-none items-center justify-between">
      <h2 class="text-13/20 font-semibold text-ink">授权健康</h2>
      <button
        type="button"
        class="flex items-center gap-2 text-11 text-primary hover:underline"
        @click="emit('open')"
      >
        授权店铺<AppIcon name="i-chev-right" :size="10" />
      </button>
    </div>

    <div v-if="status === 'error'" class="mt-12 flex flex-1 flex-col items-start gap-8 text-12">
      <span class="text-danger">{{ error }}</span>
      <AppButton size="md" @click="emit('retry')">重试</AppButton>
    </div>

    <template v-else>
      <div class="mt-12 grid grid-cols-3 gap-8 text-center">
        <div>
          <div class="text-16 font-semibold tabular-nums text-success">
            {{ summary ? summary.valid : '—' }}
          </div>
          <div class="text-11 text-muted">有效</div>
        </div>
        <div>
          <div class="text-16 font-semibold tabular-nums text-danger">
            {{ summary ? summary.invalid : '—' }}
          </div>
          <div class="text-11 text-muted">无效</div>
        </div>
        <div>
          <div class="text-16 font-semibold tabular-nums text-body-strong">
            {{ summary ? summary.unknown : '—' }}
          </div>
          <div class="text-11 text-muted">未知</div>
        </div>
      </div>
      <div class="mt-10 flex items-center justify-between gap-8">
        <span class="text-11 text-muted">
          最近检查 {{ formatDateTime(summary?.lastSyncedAt ?? null) }}
        </span>
        <AppButton size="md" :disabled="running" @click="emit('sync')">
          {{ running ? '同步中…' : '去同步' }}
        </AppButton>
      </div>
    </template>
  </AppCard>
</template>
