<script setup lang="ts">
import { ref } from 'vue'
import AppDialog from '../../ui/AppDialog.vue'
import AppIcon from '../../ui/AppIcon.vue'

defineProps<{ open: boolean }>()

const emit = defineEmits<{
  close: []
  feedback: [message: string]
}>()

const productName = ref('')
const showNameError = ref(false)
const productNameInputRef = ref<HTMLInputElement | null>(null)
const activeSection = ref('基本信息')
const descriptionCount = ref(0)

const fieldClass = 'relative min-w-0'
const labelClass =
  'product-field-label relative w-90 flex-none pr-12 text-right text-12/32 text-body whitespace-nowrap'
const controlClass =
  'relative flex h-32 w-full items-center rounded-4 border border-input bg-white hover:border-input-hover focus-within:border-primary'
const controlInvalidClass =
  'relative flex h-32 w-full items-center rounded-4 border border-danger bg-white hover:border-danger focus-within:border-danger'
const inputClass =
  'h-30 w-full min-w-0 rounded-4 border-0 border-current bg-transparent px-11 py-0 text-12/30 text-ink outline-none placeholder:text-placeholder'
const selectBaseClass =
  'relative flex h-32 w-full items-center overflow-hidden rounded-4 border border-input bg-white py-0 pr-29 pl-11 text-left text-12 text-ellipsis whitespace-nowrap hover:border-input-hover'
const selectClass = `${selectBaseClass} text-ink`
const selectPlaceholderClass = `${selectBaseClass} text-placeholder`
const errorClass = 'absolute left-0 top-full pt-3 text-12/16 text-danger whitespace-nowrap'
const hiddenClass = 'hidden'
const navItemClass = 'flex h-32 w-full items-center rounded-2 px-12 text-left text-12'
const navItemIdleClass = 'text-ink hover:bg-primary-faint hover:text-primary'
const navItemActiveClass = 'bg-primary-faint font-semibold text-primary'
const toolButtonClass =
  'inline-flex h-26 w-26 flex-none items-center justify-center rounded-3 text-body hover:bg-page hover:text-ink'
const infoDotClass = 'relative -top-1 ml-1 text-placeholder'
const footerButtonClass =
  'inline-flex h-32 w-50 shrink-0 items-center justify-center rounded-4 border text-12/28'
const footerDefaultClass = `${footerButtonClass} border-input bg-white text-ink hover:border-input-hover hover:bg-page-soft`
const footerPrimaryClass = `${footerButtonClass} border-primary bg-primary text-white hover:border-primary-hover hover:bg-primary-hover`

const sections = [
  '基本信息',
  '采购信息',
  '供应商报价',
  '关联辅料',
  '物流报关清关',
  '图片信息',
  '质检信息',
]

function updateDescriptionCount(event: Event): void {
  descriptionCount.value = (event.target as HTMLElement).innerText.length
}

function save(): void {
  if (!productName.value.trim()) {
    showNameError.value = true
    productNameInputRef.value?.focus()
    emit('feedback', '请先填写产品名称')
    return
  }
  emit('feedback', '原型演示：表单未接入保存接口')
}
</script>

<template>
  <AppDialog
    id="productFormLayer"
    :open="open"
    layer="form"
    backdrop="none"
    aria-label="添加产品"
    class="overflow-visible bg-transparent"
    @close="emit('close')"
  >
    <div class="absolute inset-0 flex flex-col overflow-hidden rounded-4 bg-white shadow-layer">
      <div
        class="flex h-54 flex-none items-center justify-between border-b border-line bg-white px-24"
      >
        <div class="text-16/24 font-semibold text-ink">添加产品</div>
        <!-- 旧表单的关闭按钮图标为 16px（.ic-16），账户弹窗是 14px，故此处不复用 AppDialogClose。 -->
        <button
          id="closeProductForm"
          type="button"
          title="关闭"
          aria-label="关闭"
          class="flex h-24 w-26 shrink-0 items-center justify-center rounded-3 text-body hover:bg-page hover:text-ink"
          @click="emit('close')"
        >
          <AppIcon name="i-close" :size="16" />
        </button>
      </div>
      <div class="flex min-h-0 flex-1 overflow-hidden">
        <aside
          class="w-192 flex-none overflow-y-auto border-r border-line bg-white [scrollbar-width:thin]"
        >
          <div class="px-20 py-16">
            <div
              class="flex h-152 w-full cursor-pointer items-center justify-center rounded-2 border border-dashed border-input bg-white text-12/18 text-body-strong hover:border-primary hover:bg-row-hover hover:text-primary"
              role="button"
              tabindex="0"
            >
              编辑图片
            </div>
            <div class="mt-8 truncate text-12/18 font-semibold text-body">—</div>
            <div class="mt-4 truncate text-12/18 text-muted">—</div>
          </div>
          <nav class="border-t border-line px-8 py-16" aria-label="产品表单分区">
            <button
              v-for="section in sections"
              :key="section"
              :class="[
                navItemClass,
                activeSection === section ? navItemActiveClass : navItemIdleClass,
              ]"
              type="button"
              @click="activeSection = section"
            >
              {{ section }}
            </button>
          </nav>
        </aside>
        <div
          id="formRight"
          class="min-w-0 flex-1 overflow-y-auto px-24 pt-24 pb-20 [scrollbar-width:thin]"
        >
          <section class="w-full">
            <div
              class="product-section-title relative mb-20 flex h-24 items-center pl-10 text-14/24 font-semibold text-ink"
            >
              基本信息
            </div>
            <form id="addProductForm" @submit.prevent="save">
              <div class="grid grid-cols-4 items-start gap-x-24 gap-y-20">
                <div
                  class="relative col-span-full min-w-0"
                  :data-invalid="showNameError ? 'true' : undefined"
                >
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productNameInput"
                      ><span class="mr-1 text-danger">*</span>品名</label
                    >
                    <div class="relative min-w-0 flex-1">
                      <div :class="showNameError ? controlInvalidClass : controlClass">
                        <input
                          id="productNameInput"
                          ref="productNameInputRef"
                          v-model="productName"
                          type="text"
                          required
                          :aria-invalid="showNameError"
                          aria-describedby="productNameError"
                          autocomplete="off"
                          aria-label="品名"
                          :class="inputClass"
                          @input="showNameError = !productName.trim()"
                        />
                      </div>
                      <div
                        id="productNameError"
                        role="alert"
                        :class="showNameError ? errorClass : hiddenClass"
                      >
                        请输入产品名称
                      </div>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productSkuInput"
                      ><span class="mr-1 text-danger">*</span>SKU</label
                    >
                    <div class="relative min-w-0 flex-1">
                      <div :class="controlClass">
                        <input
                          id="productSkuInput"
                          type="text"
                          autocomplete="off"
                          aria-label="SKU"
                          :class="inputClass"
                          required
                        />
                      </div>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productTypeSelect"
                      ><span class="mr-1 text-danger">*</span>产品类型</label
                    >
                    <div class="relative min-w-0 flex-1">
                      <button id="productTypeSelect" :class="selectClass" type="button">
                        <span>普通产品</span
                        ><AppIcon
                          name="i-chev-down"
                          class="pointer-events-none absolute right-10"
                        />
                      </button>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productCategorySelect">分类</label>
                    <div class="relative min-w-0 flex-1">
                      <button
                        id="productCategorySelect"
                        :class="selectPlaceholderClass"
                        type="button"
                        aria-label="分类"
                      >
                        <span></span
                        ><AppIcon
                          name="i-chev-down"
                          class="pointer-events-none absolute right-10"
                        />
                      </button>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productIdentifierInput"
                      >识别码<AppIcon name="i-info" :size="13" :class="infoDotClass"
                    /></label>
                    <div class="relative min-w-0 flex-1">
                      <div :class="controlClass">
                        <input
                          id="productIdentifierInput"
                          type="text"
                          autocomplete="off"
                          aria-label="识别码"
                          :class="inputClass"
                        />
                      </div>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productStatusSelect"
                      >状态<AppIcon name="i-info" :size="13" :class="infoDotClass"
                    /></label>
                    <div class="relative min-w-0 flex-1">
                      <button id="productStatusSelect" :class="selectClass" type="button">
                        <span>在售</span
                        ><AppIcon
                          name="i-chev-down"
                          class="pointer-events-none absolute right-10"
                        />
                      </button>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productBrandSelect"
                      >品牌<AppIcon name="i-info" :size="13" :class="infoDotClass"
                    /></label>
                    <div class="relative min-w-0 flex-1">
                      <button
                        id="productBrandSelect"
                        :class="selectPlaceholderClass"
                        type="button"
                        aria-label="品牌"
                      >
                        <span></span
                        ><AppIcon
                          name="i-chev-down"
                          class="pointer-events-none absolute right-10"
                        />
                      </button>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productModelInput">型号</label>
                    <div class="relative min-w-0 flex-1">
                      <div :class="controlClass">
                        <input
                          id="productModelInput"
                          type="text"
                          autocomplete="off"
                          aria-label="型号"
                          :class="inputClass"
                        />
                      </div>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productUnitInput">单位</label>
                    <div class="relative min-w-0 flex-1">
                      <div :class="controlClass">
                        <input
                          id="productUnitInput"
                          type="text"
                          autocomplete="off"
                          aria-label="单位"
                          :class="inputClass"
                        />
                      </div>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productMaterialInput">产品材质</label>
                    <div class="relative min-w-0 flex-1">
                      <div :class="controlClass">
                        <input
                          id="productMaterialInput"
                          type="text"
                          autocomplete="off"
                          aria-label="产品材质"
                          :class="inputClass"
                        />
                      </div>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productTagsSelect">产品标签</label>
                    <div class="relative min-w-0 flex-1">
                      <button
                        id="productTagsSelect"
                        :class="selectPlaceholderClass"
                        type="button"
                        aria-label="产品标签"
                      >
                        <span></span
                        ><AppIcon
                          name="i-chev-down"
                          class="pointer-events-none absolute right-10"
                        />
                      </button>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productDeveloperInput">开发人</label>
                    <div class="relative min-w-0 flex-1">
                      <div :class="controlClass">
                        <input
                          id="productDeveloperInput"
                          type="text"
                          autocomplete="off"
                          aria-label="开发人"
                          :class="inputClass"
                        />
                      </div>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productDevelopmentDateInput">开发日期</label>
                    <div class="relative min-w-0 flex-1">
                      <div :class="controlClass">
                        <input
                          id="productDevelopmentDateInput"
                          type="text"
                          autocomplete="off"
                          aria-label="开发日期"
                          :class="inputClass"
                        /><AppIcon
                          name="i-calendar"
                          :size="14"
                          class="pointer-events-none absolute right-11 text-placeholder"
                        />
                      </div>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productOwnerSelect"
                      >产品负责人<AppIcon name="i-info" :size="13" :class="infoDotClass"
                    /></label>
                    <div class="relative min-w-0 flex-1">
                      <button
                        id="productOwnerSelect"
                        :class="selectPlaceholderClass"
                        type="button"
                        aria-label="产品负责人"
                      >
                        <span></span
                        ><AppIcon
                          name="i-chev-down"
                          class="pointer-events-none absolute right-10"
                        />
                      </button>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productSpuSelect">关联SPU</label>
                    <div class="relative min-w-0 flex-1">
                      <button
                        id="productSpuSelect"
                        :class="selectPlaceholderClass"
                        type="button"
                        aria-label="关联SPU"
                      >
                        <span></span
                        ><AppIcon
                          name="i-chev-down"
                          class="pointer-events-none absolute right-10"
                        />
                      </button>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productAttributesSelect">属性</label>
                    <div class="relative min-w-0 flex-1">
                      <button
                        id="productAttributesSelect"
                        :class="selectPlaceholderClass"
                        type="button"
                        aria-label="属性"
                      >
                        <span></span
                        ><AppIcon
                          name="i-chev-down"
                          class="pointer-events-none absolute right-10"
                        />
                      </button>
                    </div>
                  </div>
                </div>
                <div :class="fieldClass">
                  <div class="flex min-w-0 items-start">
                    <label :class="labelClass" for="productNoteInput"
                      >产品备注<AppIcon name="i-info" :size="13" :class="infoDotClass"
                    /></label>
                    <div class="relative min-w-0 flex-1">
                      <div :class="controlClass">
                        <input
                          id="productNoteInput"
                          type="text"
                          autocomplete="off"
                          aria-label="产品备注"
                          :class="inputClass"
                        />
                      </div>
                    </div>
                  </div>
                </div>
              </div>
              <div class="mt-20 flex min-h-28 items-center">
                <label
                  class="product-field-label relative w-90 flex-none pr-12 text-right text-12/28 text-body whitespace-nowrap"
                  >附件</label
                >
                <button
                  type="button"
                  class="inline-flex h-28 items-center gap-5 text-12 text-primary hover:rounded-3 hover:bg-primary-faint hover:text-primary-hover"
                >
                  <AppIcon name="i-plus" :size="12" />上传附件
                </button>
              </div>
              <div class="mt-24 flex items-start">
                <label
                  class="product-field-label relative w-90 flex-none pr-12 pt-4 text-right text-12/22 text-body whitespace-nowrap"
                  >产品描述</label
                >
                <div
                  class="relative flex h-343 min-w-0 flex-1 flex-col overflow-hidden rounded-4 border border-dialog-editor-border bg-white"
                >
                  <div
                    class="flex h-40 flex-none items-center gap-1 overflow-hidden border-b border-line px-6 text-body"
                    aria-label="富文本工具栏"
                  >
                    <button :class="toolButtonClass" type="button" title="撤销" aria-label="撤销">
                      <AppIcon name="i-undo" />
                    </button>
                    <button :class="toolButtonClass" type="button" title="恢复" aria-label="恢复">
                      <AppIcon name="i-redo" />
                    </button>
                    <span class="mx-3 h-16 w-1 flex-none bg-line-soft"></span>
                    <button :class="toolButtonClass" type="button" title="字体" aria-label="字体">
                      <svg aria-hidden="true" class="ic" viewBox="0 0 16 16">
                        <text x="3" y="12" font-family="Verdana" font-size="12" fill="currentColor">
                          T
                        </text>
                      </svg>
                    </button>
                    <button :class="toolButtonClass" type="button" title="字号" aria-label="字号">
                      <svg aria-hidden="true" class="ic" viewBox="0 0 16 16">
                        <text x="2" y="12" font-family="Verdana" font-size="9" fill="currentColor">
                          T
                        </text>
                        <text x="7" y="13" font-family="Verdana" font-size="13" fill="currentColor">
                          T
                        </text>
                      </svg>
                    </button>
                    <button :class="toolButtonClass" type="button" title="加粗" aria-label="加粗">
                      <svg aria-hidden="true" class="ic" viewBox="0 0 16 16">
                        <text
                          x="4"
                          y="12"
                          font-family="Verdana"
                          font-size="12"
                          font-weight="700"
                          fill="currentColor"
                        >
                          B
                        </text>
                      </svg>
                    </button>
                    <button :class="toolButtonClass" type="button" title="斜体" aria-label="斜体">
                      <svg aria-hidden="true" class="ic" viewBox="0 0 16 16">
                        <text
                          x="4"
                          y="12"
                          font-family="Verdana"
                          font-size="12"
                          font-style="italic"
                          fill="currentColor"
                        >
                          I
                        </text>
                      </svg>
                    </button>
                    <button
                      :class="toolButtonClass"
                      type="button"
                      title="下划线"
                      aria-label="下划线"
                    >
                      <svg aria-hidden="true" class="ic" viewBox="0 0 16 16">
                        <text x="3" y="11" font-family="Verdana" font-size="11" fill="currentColor">
                          U
                        </text>
                        <path d="M4 14h8" />
                      </svg>
                    </button>
                    <span class="mx-3 h-16 w-1 flex-none bg-line-soft"></span>
                    <button
                      :class="toolButtonClass"
                      type="button"
                      title="文字颜色"
                      aria-label="文字颜色"
                    >
                      <AppIcon name="i-pen" />
                    </button>
                    <button
                      :class="toolButtonClass"
                      type="button"
                      title="背景色"
                      aria-label="背景色"
                    >
                      <AppIcon name="i-fill" />
                    </button>
                    <span class="mx-3 h-16 w-1 flex-none bg-line-soft"></span>
                    <button :class="toolButtonClass" type="button" title="对齐" aria-label="对齐">
                      <AppIcon name="i-align" />
                    </button>
                    <button
                      :class="toolButtonClass"
                      type="button"
                      title="有序列表"
                      aria-label="有序列表"
                    >
                      <AppIcon name="i-list" />
                    </button>
                    <button :class="toolButtonClass" type="button" title="缩进" aria-label="缩进">
                      <AppIcon name="i-indent" />
                    </button>
                    <button
                      :class="toolButtonClass"
                      type="button"
                      title="分割线"
                      aria-label="分割线"
                    >
                      <AppIcon name="i-hr" />
                    </button>
                    <button
                      :class="toolButtonClass"
                      type="button"
                      title="清除格式"
                      aria-label="清除格式"
                    >
                      <AppIcon name="i-clear" />
                    </button>
                  </div>
                  <div
                    id="productDescription"
                    contenteditable="true"
                    data-placeholder="请输入正文"
                    class="rich-editor-body min-h-0 flex-1 overflow-y-auto px-12 pt-10 pb-30 text-12/20 text-ink outline-none whitespace-pre-wrap"
                    @input="updateDescriptionCount"
                  ></div>
                  <div
                    class="pointer-events-none absolute right-8 bottom-3 flex h-20 items-center justify-end gap-2 px-2 text-12 text-placeholder"
                  >
                    <span>带样式文本预计长度&nbsp;</span
                    ><span id="descCount">{{ descriptionCount }}</span
                    ><span>&nbsp;/65535</span>
                  </div>
                </div>
              </div>
            </form>
          </section>
        </div>
      </div>
      <div
        class="flex h-64 flex-none items-center justify-end gap-16 border-t border-line bg-white px-24"
      >
        <button
          id="cancelProductForm"
          :class="footerDefaultClass"
          type="button"
          @click="emit('close')"
        >
          取消
        </button>
        <button id="saveProductForm" :class="footerPrimaryClass" type="button" @click="save">
          保存
        </button>
      </div>
    </div>
  </AppDialog>
</template>

<style scoped>
/* 例外项：分节标题伪元素装饰、字段标签冒号、富文本占位符。 */
.product-field-label::after {
  content: '：';
  position: absolute;
  top: 0;
  right: 0;
  width: calc(var(--spacing) * 12);
  text-align: left;
}

.product-section-title::before {
  content: '';
  position: absolute;
  top: calc(var(--spacing) * 5);
  left: 0;
  width: calc(var(--spacing) * 2);
  height: calc(var(--spacing) * 14);
  background: var(--color-primary);
}

.rich-editor-body:empty::before {
  content: attr(data-placeholder);
  color: var(--color-placeholder);
  pointer-events: none;
}
</style>
