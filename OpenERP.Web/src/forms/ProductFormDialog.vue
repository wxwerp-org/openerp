<script setup lang="ts">
import { ref } from 'vue'
import { useNativeDialog } from '../composables/useNativeDialog'

const props = defineProps<{ open: boolean }>()
const dialog = useNativeDialog(() => props.open)
void dialog

const emit = defineEmits<{
  close: []
  feedback: [message: string]
}>()

const productName = ref('')
const showNameError = ref(false)
const productNameInputRef = ref<HTMLInputElement | null>(null)
const activeSection = ref('基本信息')
const descriptionCount = ref(0)

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
<dialog ref="dialog" class="form-layer" id="productFormLayer" :class="{ open }" aria-label="添加产品" @close="emit('close')">
        <div class="form-dialog-head">
          <div class="form-dialog-title">添加产品</div>
          <button class="form-close" type="button" id="closeProductForm" aria-label="关闭" @click="emit('close')">
            <svg aria-hidden="true" class="ic ic-16"><use href="#i-close"/></svg>
          </button>
        </div>
        <div class="form-dialog-body">
          <aside class="form-left">
            <div class="form-left-top">
              <div class="form-image-box" role="button" tabindex="0">编辑图片</div>
              <div class="form-abbr strong">—</div>
              <div class="form-abbr">—</div>
            </div>
            <nav class="form-nav" aria-label="产品表单分区">
              <button class="form-nav-item" :class="{ active: activeSection === '基本信息' }" type="button" @click="activeSection = '基本信息'">基本信息</button>
              <button class="form-nav-item" :class="{ active: activeSection === '采购信息' }" type="button" @click="activeSection = '采购信息'">采购信息</button>
              <button class="form-nav-item" :class="{ active: activeSection === '供应商报价' }" type="button" @click="activeSection = '供应商报价'">供应商报价</button>
              <button class="form-nav-item" :class="{ active: activeSection === '关联辅料' }" type="button" @click="activeSection = '关联辅料'">关联辅料</button>
              <button class="form-nav-item" :class="{ active: activeSection === '物流报关清关' }" type="button" @click="activeSection = '物流报关清关'">物流报关清关</button>
              <button class="form-nav-item" :class="{ active: activeSection === '图片信息' }" type="button" @click="activeSection = '图片信息'">图片信息</button>
              <button class="form-nav-item" :class="{ active: activeSection === '质检信息' }" type="button" @click="activeSection = '质检信息'">质检信息</button>
            </nav>
          </aside>
          <div class="form-right" id="formRight">
            <section class="form-section">
              <div class="form-section-title">基本信息</div>
              <form class="baseForm" id="addProductForm" @submit.prevent="save">
                <div class="form-grid">
                  <div class="form-item full" :class="{ 'is-error': showNameError }">
                    <div class="form-line">
                      <label class="form-label" for="productNameInput"><span class="req">*</span>品名</label>
                      <div class="form-control-wrap">
                        <div class="form-control"><input id="productNameInput" ref="productNameInputRef" v-model="productName" type="text" required :aria-invalid="showNameError" aria-describedby="productNameError" @input="showNameError = !productName.trim()" autocomplete="off" aria-label="品名"></div>
                        <div id="productNameError" class="form-error" role="alert">请输入产品名称</div>
                      </div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productSkuInput"><span class="req">*</span>SKU</label>
                      <div class="form-control-wrap"><div class="form-control"><input id="productSkuInput" type="text" autocomplete="off" aria-label="SKU" required></div></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productTypeSelect"><span class="req">*</span>产品类型</label>
                      <div class="form-control-wrap">
                        <button id="productTypeSelect" class="form-select" type="button"><span>普通产品</span><svg aria-hidden="true" class="ic field-chev"><use href="#i-chev-down"/></svg></button>
                      </div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productCategorySelect">分类</label>
                      <div class="form-control-wrap"><button id="productCategorySelect" class="form-select placeholder" type="button" aria-label="分类"><span></span><svg aria-hidden="true" class="ic field-chev"><use href="#i-chev-down"/></svg></button></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productIdentifierInput">识别码<svg aria-hidden="true" class="ic info-dot"><use href="#i-info"/></svg></label>
                      <div class="form-control-wrap"><div class="form-control"><input id="productIdentifierInput" type="text" autocomplete="off" aria-label="识别码"></div></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productStatusSelect">状态<svg aria-hidden="true" class="ic info-dot"><use href="#i-info"/></svg></label>
                      <div class="form-control-wrap"><button id="productStatusSelect" class="form-select" type="button"><span>在售</span><svg aria-hidden="true" class="ic field-chev"><use href="#i-chev-down"/></svg></button></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productBrandSelect">品牌<svg aria-hidden="true" class="ic info-dot"><use href="#i-info"/></svg></label>
                      <div class="form-control-wrap"><button id="productBrandSelect" class="form-select placeholder" type="button" aria-label="品牌"><span></span><svg aria-hidden="true" class="ic field-chev"><use href="#i-chev-down"/></svg></button></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productModelInput">型号</label>
                      <div class="form-control-wrap"><div class="form-control"><input id="productModelInput" type="text" autocomplete="off" aria-label="型号"></div></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productUnitInput">单位</label>
                      <div class="form-control-wrap"><div class="form-control"><input id="productUnitInput" type="text" autocomplete="off" aria-label="单位"></div></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productMaterialInput">产品材质</label>
                      <div class="form-control-wrap"><div class="form-control"><input id="productMaterialInput" type="text" autocomplete="off" aria-label="产品材质"></div></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productTagsSelect">产品标签</label>
                      <div class="form-control-wrap"><button id="productTagsSelect" class="form-select placeholder" type="button" aria-label="产品标签"><span></span><svg aria-hidden="true" class="ic field-chev"><use href="#i-chev-down"/></svg></button></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productDeveloperInput">开发人</label>
                      <div class="form-control-wrap"><div class="form-control"><input id="productDeveloperInput" type="text" autocomplete="off" aria-label="开发人"></div></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productDevelopmentDateInput">开发日期</label>
                      <div class="form-control-wrap"><div class="form-control"><input id="productDevelopmentDateInput" type="text" autocomplete="off" aria-label="开发日期"><svg aria-hidden="true" class="ic field-calendar"><use href="#i-calendar"/></svg></div></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productOwnerSelect">产品负责人<svg aria-hidden="true" class="ic info-dot"><use href="#i-info"/></svg></label>
                      <div class="form-control-wrap"><button id="productOwnerSelect" class="form-select placeholder" type="button" aria-label="产品负责人"><span></span><svg aria-hidden="true" class="ic field-chev"><use href="#i-chev-down"/></svg></button></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productSpuSelect">关联SPU</label>
                      <div class="form-control-wrap"><button id="productSpuSelect" class="form-select placeholder" type="button" aria-label="关联SPU"><span></span><svg aria-hidden="true" class="ic field-chev"><use href="#i-chev-down"/></svg></button></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productAttributesSelect">属性</label>
                      <div class="form-control-wrap"><button id="productAttributesSelect" class="form-select placeholder" type="button" aria-label="属性"><span></span><svg aria-hidden="true" class="ic field-chev"><use href="#i-chev-down"/></svg></button></div>
                    </div>
                  </div>
                  <div class="form-item">
                    <div class="form-line">
                      <label class="form-label" for="productNoteInput">产品备注<svg aria-hidden="true" class="ic info-dot"><use href="#i-info"/></svg></label>
                      <div class="form-control-wrap"><div class="form-control"><input id="productNoteInput" type="text" autocomplete="off" aria-label="产品备注"></div></div>
                    </div>
                  </div>
                </div>
                <div class="form-attachment">
                  <label class="form-label">附件</label>
                  <button class="attachment-link" type="button"><svg aria-hidden="true" class="ic ic-12"><use href="#i-plus"/></svg>上传附件</button>
                </div>
                <div class="form-description">
                  <label class="form-label">产品描述</label>
                  <div class="rich-editor">
                    <div class="rich-toolbar" aria-label="富文本工具栏">
                      <button class="rt-btn" type="button" title="撤销" aria-label="撤销"><svg aria-hidden="true" class="ic"><use href="#i-undo"/></svg></button>
                      <button class="rt-btn" type="button" title="恢复" aria-label="恢复"><svg aria-hidden="true" class="ic"><use href="#i-redo"/></svg></button>
                      <span class="rt-sep"></span>
                      <button class="rt-btn" type="button" title="字体" aria-label="字体"><svg aria-hidden="true" class="ic" viewBox="0 0 16 16"><text x="3" y="12" font-family="Verdana" font-size="12" fill="currentColor">T</text></svg></button>
                      <button class="rt-btn" type="button" title="字号" aria-label="字号"><svg aria-hidden="true" class="ic" viewBox="0 0 16 16"><text x="2" y="12" font-family="Verdana" font-size="9" fill="currentColor">T</text><text x="7" y="13" font-family="Verdana" font-size="13" fill="currentColor">T</text></svg></button>
                      <button class="rt-btn" type="button" title="加粗" aria-label="加粗"><svg aria-hidden="true" class="ic" viewBox="0 0 16 16"><text x="4" y="12" font-family="Verdana" font-size="12" font-weight="700" fill="currentColor">B</text></svg></button>
                      <button class="rt-btn" type="button" title="斜体" aria-label="斜体"><svg aria-hidden="true" class="ic" viewBox="0 0 16 16"><text x="4" y="12" font-family="Verdana" font-size="12" font-style="italic" fill="currentColor">I</text></svg></button>
                      <button class="rt-btn" type="button" title="下划线" aria-label="下划线"><svg aria-hidden="true" class="ic" viewBox="0 0 16 16"><text x="3" y="11" font-family="Verdana" font-size="11" fill="currentColor">U</text><path d="M4 14h8"/></svg></button>
                      <span class="rt-sep"></span>
                      <button class="rt-btn" type="button" title="文字颜色" aria-label="文字颜色"><svg aria-hidden="true" class="ic"><use href="#i-pen"/></svg></button>
                      <button class="rt-btn" type="button" title="背景色" aria-label="背景色"><svg aria-hidden="true" class="ic"><use href="#i-fill"/></svg></button>
                      <span class="rt-sep"></span>
                      <button class="rt-btn" type="button" title="对齐" aria-label="对齐"><svg aria-hidden="true" class="ic"><use href="#i-align"/></svg></button>
                      <button class="rt-btn" type="button" title="有序列表" aria-label="有序列表"><svg aria-hidden="true" class="ic"><use href="#i-list"/></svg></button>
                      <button class="rt-btn" type="button" title="缩进" aria-label="缩进"><svg aria-hidden="true" class="ic"><use href="#i-indent"/></svg></button>
                      <button class="rt-btn" type="button" title="分割线" aria-label="分割线"><svg aria-hidden="true" class="ic"><use href="#i-hr"/></svg></button>
                      <button class="rt-btn" type="button" title="清除格式" aria-label="清除格式"><svg aria-hidden="true" class="ic"><use href="#i-clear"/></svg></button>
                    </div>
                    <div class="editor-body" id="productDescription" contenteditable="true" @input="updateDescriptionCount" data-placeholder="请输入正文"></div>
                    <div class="editor-count"><span>带样式文本预计长度&nbsp;</span><span id="descCount">{{ descriptionCount }}</span><span>&nbsp;/65535</span></div>
                  </div>
                </div>
              </form>
            </section>
          </div>
        </div>
        <div class="form-footer">
          <button class="btn" type="button" id="cancelProductForm" @click="emit('close')">取消</button>
          <button class="btn primary" type="button" id="saveProductForm" @click="save">保存</button>
        </div>
      </dialog>
</template>
