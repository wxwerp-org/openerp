<script setup lang="ts">
import { onMounted, ref } from 'vue'
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
  <section class="config-group" aria-labelledby="erp-config-title">
    <h2 id="erp-config-title" class="config-group-label">ERP 接入</h2>
    <form class="erp-config-form" @submit.prevent="saveToken">
      <div class="erp-config-status">
        <span>配置状态</span>
        <span class="config-value" role="status">{{ labels[status] }}</span>
        <button v-if="status === 'error'" class="btn" type="button" :disabled="saving" @click="loadStatus">重试</button>
      </div>
      <div>
        <label class="config-label" for="erp-access-token">私有 ERP AccessToken</label>
        <div class="form-control">
          <input
            id="erp-access-token"
            v-model="token"
            type="password"
            autocomplete="new-password"
            :disabled="saving"
            :placeholder="status === 'configured' ? '填写新的 Token' : '填写 Token'"
          >
        </div>
      </div>
      <div class="erp-config-actions">
        <button class="btn primary" type="submit" :disabled="saving || status === 'loading' || !token.trim()">
          {{ saving ? '保存中…' : '保存' }}
        </button>
        <span aria-live="polite">{{ saveMessage }}</span>
      </div>
      <p v-if="saveError" class="erp-config-error" role="alert">{{ saveError }}</p>
    </form>
  </section>
</template>

<style scoped>
.erp-config-form {
  display: grid;
  gap: 16px;
  padding: 20px;
  background: #fff;
  border: 1px solid var(--line);
  border-radius: 4px;
}

.erp-config-status,
.erp-config-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}

.erp-config-form .form-control {
  max-width: 400px;
  margin-top: 6px;
}

.erp-config-error {
  color: var(--danger);
}
</style>
