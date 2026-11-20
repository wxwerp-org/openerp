<script setup lang="ts">
import AppCard from '../../ui/AppCard.vue'
import { formatCount } from '../../workspace/workspaceData'

defineProps<{
  rows: { key: string; name: string; orderCount: number; itemQuantity: number }[]
}>()

const emit = defineEmits<{ select: [key: string] }>()
</script>

<template>
  <AppCard class="flex h-296 flex-col px-16 pb-12 pt-14" data-testid="workspace-shop-rank-card">
    <div class="flex flex-none items-center justify-between">
      <h2 class="text-13/20 font-semibold text-ink">店铺商品量 Top5</h2>
      <span class="text-11 text-muted">按商品件数排序</span>
    </div>
    <ul v-if="rows.length > 0" class="m-0 flex min-h-0 flex-1 list-none flex-col p-0">
      <li
        v-for="(row, index) in rows"
        :key="row.key"
        class="min-w-0 border-b border-line-soft last:border-0"
      >
        <button
          type="button"
          class="flex h-44 w-full items-center gap-10 rounded-4 px-4 text-left hover:bg-primary-faint"
          :data-shop-key="row.key"
          @click="emit('select', row.key)"
        >
          <span
            class="flex size-20 flex-none items-center justify-center rounded-4 text-11 font-semibold"
            :class="index === 0 ? 'bg-primary-soft text-primary' : 'bg-page text-body-strong'"
            aria-hidden="true"
            >{{ index + 1 }}</span
          >
          <span class="min-w-0 flex-1">
            <span class="block truncate text-12 text-ink" :title="row.name">{{ row.name }}</span>
            <span class="block text-11 text-muted">订单 {{ formatCount(row.orderCount) }}</span>
          </span>
          <span class="flex-none text-right">
            <span class="block text-13 font-semibold tabular-nums text-ink">{{
              formatCount(row.itemQuantity)
            }}</span>
            <span class="block text-11 text-muted">件</span>
          </span>
        </button>
      </li>
    </ul>
    <div v-else class="flex flex-1 items-center justify-center text-12 text-muted">
      暂无订单数据
    </div>
  </AppCard>
</template>
