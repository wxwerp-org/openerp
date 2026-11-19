<script setup lang="ts">
import { onMounted, ref } from 'vue'
import AppButton from '../ui/AppButton.vue'
import AppInput from '../ui/AppInput.vue'
import { apiRequest } from '../account/session'

const labels = {
  loading: '正在读取',
  configured: '已配置',
  unconfigured: '未配置',
  error: '读取失败',
}

const status = ref<keyof typeof labels>('loading')
const token = ref('')
const saving = ref(false)
const saveMessage = ref('')
const saveError = ref('')

async function loadStatus(): Promise<void> {
  status.value = 'loading'
  try {
    const response = await apiRequest('/api/erp/config', { cache: 'no-store' })
    const config = response.ok ? await response.json() : null
    if (typeof config?.configured !== 'boolean') {
      status.value = 'error'
      return
    }
    status.value = config.configured ? 'configured' : 'unconfigured'
  } catch {
    status.value = 'error'
  }
}

async function saveToken(): Promise<void> {
  if (saving.value || status.value === 'loading' || !token.value.trim()) return

  saving.value = true
  saveMessage.value = ''
  saveError.value = ''
  try {
    const response = await apiRequest('/api/erp/config', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ accessToken: token.value }),
    })
    if (response.status === 204) {
      token.value = ''
      status.value = 'configured'
      saveMessage.value = '保存成功'
    } else {
      saveError.value = '保存失败，请重试。'
    }
  } catch {
    saveError.value = '保存失败，请重试。'
  } finally {
    saving.value = false
  }
}

onMounted(loadStatus)
</script>

<template>
  <section class="mt-20 first:mt-0" aria-labelledby="erp-config-title">
    <h2 id="erp-config-title" class="mb-8 text-12/18 font-bold text-ink">ERP 接入</h2>
    <form
      class="grid gap-16 rounded-4 border border-line bg-white p-20"
      @submit.prevent="saveToken"
    >
      <div class="flex items-center gap-12" data-testid="erp-config-status">
        <span>配置状态</span>
        <span class="text-12/22 font-bold text-body-alt" role="status">{{ labels[status] }}</span>
        <AppButton v-if="status === 'error'" :disabled="saving" @click="loadStatus">重试</AppButton>
      </div>
      <div>
        <label class="pr-12 text-13/22 text-ink" for="erp-access-token">私有 ERP AccessToken</label>
        <AppInput
          id="erp-access-token"
          v-model="token"
          class="mt-6 max-w-400"
          type="password"
          autocomplete="new-password"
          :disabled="saving"
          :placeholder="status === 'configured' ? '填写新的 Token' : '填写 Token'"
        />
      </div>
      <div class="flex items-center gap-12">
        <AppButton
          variant="primary"
          type="submit"
          :disabled="saving || status === 'loading' || !token.trim()"
        >
          {{ saving ? '保存中…' : '保存' }}
        </AppButton>
        <span aria-live="polite">{{ saveMessage }}</span>
      </div>
      <p v-if="saveError" class="text-danger" role="alert">{{ saveError }}</p>
    </form>
  </section>
</template>
