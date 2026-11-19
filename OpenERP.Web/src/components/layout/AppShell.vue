<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { logout, type Account } from '../../account/session'
import AccountMenu from '../navigation/AccountMenu.vue'
import ProfileDialog from '../../account/ProfileDialog.vue'
import PasswordDialog from '../../account/PasswordDialog.vue'
import AppSidebar from '../navigation/AppSidebar.vue'
import IconSprite from '../navigation/IconSprite.vue'
import WorkspaceTabs from '../navigation/WorkspaceTabs.vue'
import { navigationPages, workspacePages, type WorkspacePage } from '../../router/workspace'
import { useToast } from '../../composables/useToast'
import AppToast from '../../ui/AppToast.vue'

defineProps<{ account: Account }>()

const route = useRoute()
const router = useRouter()
const sidebarCollapsed = ref(false)
const openTabs = ref<WorkspacePage[]>(workspacePages.slice(0, 3))
const { toast, showToast } = useToast()
const profileOpen = ref(false)
const passwordOpen = ref(false)
const logoutPending = ref(false)
const accountMenu = ref<InstanceType<typeof AccountMenu> | null>(null)

const activePage = computed<string | null>(() => {
  return navigationPages.find((page) => page.path === route.path)?.id ?? null
})

watch(
  activePage,
  (page) => {
    if (!page || openTabs.value.some((tab) => tab.id === page)) return
    const current = workspacePages.find((item) => item.id === page)
    if (current) openTabs.value.push(current)
  },
  { immediate: true },
)

function navigate(page: string): void {
  const target = navigationPages.find((item) => item.id === page)
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

async function handleAccountAction(
  action: 'profile' | 'password' | 'settings' | 'logout',
): Promise<void> {
  if (action === 'settings') {
    navigate('business')
    return
  }
  if (action === 'profile') profileOpen.value = true
  else if (action === 'password') passwordOpen.value = true
  else if (!logoutPending.value) {
    logoutPending.value = true
    try {
      await logout()
    } catch (error) {
      showToast(
        error instanceof Error && !(error instanceof TypeError)
          ? error.message
          : '退出登录失败，请稍后重试。',
      )
      accountMenu.value?.focusTrigger()
    } finally {
      logoutPending.value = false
    }
  }
}
</script>

<template>
  <a
    href="#main"
    class="fixed left-8 top-8 z-[3000] -translate-y-[150%] rounded-4 bg-primary px-12 py-8 text-white focus-visible:translate-y-0 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ink"
    >跳转到主内容</a
  >
  <IconSprite />
  <div
    class="flex h-[100dvh] w-screen min-w-1440 overflow-hidden bg-page"
    data-testid="app-shell"
    :style="{ '--sidebar-width': sidebarCollapsed ? '56px' : '200px' }"
  >
    <AppSidebar
      :active-page="activePage ?? ''"
      :collapsed="sidebarCollapsed"
      @update:collapsed="sidebarCollapsed = $event"
      @navigate="navigate"
      @preview="showToast(`原型演示：${$event}`)"
    />

    <div class="flex h-full min-w-0 flex-1 flex-col overflow-hidden">
      <header class="relative z-50 flex h-38 flex-none items-stretch bg-topnav">
        <WorkspaceTabs :items="openTabs" :active-page="activePage ?? ''" @close="closeTab" />
        <AccountMenu
          ref="accountMenu"
          :account="account"
          @action="handleAccountAction"
          @notice="showToast('原型演示：通知')"
        />
      </header>
      <main id="main" class="relative min-h-0 flex-1 overflow-hidden bg-page" tabindex="-1">
        <RouterView />
        <ProfileDialog
          :open="profileOpen"
          :account="account"
          @close="profileOpen = false"
          @closed="accountMenu?.focusTrigger()"
          @feedback="showToast"
        />
        <PasswordDialog
          :open="passwordOpen"
          @close="passwordOpen = false"
          @closed="accountMenu?.focusTrigger()"
        />
      </main>
    </div>
  </div>
  <AppToast :message="toast" data-testid="toast" />
</template>
