<script setup lang="ts">
import { computed } from 'vue'
import { account } from '../account/session'
import { erpAccessLocation } from '../router/workspace'
import AppButton from '../ui/AppButton.vue'
import AppNotice from '../ui/AppNotice.vue'

const isAdministrator = computed(() => account.value?.role === 'Administrator')
</script>

<template>
  <AppNotice
    tone="amber"
    icon="i-warning"
    title="尚未配置私有 ERP AccessToken"
    :description="
      isAdministrator
        ? '请在系统设置 → 基础 → ERP 接入填写并保存 Token 后重试。'
        : '请联系管理员配置 ERP 接入后重试。'
    "
    role="alert"
  >
    <template v-if="isAdministrator" #trailing>
      <AppButton :to="erpAccessLocation" class="ml-auto">前往 ERP 接入</AppButton>
    </template>
  </AppNotice>
</template>
