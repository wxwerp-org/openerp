<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import AppIcon from '../../ui/AppIcon.vue'
import { workspacePages, type WorkspacePage } from '../../router/workspace'

const props = defineProps<{
  items: WorkspacePage[]
  activePage: string
}>()

const emit = defineEmits<{
  close: [page: string]
}>()

const menuOpen = ref(false)
const tabMenu = ref<HTMLElement | null>(null)
const tabAdd = ref<HTMLButtonElement | null>(null)
const openPages = workspacePages

function closeOnOutsideClick(event: MouseEvent): void {
  const target = event.target as Node
  if (!tabAdd.value?.contains(target) && !tabMenu.value?.contains(target)) menuOpen.value = false
}

/** 旧样式用相邻选择器画未激活页签之间的分隔线，迁移后按同一条件渲染分隔元素。 */
function showSeparator(index: number): boolean {
  const current = props.items[index]
  const previous = props.items[index - 1]
  if (!current || !previous) return false
  return current.id !== props.activePage && previous.id !== props.activePage
}

onMounted(() => document.addEventListener('click', closeOnOutsideClick))
onUnmounted(() => document.removeEventListener('click', closeOnOutsideClick))
</script>

<template>
  <div class="flex h-38 min-w-0 flex-1 items-end overflow-hidden pt-6">
    <div
      class="page-tabs flex h-32 min-w-0 items-end overflow-x-auto overflow-y-hidden [scrollbar-width:none]"
      aria-label="已打开页面"
    >
      <div
        v-for="(item, index) in items"
        :key="item.id"
        class="group relative flex h-32 w-max flex-none cursor-pointer items-center rounded-t-6 text-12/16 whitespace-nowrap"
        :class="[
          item.id === 'dashboard' ? 'justify-center px-14' : 'pl-12 pr-28',
          item.id === activePage ? 'bg-white text-ink' : 'text-tab hover:text-primary',
        ]"
        data-testid="page-tab"
      >
        <span
          v-if="showSeparator(index)"
          class="absolute left-0 top-9 h-14 w-1 bg-scrim"
          aria-hidden="true"
        />
        <RouterLink
          class="flex h-full items-center"
          :to="item.path"
          :aria-current="item.id === activePage ? 'page' : undefined"
        >
          {{ item.label }}
        </RouterLink>
        <button
          v-if="item.id !== 'dashboard'"
          type="button"
          class="absolute right-8 top-8 flex size-16 items-center justify-center rounded-3 text-muted hover:bg-toolbar hover:text-ink"
          :class="
            item.id === activePage
              ? 'visible'
              : 'invisible group-hover:visible group-focus-within:visible'
          "
          :data-close-tab="item.id"
          :title="`关闭${item.label}`"
          :aria-label="`关闭${item.label}`"
          @click.stop="emit('close', item.id)"
        >
          <AppIcon name="i-close" class="size-12" />
        </button>
      </div>
    </div>
    <button
      ref="tabAdd"
      class="flex size-32 flex-none items-center justify-center rounded-t-6 text-tab hover:bg-hover-bg hover:text-primary"
      type="button"
      data-testid="tab-add"
      title="打开页面"
      aria-label="打开页面"
      aria-controls="tabMenu"
      :aria-expanded="menuOpen"
      @click="menuOpen = !menuOpen"
    >
      <AppIcon name="i-plus" />
    </button>
    <div
      v-show="menuOpen"
      id="tabMenu"
      ref="tabMenu"
      class="absolute left-0 top-36 z-[200] w-148 rounded-4 border border-line bg-white p-4 shadow-menu-lg"
      @keydown.esc="menuOpen = false"
    >
      <RouterLink
        v-for="page in openPages"
        :key="page.id"
        :to="page.path"
        class="flex items-center justify-between"
        @click="menuOpen = false"
      >
        <span>{{ page.label }}</span
        ><AppIcon name="i-chev-right" class="size-12" />
      </RouterLink>
    </div>
  </div>
</template>

<style scoped>
.page-tabs::-webkit-scrollbar {
  display: none;
}
</style>
