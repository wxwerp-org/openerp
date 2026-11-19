<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { apiRequest } from '../account/session'
import AppButton from '../ui/AppButton.vue'
import AppCard from '../ui/AppCard.vue'
import AppIcon from '../ui/AppIcon.vue'
import AppTag from '../ui/AppTag.vue'

type PingStatus = 'loading' | 'success' | 'error'
type TagTone = 'green' | 'red' | 'gray'

const status = ref<PingStatus>('loading')
const responseMessage = ref('')
const errorMessage = ref('')

const statusLabel = computed(() => {
  if (status.value === 'success') return '连接成功'
  if (status.value === 'error') return '连接失败'
  return '正在检测'
})

const statusTone = computed<TagTone>(() => {
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

const messageToneClass = computed(() =>
  status.value === 'error' ? 'text-danger' : 'font-semibold text-ink',
)

const messageIconToneClass = computed(() =>
  status.value === 'error' ? 'text-danger' : 'text-success',
)

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
  <section class="flex h-full min-h-0 w-full flex-col overflow-y-auto bg-page">
    <div class="flex min-h-full w-full flex-col bg-page p-16">
      <header data-testid="service-head" class="flex-none pb-12">
        <h1 class="text-20/28 font-semibold text-ink">服务连接</h1>
        <p class="mt-4 text-12/18 text-muted">检查前端与后端 API 的连接状态。</p>
      </header>

      <AppCard data-testid="service-panel" class="flex-none">
        <div
          data-testid="service-panel-head"
          class="flex min-h-54 items-center gap-12 px-16 [@media(max-width:800px)]:flex-col [@media(max-width:800px)]:items-start [@media(max-width:800px)]:gap-8 [@media(max-width:800px)]:py-14"
        >
          <span data-testid="service-label" class="flex-none text-14/20 font-bold text-ink">
            接口
          </span>
          <code class="min-w-0 overflow-hidden truncate font-mono text-12/18 text-ink">
            GET /api/ping
          </code>
          <AppButton
            data-testid="service-retry"
            variant="primary"
            class="ml-auto focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary [@media(max-width:800px)]:ml-0"
            type="button"
            :disabled="isLoading"
            :aria-busy="isLoading ? 'true' : undefined"
            @click="checkPing"
          >
            重试
          </AppButton>
        </div>
        <div
          data-testid="service-panel-body"
          role="status"
          aria-live="polite"
          class="flex min-h-56 items-start gap-12 bg-white px-16 pt-4 pb-20 [@media(max-width:800px)]:p-16"
        >
          <AppTag data-testid="service-tag" :tone="statusTone">{{ statusLabel }}</AppTag>
          <p
            data-testid="service-message"
            :data-tone="status"
            class="flex min-w-0 items-start gap-6 text-12/18 [overflow-wrap:anywhere]"
            :class="messageToneClass"
          >
            <AppIcon
              :name="noticeIcon"
              :size="16"
              class="mt-1 flex-none"
              :class="messageIconToneClass"
            />
            <span>{{ statusMessage }}</span>
          </p>
        </div>
      </AppCard>
    </div>
  </section>
</template>
