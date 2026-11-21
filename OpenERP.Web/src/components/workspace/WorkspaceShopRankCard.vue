<script setup lang="ts">
import { computed } from 'vue'
import AppCard from '../../ui/AppCard.vue'
import AppIcon from '../../ui/AppIcon.vue'
import { formatCount } from '../../workspace/workspaceData'

const props = defineProps<{
  rows: { key: string; name: string; orderCount: number; itemQuantity: number }[]
}>()

const emit = defineEmits<{ select: [key: string]; open: [] }>()

const maxQuantity = computed(() => Math.max(0, ...props.rows.map((row) => row.itemQuantity)))

function barWidth(itemQuantity: number): number {
  return maxQuantity.value > 0 ? (itemQuantity / maxQuantity.value) * 100 : 0
}
</script>

<template>
  <AppCard class="flex h-296 flex-col" data-testid="workspace-shop-rank-card">
    <div class="flex h-54 flex-none items-start justify-between gap-12 px-16 pt-16">
      <button
        type="button"
        class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink hover:text-primary"
        @click="emit('open')"
      >
        店铺商品量 Top5<AppIcon
          name="i-chev-right"
          :size="12"
          class="text-title-caret [stroke-width:1.8]"
        />
      </button>
      <span class="inline-flex items-center gap-3 text-12 whitespace-nowrap text-muted">
        按商品件数排序<AppIcon name="i-info" :size="13" class="ml-2 text-placeholder" />
      </span>
    </div>
    <ul
      v-if="rows.length > 0"
      role="list"
      class="m-0 flex min-h-0 flex-1 list-none flex-col gap-8 overflow-y-auto px-16 pb-16"
    >
      <li v-for="(row, index) in rows" :key="row.key" class="min-w-0">
        <button
          type="button"
          class="flex w-full min-w-0 flex-col gap-5 rounded-4 text-left hover:bg-page-soft"
          :data-shop-key="row.key"
          @click="emit('select', row.key)"
        >
          <span class="flex items-center justify-between gap-8 text-12 text-body">
            <span class="flex min-w-0 items-center gap-6">
              <span
                class="flex size-20 flex-none items-center justify-center rounded-4 text-11 font-semibold"
                :class="index === 0 ? 'bg-primary-soft text-primary' : 'bg-page text-body-strong'"
                aria-hidden="true"
                >{{ index + 1 }}</span
              >
              <span class="truncate text-ink" :title="row.name">{{ row.name }}</span>
            </span>
            <span class="whitespace-nowrap text-muted tabular-nums">
              订单 {{ formatCount(row.orderCount) }}
            </span>
          </span>
          <span class="flex items-center gap-8">
            <span class="h-6 min-w-0 flex-1 overflow-hidden rounded-3 bg-progress-track">
              <span
                class="block h-full rounded-3"
                :style="{
                  width: `${barWidth(row.itemQuantity)}%`,
                  background: index === 0 ? 'var(--color-chart-1)' : 'var(--color-chart-2)',
                }"
              ></span>
            </span>
            <b class="flex-none text-12 font-medium text-ink tabular-nums">
              {{ formatCount(row.itemQuantity) }} 件
            </b>
          </span>
        </button>
      </li>
    </ul>
    <div v-else class="flex flex-1 items-center justify-center text-12 text-muted">
      暂无订单数据
    </div>
  </AppCard>
</template>
