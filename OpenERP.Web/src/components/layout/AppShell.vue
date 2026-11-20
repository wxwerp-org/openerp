<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { logout, type Account } from '../../account/session'
import AccountMenu from '../navigation/AccountMenu.vue'
import AboutDialog from '../../account/AboutDialog.vue'
import ProfileDialog from '../../account/ProfileDialog.vue'
import PasswordDialog from '../../account/PasswordDialog.vue'
import AppSidebar from '../navigation/AppSidebar.vue'
import IconSprite from '../navigation/IconSprite.vue'
import WorkspaceTabs from '../navigation/WorkspaceTabs.vue'
import { navigationPages, workspacePages, type WorkspacePage } from '../../router/workspace'
import { useToast } from '../../composables/useToast'
import AppToast from '../../ui/AppToast.vue'

const props = defineProps<{ account: Account }>()

const route = useRoute()
const router = useRouter()
const sidebarCollapsed = ref(false)
const administrator = computed(() => props.account.role === 'Administrator')
const openTabs = ref<WorkspacePage[]>(workspacePages.slice(0, 1))
const visibleTabs = computed(() =>
  openTabs.value.filter((tab) => !tab.administratorOnly || administrator.value),
)
const { toast, showToast } = useToast()
const profileOpen = ref(false)
const passwordOpen = ref(false)
const logoutPending = ref(false)
const accountMenu = ref<InstanceType<typeof AccountMenu> | null>(null)
const aboutOpen = ref(false)

watch(
  () => route.query.denied,
  (denied) => {
    if (denied !== '1') return
    showToast('该页面仅管理员可访问。')
    const query = { ...route.query }
    delete query.denied
    void router.replace({ query })
  },
  { immediate: true },
)

const activePage = computed<string | null>(() => {
  return navigationPages.find((page) => page.path === route.path)?.id ?? null
})

const narrowViewport = ref(false)
const coarsePointer = ref(false)
let narrowQuery: MediaQueryList | undefined
let coarseQuery: MediaQueryList | undefined

function syncViewportTraits(): void {
  narrowViewport.value = narrowQuery?.matches ?? false
  coarsePointer.value = coarseQuery?.matches ?? false
}

onMounted(() => {
  if (typeof window.matchMedia !== 'function') return
  // 工作台/报表在 <1024 时统一为单列 + 轨道侧栏（断点验收 1023/1024）。
  narrowQuery = window.matchMedia('(max-width: 1023px)')
  coarseQuery = window.matchMedia('(pointer: coarse)')
  syncViewportTraits()
  narrowQuery.addEventListener('change', syncViewportTraits)
  coarseQuery.addEventListener('change', syncViewportTraits)
})

onUnmounted(() => {
  narrowQuery?.removeEventListener('change', syncViewportTraits)
  coarseQuery?.removeEventListener('change', syncViewportTraits)
})

/** 工作台与商品量报表共用窄屏轨道：解除桌面最小宽度，并让侧栏收敛为导航轨道。 */
const fluidShell = computed(
  () => activePage.value === 'dashboard' || activePage.value === 'order-report',
)
const reportRail = computed(() => fluidShell.value && narrowViewport.value)
const touchRail = computed(() => reportRail.value && coarsePointer.value)
const shellMinWidthClass = computed(() => (fluidShell.value ? 'min-w-0' : 'min-w-1440'))

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
  if (target && (!target.administratorOnly || administrator.value) && route.path !== target.path)
    void router.push(target.path)
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
  action: 'profile' | 'password' | 'settings' | 'logout' | 'about',
): Promise<void> {
  if (action === 'settings') {
    navigate('business')
    return
  }
  if (action === 'about') {
    aboutOpen.value = true
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
    class="flex h-[100dvh] w-screen overflow-hidden bg-page"
    :class="shellMinWidthClass"
    :data-fluid-shell="fluidShell ? '' : undefined"
    :style="{ '--sidebar-width': sidebarCollapsed || reportRail ? '56px' : '200px' }"
  >
    <AppSidebar
      :active-page="activePage ?? ''"
      :collapsed="sidebarCollapsed"
      :rail="reportRail"
      :touch-rail="touchRail"
      :administrator="administrator"
      @update:collapsed="sidebarCollapsed = $event"
      @navigate="navigate"
    />

    <div class="flex h-full min-w-0 flex-1 flex-col overflow-hidden">
      <header class="relative z-50 flex h-38 flex-none items-stretch bg-topnav">
        <WorkspaceTabs
          :items="visibleTabs"
          :active-page="activePage ?? ''"
          :administrator="administrator"
          @close="closeTab"
        />
        <AccountMenu ref="accountMenu" :account="account" @action="handleAccountAction" />
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
        <AboutDialog :open="aboutOpen" @close="aboutOpen = false" />
      </main>
    </div>
  </div>
  <AppToast :message="toast" data-testid="toast" />
</template>

<style scoped>
/* 商品量报表在窄屏下允许收缩：body 无法用工具类命中，该覆盖保持未分层
   （unlayered）以压过 @layer base 的 body reset；离开报表路由后属性移除自动失效。 */
:global(body:has([data-fluid-shell])) {
  min-width: 0;
}
</style>
