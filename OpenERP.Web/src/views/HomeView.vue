<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { apiRequest } from '../account/session'
import AppIcon from '../components/navigation/AppIcon.vue'

type PingStatus = 'loading' | 'success' | 'error'

const status = ref<PingStatus>('loading')
const responseMessage = ref('')
const errorMessage = ref('')

const statusLabel = computed(() => {
  if (status.value === 'success') return '连接成功'
  if (status.value === 'error') return '连接失败'
  return '正在检测'
})

const statusTagClass = computed(() => {
  if (status.value === 'success') return 'green'
  if (status.value === 'error') return 'red'
  return 'gray'
})

const noticeIcon = computed(() => {
  if (status.value === 'success') return 'i-check'
  if (status.value === 'error') return 'i-warning'
  return 'i-clock'
})

const statusMessage = computed(() => {
  if (status.value === 'success') return `后端响应：${responseMessage.value}`
  if (status.value === 'error') return errorMessage.value
  return '正在请求 /api/ping…'
})

const isLoading = computed(() => status.value === 'loading')

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
  <section class="page page-service active">
    <div class="simple-page service-page">
      <header class="service-head">
        <h1>服务连接</h1>
        <p class="description">检查前端与后端 API 的连接状态。</p>
      </header>

      <div class="service-panel">
        <div class="service-panel__head">
          <span class="service-label">接口</span>
          <code class="service-endpoint">GET /api/ping</code>
          <button
            class="btn primary service-retry"
            type="button"
            :disabled="isLoading"
            :aria-busy="isLoading ? 'true' : undefined"
            @click="checkPing"
          >
            重试
          </button>
        </div>
        <div class="service-panel__body" role="status" aria-live="polite">
          <span class="tag" :class="statusTagClass">{{ statusLabel }}</span>
          <p class="service-message" :class="{ 'is-error': status === 'error' }">
            <AppIcon :name="noticeIcon" icon-class="ic-16" />
            <span>{{ statusMessage }}</span>
          </p>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.service-page {
  display: flex;
  flex-direction: column;
}

.service-head {
  flex: none;
  padding: 0 0 12px;
}

.service-head h1 {
  color: var(--text);
  font-size: 20px;
  line-height: 28px;
  font-weight: 600;
}

.service-head .description {
  margin-top: 4px;
  color: var(--muted);
  font-size: 12px;
  line-height: 18px;
}

.service-panel {
  width: 100%;
  flex: none;
  overflow: hidden;
  border: 1px solid var(--line);
  border-radius: var(--radius-card);
  background: var(--surface);
}

.service-panel__head {
  display: flex;
  min-height: 56px;
  align-items: center;
  gap: 12px;
  padding: 0 20px;
  border-bottom: 1px solid var(--line-soft);
}

.service-label {
  flex: none;
  color: var(--muted);
  font-size: 12px;
  line-height: 18px;
}

.service-endpoint {
  min-width: 0;
  overflow: hidden;
  color: var(--text);
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 12px;
  line-height: 18px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.service-retry {
  margin-left: auto;
}

.service-panel__body {
  display: flex;
  min-height: 56px;
  align-items: flex-start;
  gap: 12px;
  padding: 18px 20px;
}

.service-message {
  display: flex;
  min-width: 0;
  align-items: flex-start;
  gap: 6px;
  color: var(--text);
  font-size: 12px;
  font-weight: 600;
  line-height: 18px;
  overflow-wrap: anywhere;
}

.service-message .ic {
  flex: none;
  margin-top: 1px;
  color: var(--success);
}

.service-message.is-error {
  color: var(--danger);
  font-weight: 400;
}

.service-message.is-error .ic {
  color: var(--danger);
}

@media (max-width: 800px) {
  .service-panel__head {
    align-items: flex-start;
    flex-direction: column;
    gap: 8px;
    padding: 14px 16px;
  }

  .service-retry {
    margin-left: 0;
  }

  .service-panel__body {
    padding: 16px;
  }
}
</style>
