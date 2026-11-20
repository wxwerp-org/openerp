<script setup lang="ts">
import { computed, nextTick, onUnmounted, ref, watch } from 'vue'
import AppIcon from '../../ui/AppIcon.vue'

type NavItem = { label: string; page?: string }
type NavGroup = { id: string; label: string; icon: string; items: NavItem[] }

const props = withDefaults(
  defineProps<{
    activePage: string
    collapsed: boolean
    /** 视口强制轨道（报表页窄屏）：布局按轨道呈现，但手动折叠状态保持不变。 */
    rail?: boolean
    /** 报表页窄屏 + 触屏时轨道目标放大到 44px（WCAG 2.5.5）。 */
    touchRail?: boolean
  }>(),
  { rail: false, touchRail: false },
)

const isRail = computed(() => props.collapsed || props.rail)

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

/** 轨道态下导航分组以浮层展开（手动折叠与报表页窄屏自动轨道一致）。 */
function usesRail(): boolean {
  return isRail.value
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

function isGroupCollapsed(group: NavGroup): boolean {
  return collapsedGroups.value.has(group.id)
}

function openFlyout(group: NavGroup, event: MouseEvent | FocusEvent): void {
  cancelFlyoutClose()
  cancelFlyoutOpen()
  showFlyout(group, event.currentTarget as Element)
}

function openFlyoutOnHover(group: NavGroup, event: MouseEvent): void {
  if (!flyoutGroup.value || flyoutGroup.value === group.id) {
    openFlyout(group, event)
    return
  }
  cancelFlyoutClose()
  cancelFlyoutOpen()
  const groupElement = event.currentTarget as Element
  flyoutOpenTimer = setTimeout(() => {
    flyoutOpenTimer = undefined
    showFlyout(group, groupElement)
  }, 200)
}

function toggleGroup(group: NavGroup, event: MouseEvent): void {
  // 仅手动折叠时才展开侧栏；视口强制轨道（报表窄屏）下无法真正展开，
  // 点击分组应打开子项浮层，避免点击无响应。
  if (props.collapsed && !props.rail) {
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

/** 折叠轨道与展开两套布局值必须整体二选一，避免同属性工具类互相覆盖。 */
const railItemLayout = computed(() =>
  props.touchRail ? 'mx-auto size-44 justify-center p-0' : 'mx-auto h-40 w-40 justify-center p-0',
)
const expandedItemLayout = 'h-38 w-full gap-10 px-8 [@media(max-height:760px)]:h-36'

/** 折叠轨道高度在旧样式里由更高特异性覆盖，短视口媒体查询不应压低它。 */
function headLayout(): string[] {
  return isRail.value ? [railItemLayout.value] : [expandedItemLayout]
}

function linkState(active: boolean): string[] {
  return active
    ? ['bg-nav-active font-semibold text-primary']
    : ['text-nav-text hover:bg-nav-hover hover:text-ink']
}

function headState(active: boolean): string[] {
  if (props.collapsed) {
    return active
      ? ['bg-nav-active text-primary']
      : ['text-nav-text hover:bg-nav-hover hover:text-ink']
  }
  return active
    ? ['bg-nav-hover font-semibold text-ink']
    : ['text-nav-text hover:bg-nav-hover hover:text-ink']
}

function headIconState(active: boolean): string[] {
  return active ? ['text-primary'] : ['text-nav-icon group-hover:text-nav-text']
}

function subItemLayout(inFlyout: boolean): string[] {
  return inFlyout ? ['w-full px-8'] : ['pl-34 pr-8']
}

function subItemState(active: boolean): string[] {
  return active
    ? ['bg-nav-active font-semibold text-primary']
    : ['text-body hover:bg-nav-hover hover:text-ink']
}
</script>

<template>
  <aside
    class="relative z-[60] flex h-full flex-none flex-col border-r border-border-nav bg-nav transition-[width] duration-180 ease-[ease]"
    :class="isRail ? 'w-56' : 'w-200'"
    aria-label="主导航"
    data-testid="sidebar"
    :data-rail="isRail ? 'true' : 'false'"
  >
    <button
      class="flex h-56 flex-none items-center gap-10 text-primary"
      :class="isRail ? 'justify-center px-0' : 'px-16'"
      type="button"
      data-nav-page="dashboard"
      title="回到首页"
      aria-label="回到首页"
      @click="navigate('dashboard')"
    >
      <svg viewBox="0 0 32 32" aria-hidden="true" class="size-28 flex-none">
        <path
          d="M4.7 25.9 12.9 6.2c1.2-2.9 5.1-2.9 6.4 0l2.1 5"
          fill="none"
          class="stroke-brand"
          stroke-width="4.2"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
        <path
          d="M8.7 26 19.3 6.3c1-1.8 3.7-1.6 4.6.3 1 2.1 2.2 4.9 3.1 7.5.7 1.8.5 3.9-.5 5.5l-2.3 3.3"
          fill="none"
          class="stroke-brand"
          stroke-width="4"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
        <path
          d="M9.7 19.3h8.1"
          fill="none"
          class="stroke-brand"
          stroke-width="3.6"
          stroke-linecap="round"
        />
      </svg>
      <span
        class="whitespace-nowrap text-14/[21px] font-semibold tracking-[0.3px] text-ink"
        :class="isRail ? 'hidden' : ''"
      >
        Open ERP
      </span>
    </button>

    <nav
      class="side-nav min-h-0 flex-1 overflow-y-auto overflow-x-hidden px-8 pb-10 pt-6"
      data-testid="side-nav"
    >
      <button
        class="group flex items-center rounded-4 text-left text-13/[19.5px] whitespace-nowrap transition-[background,color] duration-150"
        :class="[...headLayout(), ...linkState(activePage === 'dashboard')]"
        type="button"
        data-nav-page="dashboard"
        :aria-current="activePage === 'dashboard' ? 'page' : undefined"
        @click="navigate('dashboard')"
      >
        <AppIcon
          name="i-nav-home"
          class="size-18 flex-none [stroke-width:1.55] [@media(max-height:760px)]:size-16"
          :class="headIconState(activePage === 'dashboard')"
        />
        <span :class="isRail ? 'hidden' : ''">工作台</span>
      </button>

      <div
        v-for="group in groups"
        :key="group.id"
        :class="collapsed ? 'mt-4' : ''"
        :data-group="group.id"
        @mouseenter="usesRail() && openFlyoutOnHover(group, $event)"
        @mouseleave="closeFlyoutSoon"
        @focusin="openFlyoutOnFocus(group, $event)"
        @focusout="closeFlyoutOnFocusOut(group, $event)"
        @keydown.esc.stop="closeFlyout"
      >
        <button
          class="group flex items-center rounded-4 text-left text-13/[19.5px] whitespace-nowrap transition-[background,color] duration-150"
          :class="[...headLayout(), ...headState(isGroupActive(group))]"
          type="button"
          :aria-expanded="isRail ? flyoutGroup === group.id : !isGroupCollapsed(group)"
          @click="toggleGroup(group, $event)"
        >
          <AppIcon
            :name="group.icon"
            class="size-18 flex-none [stroke-width:1.55] [@media(max-height:760px)]:size-16"
            :class="headIconState(isGroupActive(group))"
          />
          <span
            class="min-w-0 flex-1 overflow-hidden text-ellipsis"
            :class="isRail ? 'hidden' : ''"
            >{{ group.label }}</span
          >
          <AppIcon
            name="i-chev-down"
            class="ml-auto size-14 transition-transform duration-150 [@media(max-height:760px)]:size-16"
            :class="[
              isRail ? 'hidden' : isGroupCollapsed(group) ? '[transform:rotate(-90deg)]' : '',
              isGroupActive(group) ? 'text-primary' : 'text-nav-icon-muted',
            ]"
          />
        </button>
        <div
          class="grid grid-cols-[minmax(0,1fr)] gap-2 pb-8 pt-3"
          :class="isRail || isGroupCollapsed(group) ? 'hidden' : ''"
        >
          <button
            v-for="item in group.items"
            :key="item.label"
            class="flex h-32 items-center overflow-hidden text-ellipsis rounded-4 text-left text-13/[19.5px] whitespace-nowrap transition-[background,color] duration-150 [@media(max-height:760px)]:h-28"
            :class="[...subItemLayout(false), ...subItemState(item.page === activePage)]"
            type="button"
            :data-nav-page="item.page ?? ''"
            :aria-current="item.page === activePage ? 'page' : undefined"
            @click="navigate(item.page, item.label)"
          >
            {{ item.label }}
          </button>
        </div>
        <div
          v-if="hasFlyout && flyoutGroup === group.id"
          :ref="setFlyoutElement"
          class="fixed left-48 z-[300] max-h-[calc(100dvh-16px)] w-max min-w-150 max-w-220 overflow-y-auto rounded-6 border border-line bg-white p-6 shadow-menu"
          :style="{ top: `${flyoutTop}px` }"
          data-testid="nav-flyout"
        >
          <div class="px-8 pb-6 pt-2 text-12 font-semibold text-sidebar-foot">
            {{ group.label }}
          </div>
          <button
            v-for="item in group.items"
            :key="item.label"
            class="flex h-32 items-center overflow-hidden text-ellipsis rounded-4 text-left text-13/[19.5px] whitespace-nowrap transition-[background,color] duration-150 [@media(max-height:760px)]:h-28"
            :class="[...subItemLayout(true), ...subItemState(item.page === activePage)]"
            type="button"
            @click="navigate(item.page, item.label)"
          >
            {{ item.label }}
          </button>
        </div>
      </div>
    </nav>

    <div
      class="flex h-48 w-full flex-none items-center text-sidebar-foot"
      :class="isRail ? 'justify-center pr-0' : 'justify-end pr-14'"
    >
      <button
        class="flex size-32 items-center justify-center rounded-4 hover:bg-hover-bg hover:text-nav-text"
        type="button"
        data-testid="collapse-nav"
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
.side-nav {
  scrollbar-width: thin;
  scrollbar-color: var(--color-scroll-thumb-nav) transparent;
}
.side-nav::-webkit-scrollbar {
  width: 6px;
}
.side-nav::-webkit-scrollbar-thumb {
  background: var(--color-scroll-thumb-nav);
  border-radius: 3px;
}
</style>
