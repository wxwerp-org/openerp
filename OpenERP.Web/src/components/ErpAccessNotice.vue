<script setup lang="ts">
import { erpAccessLocation } from '../router/workspace'
import AppButton from '../ui/AppButton.vue'
import AppNotice from '../ui/AppNotice.vue'
</script>

<template>
  <AppNotice
    tone="amber"
    icon="i-warning"
    title="尚未配置私有 ERP AccessToken"
    description="请在系统设置 → 基础 → ERP 接入填写并保存 Token 后重试。"
    role="alert"
  >
    <template #trailing>
      <AppButton :to="erpAccessLocation" class="ml-auto">前往 ERP 接入</AppButton>
    </template>
  </AppNotice>
</template>
