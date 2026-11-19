<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import AppIcon from '../../ui/AppIcon.vue'
import { workspacePages, type WorkspacePage } from '../../router/workspace'

defineProps<{
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

onMounted(() => document.addEventListener('click', closeOnOutsideClick))
onUnmounted(() => document.removeEventListener('click', closeOnOutsideClick))
</script>

<template>
  <div class="tab-area">
    <div class="page-tabs" aria-label="已打开页面">
      <div
        v-for="item in items"
        :key="item.id"
        class="page-tab"
        :class="{ active: item.id === activePage, home: item.id === 'dashboard' }"
      >
        <RouterLink
          class="tab-label"
          :to="item.path"
          :aria-current="item.id === activePage ? 'page' : undefined"
        >
          {{ item.label }}
        </RouterLink>
        <button
          v-if="item.id !== 'dashboard'"
          class="tab-close"
          type="button"
          :data-close-tab="item.id"
          :title="`关闭${item.label}`"
          :aria-label="`关闭${item.label}`"
          @click.stop="emit('close', item.id)"
        >
          <AppIcon name="i-close" icon-class="ic-12" />
        </button>
      </div>
    </div>
    <button
      id="tabAdd"
      ref="tabAdd"
      class="tab-add"
      type="button"
      title="打开页面"
      aria-label="打开页面"
      :aria-expanded="menuOpen"
      @click="menuOpen = !menuOpen"
    >
      <AppIcon name="i-plus" />
    </button>
    <div
      v-show="menuOpen"
      id="tabMenu"
      ref="tabMenu"
      class="tab-menu"
      @keydown.esc="menuOpen = false"
    >
      <RouterLink
        v-for="page in openPages"
        :key="page.id"
        :to="page.path"
        @click="menuOpen = false"
      >
        <span>{{ page.label }}</span
        ><AppIcon name="i-chev-right" icon-class="ic-12" />
      </RouterLink>
    </div>
  </div>
</template>

<style scoped>
.tab-label {
  display: flex;
  height: 100%;
  align-items: center;
  padding: 0;
  border: 0;
  background: transparent;
  color: inherit;
  font: inherit;
  text-decoration: none;
  cursor: pointer;
}

.tab-menu a {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
</style>
