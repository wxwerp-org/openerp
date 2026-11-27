<!--
  平台标签：圆形首字母 logo + 平台名称，按平台品牌色描边。
  标签页、表格、详情里的平台统一用它展示，颜色与 logo 规则见 utils/platform.ts。

  <PlatformTag name="TemuPop" label="Temu 半托管" />
-->
<template>
  <span class="platform-tag" :style="{ '--platform-color': platformColor(name) }">
    <span class="platform-tag__logo" aria-hidden="true">{{ platformInitial(name) }}</span>
    <span class="platform-tag__text">{{ label || name }}</span>
  </span>
</template>

<script setup lang="ts">
  import { platformColor, platformInitial } from '@/utils/platform'

  defineOptions({ name: 'PlatformTag' })

  defineProps<{
    /** 平台标识（决定颜色与 logo 字母），如 TemuPop */
    name: string
    /** 展示名称，缺省使用 name */
    label?: string
  }>()
</script>

<style scoped>
  .platform-tag {
    display: inline-flex;
    gap: 6px;
    align-items: center;
    height: 24px;
    padding: 0 10px 0 3px;
    font-size: 12px;
    font-weight: 500;
    line-height: 1;
    color: var(--platform-color);
    white-space: nowrap;
    background: color-mix(in srgb, var(--platform-color) 8%, transparent);
    border: 1px solid color-mix(in srgb, var(--platform-color) 45%, transparent);
    border-radius: 12px;
  }

  .platform-tag__logo {
    display: inline-flex;
    flex: none;
    align-items: center;
    justify-content: center;
    width: 18px;
    height: 18px;
    font-size: 11px;
    font-weight: 700;
    color: #fff;
    background: var(--platform-color);
    border-radius: 50%;
  }
</style>
