<script setup lang="ts">
import { computed } from 'vue'
import AppButton from '../../ui/AppButton.vue'
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'
import AppProgress from '../../ui/AppProgress.vue'
import type { OrderSyncTask } from '../../composables/orderSyncTask'
import type { ShopSyncStatus } from '../../composables/useErpHub'
import {
  formatCount,
  formatDateTime,
  skipReasonLabel,
  stageLabel,
  syncStatusLabels,
} from '../../workspace/workspaceData'
import { groupSyncTasks } from '../../workspace/workspaceDerivation'

const props = defineProps<{
  administrator: boolean
  hubState: string
  shopSync: ShopSyncStatus | null
  orderTasks: OrderSyncTask[]
  shopNames: Map<string, string>
  shopSyncStarting: boolean
  syncMessage: string
  startingAll: boolean
  batchMessage: string
  batchError: string
}>()

const emit = defineEmits<{ syncShops: []; startAll: []; open: [] }>()

const groupedTasks = computed(() => groupSyncTasks(props.orderTasks))
const runningTasks = computed(() => groupedTasks.value.running)
const queuedCount = computed(() => groupedTasks.value.queued)
const terminalTasks = computed(() =>
  props.orderTasks.filter((task) => task.status !== 'running').slice(-3),
)
const hasRunning = computed(() => runningTasks.value.length > 0 || queuedCount.value > 0)

const shopSyncPercent = computed(() => {
  const status = props.shopSync
  if (!status || status.total <= 0) return 0
  return Math.min(100, Math.round((status.checked / status.total) * 100))
})

function shopLabel(sid: number): string {
  return props.shopNames.get(String(sid)) ?? `店铺 ${sid}`
}

function terminalDetail(task: OrderSyncTask): string {
  const parts = [
    `新增 ${formatCount(task.added)}`,
    `更新 ${formatCount(task.updated)}`,
    `未变 ${formatCount(task.unchanged)}`,
    `跳过 ${formatCount(task.skipped)}`,
  ]
  return parts.join(' · ')
}

function skipSummary(task: OrderSyncTask): string {
  const entries = Object.entries(task.skipReasons).filter(([, count]) => count > 0)
  if (entries.length === 0) return ''
  return entries.map(([reason, count]) => `${skipReasonLabel(reason)} ${count}`).join('，')
}
</script>

<template>
  <AppCard class="flex h-full min-h-120 flex-col" data-testid="workspace-sync-card">
    <div class="flex h-54 flex-none items-start justify-between gap-12 px-16 pt-16">
      <button
        type="button"
        class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink hover:text-primary"
        @click="emit('open')"
      >
        同步状态<AppIcon
          name="i-chev-right"
          :size="12"
          class="text-title-caret [stroke-width:1.8]"
        />
      </button>
      <span v-if="hasRunning" class="text-12 whitespace-nowrap text-primary">同步进行中</span>
    </div>
    <div class="flex min-h-0 flex-1 flex-col px-16 pb-16">
      <p
        v-if="hubState === 'disconnected' || hubState === 'reconnecting'"
        class="m-0 mt-8 rounded-2 bg-notice-amber-bg px-6 py-3 text-12 text-notice-amber-text"
        role="status"
      >
        实时连接已断开，进度暂不可确认；重连后自动补取。
      </p>

      <div v-if="administrator" class="mt-10 border-b border-line-soft pb-10">
        <div class="flex items-center justify-between gap-8">
          <span class="text-12 text-muted">目录同步</span>
          <span v-if="shopSync?.running" class="text-12/16 text-primary">
            已检查 {{ formatCount(shopSync.checked) }} /
            {{ shopSync.total > 0 ? formatCount(shopSync.total) : '—' }}
          </span>
          <span v-else-if="shopSync?.error" class="text-12/16 text-danger"
            >失败：{{ shopSync.error }}</span
          >
          <span v-else class="text-12/16 text-muted">
            {{
              shopSync?.completedAt
                ? `最近完成 ${formatDateTime(shopSync.completedAt)}`
                : '尚未同步'
            }}
          </span>
        </div>
        <AppProgress
          v-if="shopSync?.running"
          :value="shopSyncPercent"
          :max="100"
          label="目录同步进度"
          class="mt-6"
        />
        <div class="mt-4 flex items-center gap-10 text-12/16 text-muted">
          <span>有效 {{ formatCount(shopSync?.valid ?? 0) }}</span>
          <span>无效 {{ formatCount(shopSync?.invalid ?? 0) }}</span>
          <span>未知 {{ formatCount(shopSync?.unknown ?? 0) }}</span>
          <button
            type="button"
            class="ml-auto text-primary hover:underline disabled:text-muted"
            :disabled="shopSync?.running || shopSyncStarting"
            @click="emit('syncShops')"
          >
            {{ shopSync?.running ? '同步中…' : shopSyncStarting ? '启动中…' : '同步目录' }}
          </button>
        </div>
        <p v-if="syncMessage" class="m-0 mt-4 text-12/16 text-danger" role="alert">
          {{ syncMessage }}
        </p>
      </div>

      <div class="mt-10 min-h-0 flex-1">
        <div class="flex items-center justify-between gap-8">
          <span class="text-12 text-muted">订单同步</span>
          <span v-if="queuedCount > 0" class="text-12/16 text-body">排队中 {{ queuedCount }}</span>
        </div>

        <ul v-if="runningTasks.length > 0" class="m-0 mt-6 flex list-none flex-col gap-8 p-0">
          <li v-for="task in runningTasks" :key="task.sid" class="min-w-0">
            <div class="flex items-center justify-between gap-8 text-12">
              <span class="min-w-0 truncate text-body-strong" :title="shopLabel(task.sid)">
                {{ shopLabel(task.sid) }}
              </span>
              <span class="flex-none text-12/16 text-primary">{{ stageLabel(task.stage) }}</span>
            </div>
            <AppProgress
              :value="task.total > 0 ? Math.min(task.processed, task.total) : 0"
              :max="task.total > 0 ? task.total : 1"
              :label="`${shopLabel(task.sid)}同步进度`"
              class="mt-2"
            />
            <div class="mt-2 flex items-center justify-between text-12/16 text-muted">
              <span>已处理 {{ formatCount(task.processed) }}</span>
              <span
                >读取 {{ formatCount(task.read) }} / 总数
                {{ task.total > 0 ? formatCount(task.total) : '—' }}</span
              >
            </div>
          </li>
        </ul>

        <ul v-if="terminalTasks.length > 0" class="m-0 mt-6 flex list-none flex-col gap-6 p-0">
          <li
            v-for="task in terminalTasks"
            :key="task.sid"
            class="rounded-4 bg-page px-8 py-6 text-12/16"
            :data-sync-result="task.status"
          >
            <div class="flex items-center justify-between gap-8">
              <span class="min-w-0 truncate text-body-strong">{{ shopLabel(task.sid) }}</span>
              <span :class="task.status === 'failed' ? 'text-danger' : 'text-body'">
                {{ syncStatusLabels[task.status] ?? task.status }}
              </span>
            </div>
            <div class="mt-2 text-muted">{{ terminalDetail(task) }}</div>
            <div v-if="skipSummary(task)" class="mt-1 text-muted">
              跳过原因：{{ skipSummary(task) }}
            </div>
            <div v-if="task.status === 'failed' && task.error" class="mt-1 text-danger">
              {{ task.error }}
            </div>
          </li>
        </ul>

        <p v-if="!hasRunning && terminalTasks.length === 0" class="m-0 mt-6 text-12/16 text-muted">
          当前无进行中的订单同步。
        </p>

        <div class="mt-8 flex items-center gap-8">
          <AppButton size="md" :disabled="startingAll || hasRunning" @click="emit('startAll')">
            {{ startingAll ? '启动中…' : '全部同步' }}
          </AppButton>
          <span v-if="batchMessage" class="text-12/16 text-body">{{ batchMessage }}</span>
          <span v-if="batchError" class="text-12/16 text-danger">{{ batchError }}</span>
        </div>
      </div>
    </div>
  </AppCard>
</template>
