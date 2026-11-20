<script setup lang="ts">
import AppButton from '../../ui/AppButton.vue'
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'

defineProps<{
  configured: boolean | null
  orderSyncEnabled: boolean | null
  status: 'idle' | 'loading' | 'success' | 'error'
  error: string
}>()

const emit = defineEmits<{ open: []; retry: [] }>()

function statusLabel(value: boolean | null, yes: string, no: string): string {
  if (value === null) return '—'
  return value ? yes : no
}
</script>

<template>
  <AppCard
    class="flex h-full min-h-120 flex-col px-16 pb-14 pt-14"
    data-testid="workspace-system-card"
  >
    <div class="flex flex-none items-center justify-between">
      <h2 class="text-13/20 font-semibold text-ink">系统状态</h2>
      <button
        type="button"
        class="flex items-center gap-2 text-11 text-primary hover:underline"
        @click="emit('open')"
      >
        ERP 接入<AppIcon name="i-chev-right" :size="10" />
      </button>
    </div>
    <div v-if="status === 'error'" class="mt-12 flex flex-1 flex-col items-start gap-8 text-12">
      <span class="text-danger">{{ error }}</span>
      <AppButton size="md" @click="emit('retry')">重试</AppButton>
    </div>
    <dl v-else class="m-0 mt-12 flex flex-1 flex-col gap-10 text-12">
      <div class="flex items-center justify-between gap-8">
        <dt class="m-0 text-muted">ERP 接入</dt>
        <dd class="m-0 flex items-center gap-4 font-medium text-body-strong">
          <span
            class="size-6 rounded-full"
            :class="configured ? 'bg-success' : 'bg-status-off-border'"
            aria-hidden="true"
          ></span>
          {{ statusLabel(configured, '已配置', '未配置') }}
        </dd>
      </div>
      <div class="flex items-center justify-between gap-8">
        <dt class="m-0 text-muted">订单同步</dt>
        <dd class="m-0 flex items-center gap-4 font-medium text-body-strong">
          <span
            class="size-6 rounded-full"
            :class="orderSyncEnabled ? 'bg-success' : 'bg-status-off-border'"
            aria-hidden="true"
          ></span>
          {{ statusLabel(orderSyncEnabled, '可用', '不可用') }}
        </dd>
      </div>
    </dl>
  </AppCard>
</template>
