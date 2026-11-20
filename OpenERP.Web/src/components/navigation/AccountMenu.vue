<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import type { Account } from '../../account/session'
import { useAboutInfo } from '../../account/aboutInfo'
import AppIcon from '../../ui/AppIcon.vue'

defineProps<{ account: Account }>()

const { versionLabel, commitLabel } = useAboutInfo()
const versionText = computed(() =>
  commitLabel.value ? `${versionLabel.value} · ${commitLabel.value}` : versionLabel.value,
)

const emit = defineEmits<{
  action: [action: 'profile' | 'password' | 'settings' | 'logout' | 'about']
}>()

const accountButton = ref<HTMLButtonElement | null>(null)
const accountMenu = ref<HTMLElement | null>(null)
const open = ref(false)

const menuItemClass =
  'group flex h-34 w-full items-center gap-8 rounded-4 px-10 text-left text-12 text-ink hover:bg-primary-faint hover:text-primary'
const menuIconClass = 'size-14 text-menu-icon group-hover:text-primary'
const dangerItemClass =
  'group flex h-34 w-full items-center gap-8 rounded-4 px-10 text-left text-12 text-danger hover:bg-danger-faint'

function choose(action: 'profile' | 'password' | 'settings' | 'logout' | 'about'): void {
  open.value = false
  emit('action', action)
}

function closeOnOutsideClick(event: MouseEvent): void {
  const target = event.target as Node
  if (!accountButton.value?.contains(target) && !accountMenu.value?.contains(target))
    open.value = false
}

function openAndFocusFirst(): void {
  open.value = true
  void nextTick(() => accountMenu.value?.querySelector<HTMLButtonElement>('button')?.focus())
}

function closeAndFocusTrigger(): void {
  open.value = false
  accountButton.value?.focus()
}

function handleMenuKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    closeAndFocusTrigger()
    return
  }
  if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return
  event.preventDefault()
  const items = Array.from(accountMenu.value?.querySelectorAll<HTMLButtonElement>('button') ?? [])
  const current = items.indexOf(document.activeElement as HTMLButtonElement)
  let next = 0
  if (event.key === 'ArrowDown') next = (current + 1 + items.length) % items.length
  else if (event.key === 'ArrowUp') next = (current - 1 + items.length) % items.length
  else if (event.key === 'End') next = items.length - 1
  items[next]?.focus()
}

function focusTrigger(): void {
  accountButton.value?.focus()
}

defineExpose({ focusTrigger })

onMounted(() => document.addEventListener('click', closeOnOutsideClick))
onUnmounted(() => document.removeEventListener('click', closeOnOutsideClick))
</script>

<template>
  <div class="flex h-38 flex-none items-center pl-8 pr-20 text-nav-text">
    <button
      ref="accountButton"
      type="button"
      data-testid="account-button"
      class="ml-7 flex h-32 items-center gap-6 rounded-4 px-8 text-nav-text whitespace-nowrap hover:bg-hover-bg hover:text-ink"
      aria-haspopup="menu"
      :aria-expanded="open"
      aria-controls="accountMenu"
      @click="open = !open"
      @keydown.down.prevent="openAndFocusFirst"
    >
      <span
        class="flex size-20 flex-none items-center justify-center rounded-full bg-avatar-bg text-primary"
      >
        <AppIcon name="i-avatar" class="size-16 [stroke-width:1.7]" />
      </span>
      <span>{{ account.name }}</span>
      <AppIcon
        name="i-chev-down"
        class="size-10 text-caret-soft transition-transform duration-150"
        :class="open ? 'rotate-180' : ''"
      />
    </button>
  </div>

  <div
    v-show="open"
    id="accountMenu"
    ref="accountMenu"
    role="menu"
    class="absolute right-12 top-36 z-[210] w-180 rounded-6 border border-line bg-white p-4 shadow-account-menu"
    @keydown="handleMenuKeydown"
  >
    <div class="flex items-center gap-8 px-10 pb-10 pt-8">
      <span
        class="flex size-28 flex-none items-center justify-center rounded-full bg-nav-active text-primary"
      >
        <AppIcon name="i-avatar" class="size-18 [stroke-width:1.7]" />
      </span>
      <div class="flex min-w-0 flex-col">
        <b class="text-13/18 font-semibold text-ink">{{ account.name }}</b>
        <span class="overflow-hidden text-ellipsis text-11 whitespace-nowrap text-muted">{{
          account.email || account.username
        }}</span>
      </div>
    </div>
    <hr class="mx-6 my-4 h-1 bg-line-soft" />
    <button
      type="button"
      role="menuitem"
      :class="menuItemClass"
      data-account-action="profile"
      @click="choose('profile')"
    >
      <AppIcon name="i-user" :class="menuIconClass" />修改个人信息
    </button>
    <button
      type="button"
      role="menuitem"
      :class="menuItemClass"
      data-account-action="password"
      @click="choose('password')"
    >
      <AppIcon name="i-lock" :class="menuIconClass" />修改密码
    </button>
    <button
      v-if="account.role === 'Administrator'"
      type="button"
      role="menuitem"
      :class="menuItemClass"
      data-account-action="settings"
      @click="choose('settings')"
    >
      <AppIcon name="i-settings" :class="menuIconClass" />系统设置
    </button>
    <hr class="mx-6 my-4 h-1 bg-line-soft" />
    <button
      type="button"
      role="menuitem"
      :class="menuItemClass"
      data-account-action="about"
      @click="choose('about')"
    >
      <AppIcon name="i-help" :class="menuIconClass" />关于
      <span class="ml-auto text-11 text-muted" data-testid="account-version">
        {{ versionText }}
      </span>
    </button>
    <button
      type="button"
      role="menuitem"
      :class="dangerItemClass"
      data-account-action="logout"
      @click="choose('logout')"
    >
      <AppIcon name="i-logout" class="size-14 text-danger" />退出登录
    </button>
  </div>
</template>
