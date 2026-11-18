<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import { useNativeDialog } from '../../composables/useNativeDialog'
import type { Shop } from './types'

const props = defineProps<{
  open: boolean
  shop: Shop | null
  platformName: string
}>()

const emit = defineEmits<{
  close: []
  closed: []
  authorize: [shop: Shop]
}>()

const dialog = useNativeDialog(() => props.open)
const authorizeButton = ref<HTMLButtonElement | null>(null)

watch(
  () => props.open,
  async (open) => {
    if (!open) return
    await nextTick()
    authorizeButton.value?.focus()
  },
  { immediate: true },
)

function close(): void {
  if (dialog.value?.open) emit('close')
}

function authorize(): void {
  if (props.shop) emit('authorize', props.shop)
}

function shopTitle(): string {
  if (!props.shop) return ''
  return props.shop.name.trim() || `店铺 ${props.shop.sid}`
}

function statusLabel(isTokenOk: boolean | null): string {
  if (isTokenOk === true) return '有效'
  if (isTokenOk === false) return '无效'
  return '未知'
}

function statusClass(isTokenOk: boolean | null): string {
  if (isTokenOk === true) return 'green'
  if (isTokenOk === false) return 'red'
  return 'gray'
}

function formatDateTime(value: string | null): string {
  if (!value) return '尚未检查'
  const date = new Date(value)
  return Number.isFinite(date.getTime())
    ? date.toLocaleString('zh-CN', { hour12: false })
    : '尚未检查'
}
</script>

<template>
  <dialog
    id="shopDetailLayer"
    ref="dialog"
    class="shop-detail-layer"
    :class="{ open }"
    aria-labelledby="shopDetailTitle"
    @cancel.prevent="close"
    @close="emit('closed')"
  >
    <div v-if="shop" class="shop-detail">
      <header class="shop-detail-head">
        <div class="shop-detail-title">
          <h2 id="shopDetailTitle">{{ shopTitle() }}</h2>
          <p>SID {{ shop.sid }} · {{ platformName }}</p>
        </div>
        <button
          id="closeShopDetail"
          class="form-close"
          type="button"
          title="关闭"
          aria-label="关闭"
          @click="close"
        >
          <svg aria-hidden="true" class="ic"><use href="#i-close" /></svg>
        </button>
      </header>

      <dl class="shop-detail-grid">
        <div>
          <dt>店铺名称</dt>
          <dd>{{ shopTitle() }}</dd>
        </div>
        <div>
          <dt>店铺 ID</dt>
          <dd>{{ shop.sid }}</dd>
        </div>
        <div>
          <dt>平台</dt>
          <dd>{{ platformName }}</dd>
        </div>
        <div>
          <dt>站点</dt>
          <dd>{{ shop.region?.trim() || '未知' }}</dd>
        </div>
        <div>
          <dt>成员 ID</dt>
          <dd>{{ shop.memberId?.trim() || '未提供' }}</dd>
        </div>
        <div>
          <dt>授权状态</dt>
          <dd>
            <span class="tag" :class="statusClass(shop.isTokenOk)">{{
              statusLabel(shop.isTokenOk)
            }}</span>
          </dd>
        </div>
        <div>
          <dt>最后检查</dt>
          <dd>{{ formatDateTime(shop.syncedAt) }}</dd>
        </div>
        <div>
          <dt>上游来源</dt>
          <dd>wxw ERP（OpenERP 只读）</dd>
        </div>
      </dl>

      <p v-if="shop.tokenError" class="shop-detail-error" role="alert">
        状态检查失败：{{ shop.tokenError }}
      </p>

      <div v-if="shop.isTokenOk === false" class="notice amber shop-detail-notice">
        <svg aria-hidden="true" class="ic ic-16"><use href="#i-warning" /></svg>
        <div>
          <p class="notice-title">平台授权失效</p>
          <p class="notice-desc">
            新数据可能停止，历史数据仍可读。请去 wxw ERP 重新授权，完成后回到本页点击「立即同步」。
          </p>
        </div>
      </div>

      <p class="shop-detail-note">
        授权范围、有效期、授权历史由 wxw ERP 管理，本系统不读取，也不在本地发起授权。
      </p>

      <footer class="shop-detail-foot">
        <button
          id="shopAuthorize"
          ref="authorizeButton"
          class="btn primary"
          type="button"
          @click="authorize"
        >
          去 wxw 授权
        </button>
        <button class="btn" type="button" @click="close">关闭</button>
      </footer>
    </div>
  </dialog>
</template>

<style scoped>
.shop-detail-layer {
  width: min(560px, calc(100vw - 32px));
  max-height: calc(100vh - 64px);
  padding: 0;
  border: 0;
  border-radius: 6px;
  background: #fff;
  box-shadow: 0 18px 50px rgba(11, 16, 25, 0.24);
}

.shop-detail-layer::backdrop {
  background: rgba(11, 16, 25, 0.38);
}

.shop-detail {
  display: flex;
  flex-direction: column;
  padding: 20px 22px 18px;
}

.shop-detail-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.shop-detail-title h2 {
  margin: 0;
  color: var(--text);
  font-size: 16px;
  font-weight: 600;
  line-height: 24px;
  overflow-wrap: anywhere;
}

.shop-detail-title p {
  margin: 4px 0 0;
  color: var(--muted);
  font-size: 12px;
  line-height: 18px;
}

.shop-detail-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px 20px;
  margin: 18px 0 0;
  padding: 16px 0;
  border-top: 1px solid var(--line-soft);
  border-bottom: 1px solid var(--line-soft);
}

.shop-detail-grid div {
  min-width: 0;
}

.shop-detail-grid dt {
  color: var(--muted);
  font-size: 12px;
  line-height: 18px;
}

.shop-detail-grid dd {
  margin: 3px 0 0;
  color: var(--text);
  font-size: 13px;
  line-height: 20px;
  overflow-wrap: anywhere;
}

.shop-detail-error {
  margin: 12px 0 0;
  color: #b42318;
  font-size: 12px;
  line-height: 18px;
  overflow-wrap: anywhere;
}

.shop-detail-notice {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  margin-top: 12px;
  padding: 12px;
  border: 1px solid #f5d9a8;
  border-radius: 4px;
  background: #fff8ea;
}

.shop-detail-notice .notice-title {
  color: #8a5a00;
  font-size: 12px;
  font-weight: 600;
  line-height: 18px;
}

.shop-detail-notice .notice-desc {
  margin-top: 2px;
  color: #8a5a00;
  font-size: 12px;
  line-height: 18px;
}

.shop-detail-note {
  margin: 12px 0 0;
  color: var(--muted);
  font-size: 12px;
  line-height: 18px;
}

.shop-detail-foot {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 20px;
}

@media (max-width: 560px) {
  .shop-detail-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
