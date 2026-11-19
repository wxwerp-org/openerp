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
        <span class="text-18 font-semibold tracking-[0.3px] text-ink">Open ERP</span>
      </div>
      <div class="w-full rounded-6 border border-line bg-surface p-32 shadow-header"><slot /></div>
    </div>
  </main>
</template>

<style scoped>
/* 登录/初始化页允许窄屏：布局组件挂载时解除桌面固定最小宽度。
   该覆盖必须保持未分层（unlayered），否则会被 legacy 层的 body 规则压回；
   body 无法用工具类命中，故按布局例外保留在此。 */
:global(body:has([data-layout='auth'])) {
  min-width: 0;
  overflow: auto;
}
</style>
