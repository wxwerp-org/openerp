<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { apiRequest } from '../account/session'
import { ElAlert, ElButton, ElCard, ElTag } from 'element-plus'

type PingStatus = 'loading' | 'success' | 'error'

const status = ref<PingStatus>('loading')
const responseMessage = ref('')
const errorMessage = ref('')
const tagType = computed(() => {
  if (status.value === 'success') return 'success'
  if (status.value === 'error') return 'danger'
  return 'info'
})
const statusLabel = computed(() => {
  if (status.value === 'success') return '连接成功'
  if (status.value === 'error') return '连接失败'
  return '正在检测'
})

let requestNumber = 0

async function checkPing(): Promise<void> {
  const currentRequest = ++requestNumber
  status.value = 'loading'
  responseMessage.value = ''
  errorMessage.value = ''

  let response: Response
  try {
    response = await apiRequest('/api/ping')
  } catch {
    if (currentRequest === requestNumber) {
      errorMessage.value = '网络请求失败，请检查前端代理和后端连接。'
      status.value = 'error'
    }
    return
  }

  if (currentRequest !== requestNumber) return
  if (!response.ok) {
    const statusText = response.statusText ? ` ${response.statusText}` : ''
    errorMessage.value = `服务器返回 HTTP ${response.status}${statusText}。`
    status.value = 'error'
    return
  }

  let body: unknown
  try {
    body = await response.json()
  } catch {
    if (currentRequest === requestNumber) {
      errorMessage.value = '服务器响应不是有效的 JSON。'
      status.value = 'error'
    }
    return
  }

  if (currentRequest !== requestNumber) return
  if (
    typeof body !== 'object' ||
    body === null ||
    !('message' in body) ||
    typeof body.message !== 'string'
  ) {
    errorMessage.value = '服务器响应缺少字符串类型的 message 字段。'
    status.value = 'error'
    return
  }

  responseMessage.value = body.message
  status.value = 'success'
}

onMounted(checkPing)
</script>

<template>
  <section class="page-shell">
    <ElCard class="ping-card" shadow="never">
      <div class="card-heading">
        <div>
          <p class="eyebrow">OpenERP</p>
          <h1>服务连接</h1>
          <p class="description">检查前端与后端 API 的连接状态。</p>
        </div>
        <ElTag :type="tagType" effect="light">{{ statusLabel }}</ElTag>
      </div>

      <ElAlert
        v-if="status === 'loading'"
        title="正在请求 /api/ping…"
        type="info"
        :closable="false"
        show-icon
      />
      <ElAlert
        v-else-if="status === 'success'"
        :title="`后端响应：${responseMessage}`"
        type="success"
        :closable="false"
        show-icon
      />
      <ElAlert
        v-else
        :title="errorMessage"
        type="error"
        :closable="false"
        show-icon
      />

      <div class="card-footer">
        <span class="endpoint">GET <code>/api/ping</code></span>
        <ElButton type="primary" :loading="status === 'loading'" @click="checkPing">
          重试
        </ElButton>
      </div>
    </ElCard>
  </section>
</template>

<style scoped>
.page-shell {
  display: grid;
  min-height: 100%;
  padding: clamp(24px, 4vw, 48px) 20px;
  place-items: center;
}

.ping-card {
  width: min(100%, 560px);
  border: 1px solid var(--color-border);
  border-radius: 14px;
}

.ping-card :deep(.el-card__body) {
  padding: 28px;
}

.card-heading,
.card-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
}

.card-heading {
  margin-bottom: 24px;
}

.eyebrow {
  margin: 0 0 8px;
  color: var(--color-muted);
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.page-shell h1 {
  margin: 0;
  color: var(--color-text);
  font-size: 24px;
  font-weight: 650;
  letter-spacing: -0.02em;
}

.description {
  margin: 8px 0 0;
  color: var(--color-muted);
  font-size: 14px;
}

.card-footer {
  margin-top: 24px;
}

.endpoint {
  color: var(--color-muted);
  font-size: 13px;
}

.endpoint code {
  color: var(--color-text);
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
}

@media (max-width: 640px) {
  .ping-card :deep(.el-card__body) {
    padding: 22px;
  }

  .card-heading,
  .card-footer {
    gap: 12px;
  }
}
</style>
