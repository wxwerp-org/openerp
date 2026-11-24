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
  <AppCard class="flex h-full min-h-120 flex-col" data-testid="workspace-system-card">
    <div class="flex h-54 flex-none items-start justify-between gap-12 px-16 pt-16">
      <button
        type="button"
        class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink hover:text-primary"
        @click="emit('open')"
      >
        系统状态<AppIcon
          name="i-chev-right"
          :size="12"
          class="text-title-caret [stroke-width:1.8]"
        />
      </button>
      <span class="text-12 whitespace-nowrap text-muted">ERP 接入</span>
    </div>
    <div
      v-if="status === 'error'"
      class="flex flex-1 flex-col items-start gap-8 px-16 pb-16 text-12"
    >
      <span class="text-danger">{{ error }}</span>
      <AppButton size="md" @click="emit('retry')">重试</AppButton>
    </div>
    <dl v-else class="m-0 flex flex-1 flex-col gap-10 px-16 pt-6 pb-16 text-12/16">
      <div class="flex items-center justify-between gap-8">
        <dt class="m-0 text-muted">ERP 接入</dt>
        <dd class="m-0 flex items-center gap-4 font-medium text-body-strong">
          <span
            class="size-6 rounded-full"
            :class="configured ? 'bg-success' : 'bg-tag-gray-border'"
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
            :class="orderSyncEnabled ? 'bg-success' : 'bg-tag-gray-border'"
            aria-hidden="true"
          ></span>
          {{ statusLabel(orderSyncEnabled, '可用', '不可用') }}
        </dd>
      </div>
    </dl>
  </AppCard>
</template>
