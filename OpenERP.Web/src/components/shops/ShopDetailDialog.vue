<script setup lang="ts">
import AppButton from '../../ui/AppButton.vue'
import AppDialog from '../../ui/AppDialog.vue'
import AppDialogClose from '../../ui/AppDialogClose.vue'
import AppIcon from '../../ui/AppIcon.vue'
import AppTag from '../../ui/AppTag.vue'
import type { Shop } from './types'

const props = defineProps<{
  open: boolean
  canAuthorize: boolean
  shop: Shop | null
  platformName: string
}>()

const emit = defineEmits<{
  close: []
  closed: []
  authorize: []
}>()

function close(): void {
  if (props.open) emit('close')
}

function shopTitle(): string {
  if (!props.shop) return ''
  return props.shop.name.trim() || `店铺 ${props.shop.sid}`
}

function statusLabel(isTokenOk: boolean | null): string {
  if (isTokenOk === true) return '有效'
  if (isTokenOk === false) return '无效'
  return '未知'
}

function statusClass(isTokenOk: boolean | null): 'green' | 'red' | 'gray' {
  if (isTokenOk === true) return 'green'
  if (isTokenOk === false) return 'red'
  return 'gray'
}

function formatDateTime(value: string | null): string {
  if (!value) return '尚未检查'
  const date = new Date(value)
  return Number.isFinite(date.getTime())
    ? date.toLocaleString('zh-CN', { hour12: false })
    : '尚未检查'
}
</script>

<template>
  <AppDialog
    id="shopDetailLayer"
    :open="open"
    layer="center"
    backdrop="strong"
    aria-labelledby="shopDetailTitle"
    class="w-560 max-w-[calc(100vw_-_32px)] max-h-[calc(100vh_-_64px)] rounded-6 border-0 bg-white p-0 shadow-dialog-lg"
    @close="close"
    @closed="emit('closed')"
  >
    <div v-if="shop" class="flex flex-col px-22 pt-20 pb-18">
      <header class="flex items-start justify-between gap-12">
        <div>
          <h2
            id="shopDetailTitle"
            class="text-16/24 font-semibold text-ink [overflow-wrap:anywhere]"
          >
            {{ shopTitle() }}
          </h2>
          <p class="mt-4 text-12/18 text-muted">SID {{ shop.sid }} · {{ platformName }}</p>
        </div>
        <AppDialogClose id="closeShopDetail" @click="close" />
      </header>

      <dl class="m-0 mt-18 grid grid-cols-2 gap-x-20 gap-y-12 border-y border-y-line-soft py-16">
        <div class="min-w-0">
          <dt class="text-12/18 text-muted">店铺名称</dt>
          <dd class="m-0 mt-3 text-13/20 text-ink [overflow-wrap:anywhere]">{{ shopTitle() }}</dd>
        </div>
        <div class="min-w-0">
          <dt class="text-12/18 text-muted">店铺 ID</dt>
          <dd class="m-0 mt-3 text-13/20 text-ink [overflow-wrap:anywhere]">{{ shop.sid }}</dd>
        </div>
        <div class="min-w-0">
          <dt class="text-12/18 text-muted">平台</dt>
          <dd class="m-0 mt-3 text-13/20 text-ink [overflow-wrap:anywhere]">{{ platformName }}</dd>
        </div>
        <div class="min-w-0">
          <dt class="text-12/18 text-muted">站点</dt>
          <dd class="m-0 mt-3 text-13/20 text-ink [overflow-wrap:anywhere]">
            {{ shop.region?.trim() || '未知' }}
          </dd>
        </div>
        <div class="min-w-0">
          <dt class="text-12/18 text-muted">成员 ID</dt>
          <dd class="m-0 mt-3 text-13/20 text-ink [overflow-wrap:anywhere]">
            {{ shop.memberId?.trim() || '未提供' }}
          </dd>
        </div>
        <div class="min-w-0">
          <dt class="text-12/18 text-muted">授权状态</dt>
          <dd class="m-0 mt-3 text-13/20 text-ink [overflow-wrap:anywhere]">
            <AppTag :tone="statusClass(shop.isTokenOk)">{{ statusLabel(shop.isTokenOk) }}</AppTag>
          </dd>
        </div>
        <div class="min-w-0">
          <dt class="text-12/18 text-muted">最后检查</dt>
          <dd class="m-0 mt-3 text-13/20 text-ink [overflow-wrap:anywhere]">
            {{ formatDateTime(shop.syncedAt) }}
          </dd>
        </div>
        <div class="min-w-0">
          <dt class="text-12/18 text-muted">上游来源</dt>
          <dd class="m-0 mt-3 text-13/20 text-ink [overflow-wrap:anywhere]">
            wxw ERP（OpenERP 只读）
          </dd>
        </div>
      </dl>

      <p
        v-if="shop.tokenError"
        role="alert"
        class="mt-12 text-12/18 text-danger-deep [overflow-wrap:anywhere]"
      >
        状态检查失败：{{ shop.tokenError }}
      </p>

      <div
        v-if="shop.isTokenOk === false"
        class="mt-12 flex min-w-0 items-start gap-8 rounded-4 border border-notice-amber-border bg-notice-amber-bg p-12 text-12/18"
      >
        <AppIcon name="i-warning" :size="16" class="mt-1 shrink-0 text-warning" />
        <div class="min-w-0">
          <p class="font-semibold text-notice-amber-text [overflow-wrap:anywhere]">平台授权失效</p>
          <p class="mt-2 text-notice-amber-text [overflow-wrap:anywhere]">
            新数据可能停止，历史数据仍可读。{{
              canAuthorize
                ? '请去 wxw ERP 重新授权，完成后回到本页点击「立即同步」。'
                : '请联系管理员重新授权，完成后可点击「立即同步」刷新状态。'
            }}
          </p>
        </div>
      </div>

      <p class="mt-12 text-12/18 text-muted">
        授权范围、有效期、授权历史由 wxw ERP 管理，本系统不读取，也不在本地发起授权。
      </p>

      <footer class="mt-20 flex justify-end gap-8">
        <AppButton
          v-if="canAuthorize"
          id="shopAuthorize"
          variant="primary"
          autofocus
          @click="emit('authorize')"
        >
          去 wxw 授权
        </AppButton>
        <AppButton @click="close">关闭</AppButton>
      </footer>
    </div>
  </AppDialog>
</template>
