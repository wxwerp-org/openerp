<!-- 系统 logo：渐变圆角方块里，一个有缺口的圆环（Open）加三根递增的柱子（ERP 数据） -->
<template>
  <div class="flex-cc">
    <svg
      :style="logoStyle"
      class="w-full h-full"
      viewBox="0 0 48 48"
      role="img"
      aria-label="Open ERP"
      xmlns="http://www.w3.org/2000/svg"
    >
      <defs>
        <linearGradient
          id="art-logo-bg"
          x1="6"
          y1="2"
          x2="42"
          y2="46"
          gradientUnits="userSpaceOnUse"
        >
          <stop offset="0" stop-color="#4F8CFF" />
          <stop offset="1" stop-color="#5B3FE0" />
        </linearGradient>
        <linearGradient
          id="art-logo-shine"
          x1="24"
          y1="2"
          x2="24"
          y2="26"
          gradientUnits="userSpaceOnUse"
        >
          <stop offset="0" stop-color="#fff" stop-opacity="0.28" />
          <stop offset="1" stop-color="#fff" stop-opacity="0" />
        </linearGradient>
      </defs>
      <rect x="2" y="2" width="44" height="44" rx="12" fill="url(#art-logo-bg)" />
      <rect x="2" y="2" width="44" height="44" rx="12" fill="url(#art-logo-shine)" />
      <!-- 圆环：缺口朝右上，stroke-dasharray 留出约 25% 的开口 -->
      <circle
        cx="24"
        cy="24"
        r="11"
        fill="none"
        stroke="#fff"
        stroke-width="4"
        stroke-linecap="round"
        stroke-dasharray="44 25.1"
        transform="rotate(15 24 24)"
      />
      <!-- 三根递增的柱子，最高的一根伸进圆环缺口 -->
      <rect x="18.5" y="26" width="3.4" height="5" rx="1.2" fill="#fff" />
      <rect x="23.3" y="21.5" width="3.4" height="9.5" rx="1.2" fill="#fff" />
      <rect x="28.1" y="16.5" width="3.4" height="14.5" rx="1.2" fill="#fff" />
    </svg>
  </div>
</template>

<script setup lang="ts">
  defineOptions({ name: 'ArtLogo' })

  interface Props {
    /** logo 大小 */
    size?: number | string
  }

  const props = withDefaults(defineProps<Props>(), {
    size: 36
  })

  const logoStyle = computed(() => ({ width: `${props.size}px`, height: `${props.size}px` }))
</script>
