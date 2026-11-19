<script setup lang="ts">
import { computed, nextTick, onUnmounted, ref, watch } from 'vue'
import AppIcon from './AppIcon.vue'

type NavItem = { label: string; page?: string }
type NavGroup = { id: string; label: string; icon: string; items: NavItem[] }

const props = defineProps<{
  activePage: string
  collapsed: boolean
}>()

const emit = defineEmits<{
  'update:collapsed': [value: boolean]
  navigate: [page: string]
  preview: [label: string]
}>()

const groups: NavGroup[] = [
  {
    id: 'product',
    label: '产品',
    icon: 'i-nav-product',
    items: [
      { label: '新品开发', page: 'newdev' },
      { label: '产品管理', page: 'product' },
      { label: '产品采集' },
      { label: '产品资料' },
    ],
  },
  {
    id: 'sales',
    label: '销售',
    icon: 'i-nav-sales',
    items: [
      { label: '订单列表', page: 'orders' },
      { label: '售后订单' },
      { label: '评价管理' },
      { label: '销量分析' },
    ],
  },
  {
    id: 'fba',
    label: 'FBA',
    icon: 'i-nav-fba',
    items: [{ label: 'FBA发货' }, { label: 'FBA库存' }],
  },
  {
    id: 'purchase',
    label: '采购',
    icon: 'i-nav-purchase',
    items: [{ label: '采购单' }, { label: '供应商' }],
  },
  {
    id: 'warehouse',
    label: '仓库',
    icon: 'i-nav-warehouse',
    items: [{ label: '库存管理' }, { label: '出入库流水' }],
  },
  {
    id: 'logistics',
    label: '物流',
    icon: 'i-nav-logistics',
    items: [{ label: '物流跟踪' }, { label: '头程管理' }],
  },
  {
    id: 'ad',
    label: '广告',
    icon: 'i-nav-ad',
    items: [{ label: '广告活动' }, { label: '广告报表' }],
  },
  {
    id: 'service',
    label: '客服',
    icon: 'i-nav-service',
    items: [{ label: '客服邮件' }, { label: '售后工单' }],
  },
  {
    id: 'finance',
    label: '财务',
    icon: 'i-nav-finance',
    items: [{ label: '财务结算' }, { label: '利润报表' }],
  },
  {
    id: 'stats',
    label: '统计',
    icon: 'i-nav-stats',
    items: [
      { label: '销量统计' },
      { label: '产品表现' },
      { label: '商品量报表', page: 'order-report' },
    ],
  },
  { id: 'tools', label: '工具', icon: 'i-nav-tools', items: [{ label: '工具箱' }] },
  {
    id: 'settings',
    label: '设置',
    icon: 'i-nav-settings',
    items: [
      { label: '业务配置', page: 'business' },
      { label: '用户管理', page: 'users' },
      { label: '服务连接', page: 'service-connection' },
      { label: '授权店铺', page: 'shops' },
    ],
  },
]

const collapsedGroups = ref(
  new Set(groups.filter((group) => group.id !== 'product').map((group) => group.id)),
)
const flyoutGroup = ref<string | null>(null)
const flyoutTop = ref(8)
const flyoutElement = ref<HTMLElement | null>(null)
let flyoutCloseTimer: ReturnType<typeof setTimeout> | undefined
let flyoutOpenTimer: ReturnType<typeof setTimeout> | undefined

function isGroupActive(group: NavGroup): boolean {
  return group.items.some((item) => item.page === props.activePage)
}

function cancelFlyoutClose(): void {
  if (flyoutCloseTimer) clearTimeout(flyoutCloseTimer)
  flyoutCloseTimer = undefined
}

function cancelFlyoutOpen(): void {
  if (flyoutOpenTimer) clearTimeout(flyoutOpenTimer)
  flyoutOpenTimer = undefined
}

function closeFlyout(): void {
  cancelFlyoutClose()
  cancelFlyoutOpen()
  flyoutGroup.value = null
}

function closeFlyoutSoon(): void {
  cancelFlyoutClose()
  cancelFlyoutOpen()
  flyoutCloseTimer = setTimeout(closeFlyout, 200)
}

onUnmounted(() => {
  cancelFlyoutClose()
  cancelFlyoutOpen()
})

watch(
  () => props.activePage,
  () => {
    closeFlyout()
    const activeGroup = groups.find(isGroupActive)
    if (activeGroup) collapsedGroups.value.delete(activeGroup.id)
  },
  { immediate: true },
)

function isGroupCollapsed(group: NavGroup): boolean {
  return collapsedGroups.value.has(group.id)
}

function usesRail(): boolean {
  return (
    props.collapsed ||
    (props.activePage === 'order-report' && window.matchMedia('(max-width: 768px)').matches)
  )
}

function setFlyoutElement(element: unknown): void {
  flyoutElement.value = element instanceof HTMLElement ? element : null
}

function showFlyout(group: NavGroup, groupElement: Element): void {
  const groupTop = groupElement.getBoundingClientRect().top
  flyoutTop.value = Math.max(8, groupTop)
  flyoutGroup.value = group.id
  void nextTick(() => {
    if (!flyoutElement.value || flyoutGroup.value !== group.id) return
    const flyoutHeight = flyoutElement.value.getBoundingClientRect().height
    flyoutTop.value = Math.max(8, Math.min(groupTop, window.innerHeight - flyoutHeight - 8))
  })
}

function openFlyout(group: NavGroup, event: MouseEvent | FocusEvent): void {
  cancelFlyoutClose()
  cancelFlyoutOpen()
  const target = event.currentTarget as HTMLElement
  showFlyout(group, target.closest('.nav-group') ?? target)
}

function openFlyoutOnHover(group: NavGroup, event: MouseEvent): void {
  if (!flyoutGroup.value || flyoutGroup.value === group.id) {
    openFlyout(group, event)
    return
  }
  cancelFlyoutClose()
  cancelFlyoutOpen()
  const target = event.currentTarget as HTMLElement
  const groupElement = target.closest('.nav-group') ?? target
  flyoutOpenTimer = setTimeout(() => {
    flyoutOpenTimer = undefined
    showFlyout(group, groupElement)
  }, 200)
}

function toggleGroup(group: NavGroup, event: MouseEvent): void {
  if (props.collapsed) {
    emit('update:collapsed', false)
    collapsedGroups.value.delete(group.id)
    return
  }
  if (usesRail()) {
    openFlyout(group, event)
    return
  }
  if (collapsedGroups.value.has(group.id)) collapsedGroups.value.delete(group.id)
  else collapsedGroups.value.add(group.id)
}

function navigate(page?: string, label?: string): void {
  closeFlyout()
  if (page) emit('navigate', page)
  else if (label) emit('preview', label)
}

function openFlyoutOnFocus(group: NavGroup, event: FocusEvent): void {
  if (usesRail()) openFlyout(group, event)
}

function closeFlyoutOnFocusOut(_group: NavGroup, event: FocusEvent): void {
  const container = event.currentTarget as HTMLElement
  if (!container.contains(event.relatedTarget as Node | null)) closeFlyout()
}

const hasFlyout = computed(() => flyoutGroup.value !== null)
</script>

<template>
  <aside class="sidebar" :class="{ 'is-rail': collapsed }" aria-label="主导航">
    <button
      class="brand"
      type="button"
      data-nav-page="dashboard"
      title="回到首页"
      aria-label="回到首页"
      @click="navigate('dashboard')"
    >
      <svg viewBox="0 0 32 32" aria-hidden="true">
        <path
          d="M4.7 25.9 12.9 6.2c1.2-2.9 5.1-2.9 6.4 0l2.1 5"
          fill="none"
          stroke="#0968FF"
          stroke-width="4.2"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
        <path
          d="M8.7 26 19.3 6.3c1-1.8 3.7-1.6 4.6.3 1 2.1 2.2 4.9 3.1 7.5.7 1.8.5 3.9-.5 5.5l-2.3 3.3"
          fill="none"
          stroke="#0968FF"
          stroke-width="4"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
        <path
          d="M9.7 19.3h8.1"
          fill="none"
          stroke="#0968FF"
          stroke-width="3.6"
          stroke-linecap="round"
        />
      </svg>
      <span class="brand-name">Open ERP</span>
    </button>

    <nav class="side-nav">
      <button
        class="nav-link"
        :class="{ active: activePage === 'dashboard' }"
        type="button"
        data-nav-page="dashboard"
        @click="navigate('dashboard')"
      >
        <AppIcon name="i-nav-home" /><span>工作台</span>
      </button>

      <div
        v-for="group in groups"
        :key="group.id"
        class="nav-group"
        :class="{ collapsed: isGroupCollapsed(group), active: isGroupActive(group) }"
        :data-group="group.id"
        @mouseenter="usesRail() && openFlyoutOnHover(group, $event)"
        @mouseleave="closeFlyoutSoon"
        @focusin="openFlyoutOnFocus(group, $event)"
        @focusout="closeFlyoutOnFocusOut(group, $event)"
        @keydown.esc.stop="closeFlyout"
      >
        <button
          class="nav-group-head"
          type="button"
          :aria-expanded="!isGroupCollapsed(group)"
          @click="toggleGroup(group, $event)"
        >
          <AppIcon :name="group.icon" />
          <span class="nav-group-label">{{ group.label }}</span>
          <AppIcon name="i-chev-down" icon-class="nav-caret" />
        </button>
        <div class="nav-sub">
          <button
            v-for="item in group.items"
            :key="item.label"
            class="nav-sub-item"
            :class="{ active: item.page === activePage }"
            type="button"
            :data-nav-page="item.page ?? ''"
            @click="navigate(item.page, item.label)"
          >
            {{ item.label }}
          </button>
        </div>
        <div
          v-if="hasFlyout && flyoutGroup === group.id"
          :ref="setFlyoutElement"
          class="nav-flyout open"
          :style="{ top: `${flyoutTop}px` }"
        >
          <div class="nav-flyout-title">{{ group.label }}</div>
          <button
            v-for="item in group.items"
            :key="item.label"
            class="nav-sub-item"
            type="button"
            @click="navigate(item.page, item.label)"
          >
            {{ item.label }}
          </button>
        </div>
      </div>
    </nav>

    <div class="sidebar-foot">
      <button
        id="collapseNav"
        class="collapse-btn"
        type="button"
        :title="collapsed ? '展开导航' : '收起导航'"
        :aria-label="collapsed ? '展开导航' : '收起导航'"
        @click="emit('update:collapsed', !collapsed)"
      >
        <AppIcon name="i-collapse" />
      </button>
    </div>
  </aside>
</template>

<style scoped>
.nav-flyout {
  left: 48px;
  max-height: calc(100dvh - 16px);
  overflow-y: auto;
}
</style>
