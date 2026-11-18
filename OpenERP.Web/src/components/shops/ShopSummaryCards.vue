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
    tone: 'valid',
  },
  {
    key: 'invalid' as const,
    label: '无效',
    value: props.summary.invalid,
    hint: '平台授权失效',
    tone: 'invalid',
  },
  {
    key: 'unknown' as const,
    label: '未知',
    value: props.summary.unknown,
    hint: '尚未检查或读取失败',
    tone: 'unknown',
  },
])

function format(value: number): string {
  return new Intl.NumberFormat('zh-CN').format(value)
}
</script>

<template>
  <div class="shop-summary" aria-label="授权状态汇总">
    <button
      v-for="card in cards"
      :key="card.key"
      class="shop-summary-card"
      :class="{ 'is-active': statusFilter === card.key }"
      type="button"
      :aria-pressed="statusFilter === card.key"
      :aria-label="`${card.label} ${format(card.value)} 家，点击筛选`"
      @click="emit('select', card.key)"
    >
      <span class="shop-summary-label">{{ card.label }}</span>
      <strong class="shop-summary-value" :class="card.tone">{{ format(card.value) }}</strong>
      <span class="shop-summary-hint">{{ card.hint }}</span>
    </button>

    <div class="shop-summary-card is-static">
      <span class="shop-summary-label">最后同步</span>
      <strong class="shop-summary-value is-time">{{ lastSyncedLabel }}</strong>
      <span class="shop-summary-hint">最近一次成功同步</span>
    </div>
  </div>
</template>

<style scoped>
.shop-summary {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
  margin-bottom: 12px;
}

.shop-summary-card {
  display: flex;
  min-width: 0;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
  padding: 14px 16px;
  border: 1px solid var(--line);
  border-radius: var(--radius-card, 6px);
  background: #fff;
  text-align: left;
}

button.shop-summary-card {
  cursor: pointer;
  transition:
    border-color 0.15s ease,
    background-color 0.15s ease,
    box-shadow 0.15s ease;
}

button.shop-summary-card:hover {
  border-color: #b7d1ff;
  box-shadow: 0 4px 10px rgba(81, 133, 192, 0.1);
}

button.shop-summary-card:focus-visible {
  outline: 2px solid var(--primary);
  outline-offset: 1px;
}

.shop-summary-card.is-active {
  border-color: var(--primary);
  background: #f4f8ff;
}

.shop-summary-card.is-static {
  cursor: default;
}

.shop-summary-label {
  color: var(--muted);
  font-size: 12px;
  line-height: 18px;
}

.shop-summary-value {
  color: var(--text);
  font-size: 24px;
  font-weight: 500;
  line-height: 30px;
  font-variant-numeric: tabular-nums;
}

.shop-summary-value.valid {
  color: #00941e;
}

.shop-summary-value.invalid {
  color: #e52e2e;
}

.shop-summary-value.unknown {
  color: #606a78;
}

.shop-summary-value.is-time {
  font-size: 18px;
  line-height: 26px;
}

.shop-summary-hint {
  overflow: hidden;
  max-width: 100%;
  color: var(--muted);
  font-size: 11px;
  line-height: 16px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 1100px) {
  .shop-summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>
