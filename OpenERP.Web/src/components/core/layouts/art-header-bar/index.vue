<!-- 顶部栏 -->
<template>
  <div
    class="w-full bg-[var(--default-bg-color)]"
    :class="[
      tabStyle === 'tab-card' || tabStyle === 'tab-google' ? 'mb-5 max-sm:mb-3 !bg-box' : ''
    ]"
  >
    <div
      class="relative box-border flex-b h-15 leading-15 select-none"
      :class="[
        tabStyle === 'tab-card' || tabStyle === 'tab-google'
          ? 'border-b border-[var(--art-card-border)]'
          : ''
      ]"
    >
      <div class="flex-c flex-1 min-w-0 leading-15" style="display: flex">
        <!-- 系统信息  -->
        <div class="flex-c c-p" @click="toHome" v-if="isTopMenu">
          <ArtLogo class="pl-4.5" />
        </div>

        <ArtLogo
          class="!hidden pl-3.5 overflow-hidden align-[-0.15em] fill-current"
          @click="toHome"
        />

        <!-- 菜单按钮 -->
        <ArtIconButton
          v-if="isLeftMenu && shouldShowMenuButton"
          icon="ri:menu-2-fill"
          class="ml-3 max-sm:ml-[7px]"
          @click="visibleMenu"
        />

        <!-- 刷新按钮 -->
        <ArtIconButton
          v-if="shouldShowRefreshButton"
          icon="ri:refresh-line"
          class="!ml-3 refresh-btn max-sm:!hidden"
          :style="{ marginLeft: !isLeftMenu ? '10px' : '0' }"
          @click="reload"
        />

        <!-- 快速入口 -->
        <ArtFastEnter v-if="shouldShowFastEnter && width >= headerBarFastEnterMinWidth">
          <ArtIconButton icon="ri:function-line" class="ml-3" />
        </ArtFastEnter>

        <!-- 面包屑 -->
        <ArtBreadcrumb
          v-if="(shouldShowBreadcrumb && isLeftMenu) || (shouldShowBreadcrumb && isDualMenu)"
        />

        <!-- 顶部菜单 -->
        <ArtHorizontalMenu v-if="isTopMenu" :list="filteredMenuList" />

        <!-- 混合菜单-顶部 -->
        <ArtMixedMenu v-if="isTopLeftMenu" :list="filteredMenuList" />
      </div>

      <div class="flex-c gap-2.5">
        <!-- 调试模式切换按钮 -->
        <ArtIconButton
          :icon="debugMode ? 'ri:bug-line' : 'ri:bug-fill'"
          :class="debugMode ? 'text-success' : 'text-g-500'"
          :title="debugMode ? '调试模式：开启' : '调试模式：关闭'"
          @click="toggleDebugMode"
        />

        <!-- 客服按钮 -->
        <ElPopover
          placement="bottom-end"
          :width="200"
          trigger="click"
          popper-class="customer-service-popover"
        >
          <template #reference>
            <ArtIconButton
              icon="ri:customer-service-2-line"
              class="customer-service-btn"
              title="联系客服"
            />
          </template>
          <template #default>
            <div class="p-4 text-center">
              <p class="mb-3 text-sm font-medium text-g-700">添加客服微信了解更多</p>
              <div class="flex justify-center">
                <img
                  src="https://wework.qpic.cn/wwpic/619421_CQxf5ZDCQFWrXkP_1609824875/0"
                  alt="客服微信"
                  class="w-32 h-32 object-contain"
                />
              </div>
              <p class="mt-3 text-xs text-g-500">扫码添加客服微信</p>
            </div>
          </template>
        </ElPopover>

        <!-- API 权限绑定（管理员） -->
        <button
          v-roles="'R_SUPER'"
          type="button"
          class="erp-binding-btn flex-c gap-1 px-2 h-8 text-sm text-g-700 rounded-md cursor-pointer hover:bg-g-200"
          @click="router.push('/system/erp-binding')"
        >
          <ArtSvgIcon icon="ri:plug-line" class="text-[18px]" />
          <span>{{ $t('menus.system.erpBinding') }}</span>
        </button>

        <!-- 国际化按钮 -->
        <ElDropdown
          @command="changeLanguage"
          popper-class="langDropDownStyle"
          v-if="shouldShowLanguage"
        >
          <ArtIconButton icon="ri:translate-2" class="language-btn text-[19px]" />
          <template #dropdown>
            <ElDropdownMenu>
              <div v-for="item in languageOptions" :key="item.value" class="lang-btn-item">
                <ElDropdownItem
                  :command="item.value"
                  :class="{ 'is-selected': locale === item.value }"
                >
                  <span class="menu-txt">{{ item.label }}</span>
                  <ArtSvgIcon icon="ri:check-fill" v-if="locale === item.value" />
                </ElDropdownItem>
              </div>
            </ElDropdownMenu>
          </template>
        </ElDropdown>

        <!-- 用户头像、菜单 -->
        <ArtUserMenu />
      </div>
    </div>

    <!-- 通知 -->
    <ArtNotification v-model:value="showNotice" ref="notice" />
  </div>
</template>

<script setup lang="ts">
  import { useI18n } from 'vue-i18n'
  import { useRouter } from 'vue-router'
  import { useWindowSize } from '@vueuse/core'
  import { LanguageEnum, MenuTypeEnum } from '@/enums/appEnum'
  import { useSettingStore } from '@/store/modules/setting'
  import { useUserStore } from '@/store/modules/user'
  import { useMenuStore } from '@/store/modules/menu'
  import { languageOptions } from '@/locales'
  import { useCommon } from '@/hooks/core/useCommon'
  import { useHeaderBar } from '@/hooks/core/useHeaderBar'
  import ArtUserMenu from './widget/ArtUserMenu.vue'

  defineOptions({ name: 'ArtHeaderBar' })

  const router = useRouter()
  const { locale } = useI18n()
  const { width } = useWindowSize()

  const settingStore = useSettingStore()
  const userStore = useUserStore()
  const menuStore = useMenuStore()

  // 顶部栏功能配置
  const {
    shouldShowMenuButton,
    shouldShowRefreshButton,
    shouldShowFastEnter,
    shouldShowBreadcrumb,
    shouldShowLanguage,
    fastEnterMinWidth: headerBarFastEnterMinWidth
  } = useHeaderBar()

  const { menuOpen, menuType, tabStyle, debugMode } = storeToRefs(settingStore)

  const { language } = storeToRefs(userStore)
  const { filteredMenuList } = storeToRefs(menuStore)

  const showNotice = ref(false)
  const notice = ref(null)

  // 菜单类型判断
  const isLeftMenu = computed(() => menuType.value === MenuTypeEnum.LEFT)
  const isDualMenu = computed(() => menuType.value === MenuTypeEnum.DUAL_MENU)
  const isTopMenu = computed(() => menuType.value === MenuTypeEnum.TOP)
  const isTopLeftMenu = computed(() => menuType.value === MenuTypeEnum.TOP_LEFT)

  onMounted(() => {
    initLanguage()
    document.addEventListener('click', bodyCloseNotice)
  })

  onUnmounted(() => {
    document.removeEventListener('click', bodyCloseNotice)
  })

  /**
   * 切换菜单显示/隐藏状态
   */
  const visibleMenu = (): void => {
    settingStore.setMenuOpen(!menuOpen.value)
  }

  const { homePath } = useCommon()
  const { refresh } = useCommon()

  /**
   * 跳转到首页
   */
  const toHome = (): void => {
    router.push(homePath.value)
  }

  /**
   * 刷新页面
   * @param {number} time - 延迟时间，默认为0毫秒
   */
  const reload = (time: number = 0): void => {
    setTimeout(() => {
      refresh()
    }, time)
  }

  /**
   * 初始化语言设置
   */
  const initLanguage = (): void => {
    locale.value = language.value
  }

  /**
   * 切换系统语言
   * @param {LanguageEnum} lang - 目标语言类型
   */
  const changeLanguage = (lang: LanguageEnum): void => {
    if (locale.value === lang) return
    locale.value = lang
    userStore.setLanguage(lang)
    reload(50)
  }

  /**
   * 点击页面其他区域关闭通知面板
   * @param {Event} e - 点击事件对象
   */
  const bodyCloseNotice = (e: any): void => {
    if (!showNotice.value) return

    const target = e.target as HTMLElement

    // 检查是否点击了通知按钮或通知面板内部
    const isNoticeButton = target.closest('.notice-button')
    const isNoticePanel = target.closest('.art-notification-panel')

    if (!isNoticeButton && !isNoticePanel) {
      showNotice.value = false
    }
  }

  /**
   * 切换调试模式
   */
  const toggleDebugMode = (): void => {
    settingStore.toggleDebugMode()
  }
</script>

<style lang="scss" scoped>
  /* Custom animations */
  @keyframes rotate180 {
    0% {
      transform: rotate(0);
    }

    100% {
      transform: rotate(180deg);
    }
  }

  @keyframes shake {
    0% {
      transform: rotate(0);
    }

    25% {
      transform: rotate(-5deg);
    }

    50% {
      transform: rotate(5deg);
    }

    75% {
      transform: rotate(-5deg);
    }

    100% {
      transform: rotate(0);
    }
  }

  @keyframes expand {
    0% {
      transform: scale(1);
    }

    50% {
      transform: scale(1.1);
    }

    100% {
      transform: scale(1);
    }
  }

  @keyframes shrink {
    0% {
      transform: scale(1);
    }

    50% {
      transform: scale(0.9);
    }

    100% {
      transform: scale(1);
    }
  }

  @keyframes moveUp {
    0% {
      transform: translateY(0);
    }

    50% {
      transform: translateY(-3px);
    }

    100% {
      transform: translateY(0);
    }
  }

  @keyframes breathing {
    0% {
      opacity: 0.4;
      transform: scale(0.9);
    }

    50% {
      opacity: 1;
      transform: scale(1.1);
    }

    100% {
      opacity: 0.4;
      transform: scale(0.9);
    }
  }

  /* Hover animation classes */
  .refresh-btn:hover :deep(.art-svg-icon) {
    animation: rotate180 0.5s;
  }

  .language-btn:hover :deep(.art-svg-icon) {
    animation: moveUp 0.4s;
  }

  .setting-btn:hover :deep(.art-svg-icon) {
    animation: rotate180 0.5s;
  }

  .full-screen-btn:hover :deep(.art-svg-icon) {
    animation: expand 0.6s forwards;
  }

  .exit-full-screen-btn:hover :deep(.art-svg-icon) {
    animation: shrink 0.6s forwards;
  }

  .notice-button:hover :deep(.art-svg-icon) {
    animation: shake 0.5s ease-in-out;
  }

  .chat-button:hover :deep(.art-svg-icon) {
    animation: shake 0.5s ease-in-out;
  }

  /* Breathing animation for chat dot */
  .breathing-dot {
    animation: breathing 1.5s ease-in-out infinite;
  }

  /* iPad breakpoint adjustments */
  @media screen and (width <= 768px) {
    .logo2 {
      display: block !important;
    }
  }

  @media screen and (width <= 640px) {
    .btn-box {
      width: 40px;
    }
  }
</style>
