<template>
  <main
    data-layout="auth"
    class="grid min-h-[100dvh] place-items-center bg-page p-[clamp(20px,4vw,48px)]"
  >
    <div class="w-[min(100%,420px)] min-w-0">
      <div class="mb-20 flex items-center justify-center gap-10">
        <svg viewBox="0 0 32 32" aria-hidden="true" class="size-32 flex-none">
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
        <span class="text-18 font-semibold tracking-brand text-ink">Open ERP</span>
      </div>
      <div class="w-full rounded-6 border border-line bg-white p-32 shadow-header"><slot /></div>
      <div class="mt-20 flex items-center justify-center text-11/16 text-muted">
        <slot name="footer" />
      </div>
    </div>
  </main>
</template>

<style scoped>
/* 登录/初始化页允许窄屏：布局组件挂载时解除桌面固定最小宽度与滚动限制。
   body 无法用工具类命中；该覆盖必须保持未分层（unlayered），主题里的 body reset
   位于 @layer base，未分层声明优先级更高，不会因层顺序被压回。 */
:global(body:has([data-layout='auth'])) {
  min-width: 0;
  overflow: auto;
}
</style>
