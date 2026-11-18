<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref } from 'vue'
import type { Account } from '../../account/session'
import AppIcon from './AppIcon.vue'

defineProps<{ account: Account }>()

const emit = defineEmits<{
  action: [action: 'profile' | 'password' | 'settings' | 'logout']
  notice: []
}>()

const accountButton = ref<HTMLButtonElement | null>(null)
const accountMenu = ref<HTMLElement | null>(null)
const open = ref(false)

function choose(action: 'profile' | 'password' | 'settings' | 'logout'): void {
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
  <div class="top-actions">
    <button class="top-icon" type="button" title="通知" aria-label="通知" @click="emit('notice')">
      <AppIcon name="i-bell" />
    </button>
    <button
      id="accountBtn"
      ref="accountButton"
      class="account-button"
      type="button"
      aria-haspopup="menu"
      :aria-expanded="open"
      aria-controls="accountMenu"
      @click="open = !open"
      @keydown.down.prevent="openAndFocusFirst"
    >
      <span class="avatar"><AppIcon name="i-avatar" /></span>
      <span>{{ account.name }}</span>
      <AppIcon name="i-chev-down" icon-class="caret" />
    </button>
  </div>

  <div
    v-show="open"
    id="accountMenu"
    ref="accountMenu"
    class="account-menu"
    role="menu"
    @keydown="handleMenuKeydown"
  >
    <div class="account-menu-head">
      <span class="avatar"><AppIcon name="i-avatar" /></span>
      <div class="account-menu-user">
        <b>{{ account.name }}</b
        ><span>{{ account.email || account.username }}</span>
      </div>
    </div>
    <hr class="account-menu-sep" />
    <button type="button" role="menuitem" data-account-action="profile" @click="choose('profile')">
      <AppIcon name="i-user" />修改个人信息
    </button>
    <button
      type="button"
      role="menuitem"
      data-account-action="password"
      @click="choose('password')"
    >
      <AppIcon name="i-lock" />修改密码
    </button>
    <button
      type="button"
      role="menuitem"
      data-account-action="settings"
      @click="choose('settings')"
    >
      <AppIcon name="i-settings" />系统设置
    </button>
    <hr class="account-menu-sep" />
    <button
      type="button"
      role="menuitem"
      class="danger"
      data-account-action="logout"
      @click="choose('logout')"
    >
      <AppIcon name="i-logout" />退出登录
    </button>
  </div>
</template>
