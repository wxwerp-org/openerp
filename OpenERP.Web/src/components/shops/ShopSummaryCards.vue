<script setup lang="ts">
import { computed } from 'vue'
import type { ShopStatusFilter, ShopSummary } from './types'

const props = defineProps<{
  summary: ShopSummary
  statusFilter: ShopStatusFilter | null
  lastSyncedLabel: string
}>()

const emit = defineEmits<{
  select: [status: ShopStatusFilter]
}>()

const cards = computed(() => [
  {
    key: 'valid' as const,
    label: '有效',
    value: props.summary.valid,
    hint: '平台授权有效',
    tone: 'valid' as const,
  },
  {
    key: 'invalid' as const,
    label: '无效',
    value: props.summary.invalid,
    hint: '平台授权失效',
    tone: 'invalid' as const,
  },
  {
    key: 'unknown' as const,
    label: '未知',
    value: props.summary.unknown,
    hint: '尚未检查或读取失败',
    tone: 'unknown' as const,
  },
])

const cardBaseClass =
  'flex min-w-0 flex-col items-start gap-4 rounded-6 border bg-white px-16 py-14 text-left'
const cardIdleClass = 'border-line'
const cardActiveClass = 'border-primary bg-shop-card-active'
const interactiveClass =
  'cursor-pointer transition duration-150 ease-[ease] hover:border-shop-card-hover-border hover:shadow-shop-card-hover focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-primary'
const valueClass: Record<'valid' | 'invalid' | 'unknown', string> = {
  valid: 'text-success',
  invalid: 'text-danger',
  unknown: 'text-status-unknown',
}

function format(value: number): string {
  return new Intl.NumberFormat('zh-CN').format(value)
}
</script>

<template>
  <div class="mb-12 grid grid-cols-4 gap-12" aria-label="授权状态汇总">
    <button
      v-for="card in cards"
      :key="card.key"
      type="button"
      data-testid="shop-summary-card"
      :aria-pressed="statusFilter === card.key"
      :aria-label="`${card.label} ${format(card.value)} 家，点击筛选`"
      :class="[
        cardBaseClass,
        interactiveClass,
        statusFilter === card.key ? cardActiveClass : cardIdleClass,
      ]"
      @click="emit('select', card.key)"
    >
      <span class="text-12/18 text-muted">{{ card.label }}</span>
      <strong class="text-24/30 font-medium tabular-nums" :class="valueClass[card.tone]">{{
        format(card.value)
      }}</strong>
      <span
        class="max-w-full overflow-hidden text-11/16 text-ellipsis whitespace-nowrap text-muted"
        >{{ card.hint }}</span
      >
    </button>

    <div :class="[cardBaseClass, cardIdleClass]" data-testid="shop-summary-card" data-static="true">
      <span class="text-12/18 text-muted">最后同步</span>
      <strong class="text-18/26 font-medium text-ink tabular-nums">{{ lastSyncedLabel }}</strong>
      <span class="max-w-full overflow-hidden text-11/16 text-ellipsis whitespace-nowrap text-muted"
        >最近一次成功同步</span
      >
    </div>
  </div>
</template>
