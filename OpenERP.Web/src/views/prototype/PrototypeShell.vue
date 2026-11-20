<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { account } from '../../account/session'

const route = useRoute()
const router = useRouter()

const switchItems = [
  { name: 'visual-prototype', label: '工作台', path: '/visual-prototype' },
  { name: 'visual-prototype-products', label: '产品管理', path: '/visual-prototype/products' },
] as const

const activeName = computed(() => String(route.name ?? ''))

let robotsMeta: HTMLMetaElement | null = null

onMounted(() => {
  // 页面内二次校验：路由守卫之外再挡一次，避免原型内容被误渲染。
  if (account.value?.role !== 'Administrator') {
    void router.replace('/')
    return
  }
  robotsMeta = document.createElement('meta')
  robotsMeta.name = 'robots'
  robotsMeta.content = 'noindex'
  document.head.appendChild(robotsMeta)
})

onUnmounted(() => {
  robotsMeta?.remove()
  robotsMeta = null
})
</script>

<template>
  <div class="flex h-full min-h-0 w-full flex-col bg-page" data-testid="visual-prototype">
    <div
      class="flex h-36 flex-none items-center gap-12 border-b border-notice-amber-border bg-notice-amber-bg px-16 text-12 text-notice-amber-text"
      role="note"
    >
      <span class="font-medium">
        视觉原型 · 全部为假数据 · 冻结于 2026-11-20 · 仅内部参考，请勿对外展示
      </span>
      <div class="ml-auto flex flex-none items-center gap-4">
        <RouterLink
          v-for="item in switchItems"
          :key="item.name"
          :to="item.path"
          class="rounded-4 px-8 py-2"
          :class="
            activeName === item.name
              ? 'bg-white font-medium text-primary shadow-sm'
              : 'text-ink-muted hover:text-primary'
          "
        >
          {{ item.label }}
        </RouterLink>
      </div>
    </div>
    <div class="min-h-0 flex-1 overflow-hidden">
      <RouterView />
    </div>
  </div>
</template>
