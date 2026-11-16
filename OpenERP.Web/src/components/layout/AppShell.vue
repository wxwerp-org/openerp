<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AccountMenu from '../navigation/AccountMenu.vue'
import ProfileDialog from '../../account/ProfileDialog.vue'
import PasswordDialog from '../../account/PasswordDialog.vue'
import AppSidebar from '../navigation/AppSidebar.vue'
import IconSprite from '../navigation/IconSprite.vue'
import WorkspaceTabs from '../navigation/WorkspaceTabs.vue'
import { workspacePages, type WorkspacePage, type WorkspacePageId } from '../../router/workspace'
import { useToast } from '../../composables/useToast'

const route = useRoute()
const router = useRouter()
const sidebarCollapsed = ref(false)
const openTabs = ref<WorkspacePage[]>(workspacePages.slice(0, 3))
const { toast, showToast } = useToast()
const profileOpen = ref(false)
const passwordOpen = ref(false)
const accountMenu = ref<InstanceType<typeof AccountMenu> | null>(null)

const activePage = computed<WorkspacePageId | null>(() => {
  return workspacePages.find((page) => page.path === route.path)?.id ?? null
})

watch(activePage, (page) => {
  if (!page || openTabs.value.some((tab) => tab.id === page)) return
  const current = workspacePages.find((item) => item.id === page)
  if (current) openTabs.value.push(current)
}, { immediate: true })

function navigate(page: string): void {
  const target = workspacePages.find((item) => item.id === page)
  if (target && route.path !== target.path) void router.push(target.path)
}

function closeTab(page: string): void {
  if (page === 'dashboard') return
  const index = openTabs.value.findIndex((tab) => tab.id === page)
  if (index < 0) return
  openTabs.value.splice(index, 1)
  if (activePage.value === page) {
    const next = openTabs.value[index - 1] ?? openTabs.value[index] ?? openTabs.value[0]
    navigate(next.id)
  }
}

function closeProfile(): void {
  profileOpen.value = false
  void nextTick(() => accountMenu.value?.focusTrigger())
}

function closePassword(): void {
  passwordOpen.value = false
  void nextTick(() => accountMenu.value?.focusTrigger())
}

function handleAccountAction(action: 'profile' | 'password' | 'settings' | 'logout'): void {
  if (action === 'settings') {
    navigate('business')
    return
  }
  if (action === 'profile') profileOpen.value = true
  else if (action === 'password') passwordOpen.value = true
  else showToast('原型演示：退出登录')
}
</script>

<template>
  <a class="skip-link" href="#main">跳转到主内容</a>
  <IconSprite />
  <div class="app" :style="{ '--sidebar-width': sidebarCollapsed ? '56px' : '200px' }">
    <AppSidebar
      :active-page="activePage ?? ''"
      :collapsed="sidebarCollapsed"
      @update:collapsed="sidebarCollapsed = $event"
      @navigate="navigate"
      @preview="showToast(`原型演示：${$event}`)"
    />

    <div class="app-main">
      <header class="topbar">
        <WorkspaceTabs :items="openTabs" :active-page="activePage ?? ''" @close="closeTab" />
        <AccountMenu ref="accountMenu" @action="handleAccountAction" @notice="showToast('原型演示：通知')" />
      </header>
      <main id="main" class="main" tabindex="-1">
        <RouterView />
        <ProfileDialog :open="profileOpen" @close="closeProfile" @feedback="showToast" />
        <PasswordDialog :open="passwordOpen" @close="closePassword" @feedback="showToast" />
      </main>
    </div>
  </div>
  <div v-show="toast" id="toast" class="toast" role="status">{{ toast }}</div>
</template>
