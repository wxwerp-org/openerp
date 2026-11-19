<script setup lang="ts">
import { ref } from 'vue'
import ProductFormDialog from '../forms/ProductFormDialog.vue'
import { useToast } from '../composables/useToast'
import AppButton from '../ui/AppButton.vue'
import AppCheckbox from '../ui/AppCheckbox.vue'
import AppIcon from '../ui/AppIcon.vue'
import AppIconButton from '../ui/AppIconButton.vue'
import AppListSearchbar from '../ui/AppListSearchbar.vue'
import AppListToolbar from '../ui/AppListToolbar.vue'
import AppPager from '../ui/AppPager.vue'
import AppSegmented from '../ui/AppSegmented.vue'
import AppTable from '../ui/AppTable.vue'
import AppTh from '../ui/AppTh.vue'
import AppToast from '../ui/AppToast.vue'

const skuMode = ref('按SKU')
const productType = ref('全部')

const formOpen = ref(false)
const { toast, showToast } = useToast()

const controlPlaceholder =
  'inline-flex h-[var(--control-h)] shrink-0 items-center gap-6 rounded-4 border border-input bg-white px-10 text-12/28 whitespace-nowrap text-placeholder hover:border-input-hover'
const joinedControlStart =
  'inline-flex h-[var(--control-h)] shrink-0 items-center gap-6 rounded-l-4 border-y border-l border-input bg-white px-10 text-12/28 whitespace-nowrap text-placeholder hover:border-input-hover'
const joinedDateRange =
  'inline-flex h-[var(--control-h)] shrink-0 items-center gap-8 border-y border-l border-input bg-white px-10 text-12/28 whitespace-nowrap text-placeholder hover:border-input-hover'
const fieldBox =
  'inline-flex h-[var(--control-h)] shrink-0 items-center rounded-4 border border-input bg-white'
const fieldBoxInput =
  'h-28 min-w-0 border-0 border-current bg-transparent px-10 py-0 text-12/18 text-ink outline-none placeholder:text-placeholder'
const fieldBoxInputMuted =
  'h-28 min-w-0 border-0 border-current bg-transparent px-10 py-0 text-12/18 text-muted outline-none placeholder:text-placeholder'
const searchInput =
  'h-28 min-w-0 flex-1 border-0 border-current bg-transparent px-10 py-0 text-12/18 text-ink outline-none placeholder:text-placeholder'
const searchButton =
  'flex h-28 w-30 shrink-0 items-center justify-center text-search-icon hover:bg-search-hover-bg hover:text-primary'
const sortButton =
  'inline-flex min-h-24 max-w-full items-center gap-3 text-left disabled:cursor-default'
const caretClass = 'opacity-[0.65]'
const pageSizeClass =
  'inline-flex h-28 min-w-100 shrink-0 items-center justify-between gap-10 rounded-4 border border-input bg-white px-9 text-ink'
const pageJumpInput =
  'h-28 w-46 rounded-4 border border-input bg-white px-4 py-0 text-center text-12/18 text-ink outline-none focus:border-primary'
</script>

<template>
  <section id="page-product" class="flex h-full w-full min-h-0 flex-col overflow-hidden bg-white">
    <h1 class="sr-only">产品管理</h1>
    <AppListSearchbar label="产品筛选">
      <AppSegmented
        v-model="skuMode"
        :options="['按SKU', '按SPU']"
        aria-label="产品维度"
        size="md"
        :padding-x="10"
        :min-width="52"
      />
      <AppSegmented
        v-model="productType"
        :options="['全部', '普通产品', '组合产品']"
        aria-label="产品类型"
        size="md"
        :padding-x="10"
      />
      <button :class="controlPlaceholder" type="button">
        分类<AppIcon name="i-chev-down" :size="10" class="text-caret" />
      </button>
      <button :class="controlPlaceholder" type="button">
        属性<AppIcon name="i-chev-down" :size="10" class="text-caret" />
      </button>
      <button :class="controlPlaceholder" type="button">
        品牌<AppIcon name="i-chev-down" :size="10" class="text-caret" />
      </button>
      <button :class="controlPlaceholder" type="button">
        状态<AppIcon name="i-chev-down" :size="10" class="text-caret" />
      </button>
      <button :class="controlPlaceholder" type="button">
        产品标签<AppIcon name="i-chev-down" :size="10" class="text-caret" />
      </button>
      <button :class="controlPlaceholder" type="button">
        开启状态<AppIcon name="i-chev-down" :size="10" class="text-caret" />
      </button>
      <div class="flex items-stretch">
        <button :class="joinedControlStart" type="button">
          创建时间<AppIcon name="i-chev-down" :size="10" class="text-caret" />
        </button>
        <button :class="joinedDateRange" type="button">
          开始日期 - 结束日期<AppIcon name="i-calendar" :size="14" class="text-placeholder" />
        </button>
      </div>
      <div :class="fieldBox" class="w-60">
        <input :class="fieldBoxInput" aria-label="SKU" placeholder="SKU" />
        <AppIcon name="i-chev-down" :size="10" class="mr-9 text-caret" />
      </div>
      <div :class="fieldBox" class="-ml-1 w-59">
        <input :class="fieldBoxInputMuted" aria-label="查询方式" value="模糊" readonly />
        <AppIcon name="i-chev-down" :size="10" class="mr-9 text-caret" />
      </div>
      <div
        class="ml-auto flex h-[var(--control-h)] min-w-180 max-w-260 flex-[0_1_260px] items-center overflow-hidden rounded-4 border border-input bg-white"
      >
        <input :class="searchInput" aria-label="搜索产品" placeholder="输入品名、SKU、识别码搜索" />
        <button :class="searchButton" type="button" title="搜索" aria-label="搜索">
          <AppIcon name="i-search" :size="16" />
        </button>
        <span class="h-16 w-px shrink-0 bg-line-soft"></span>
        <button
          :class="searchButton"
          class="ml-2 border-l border-l-line-soft"
          type="button"
          title="高级搜索"
          aria-label="高级搜索"
        >
          <AppIcon name="i-filter" :size="16" />
        </button>
      </div>
      <AppButton>更多筛选</AppButton>
      <AppButton variant="soft">重置</AppButton>
    </AppListSearchbar>
    <AppListToolbar>
      <AppButton id="openProductForm" variant="primary" @click="formOpen = true">
        添加产品
      </AppButton>
      <AppButton>添加多属性产品</AppButton>
      <AppButton>导入<AppIcon name="i-chev-down" :size="10" :class="caretClass" /></AppButton>
      <AppButton>同步图片<AppIcon name="i-chev-down" :size="10" :class="caretClass" /></AppButton>
      <AppButton>打印产品条码</AppButton>
      <AppButton>更多<AppIcon name="i-chev-down" :size="10" :class="caretClass" /></AppButton>
      <template #right>
        <AppIconButton label="刷新" tone="toolbar"><AppIcon name="i-refresh" /></AppIconButton>
        <AppIconButton label="帮助" tone="toolbar"><AppIcon name="i-help" /></AppIconButton>
        <AppIconButton label="导出" tone="toolbar"><AppIcon name="i-download" /></AppIconButton>
        <AppIconButton label="自定义列" tone="toolbar"><AppIcon name="i-layout" /></AppIconButton>
      </template>
    </AppListToolbar>
    <AppTable caption="产品列表" label="产品列表" :width="2040">
      <colgroup>
        <col style="width: 28px" />
        <col style="width: 80px" />
        <col style="width: 160px" />
        <col style="width: 140px" />
        <col style="width: 100px" />
        <col style="width: 140px" />
        <col style="width: 140px" />
        <col style="width: 90px" />
        <col style="width: 140px" />
        <col style="width: 140px" />
        <col style="width: 120px" />
        <col style="width: 140px" />
        <col style="width: 80px" />
        <col style="width: 140px" />
        <col style="width: 150px" />
        <col style="width: 150px" />
        <col style="width: 2px" />
        <col style="width: 100px" />
      </colgroup>
      <thead>
        <tr>
          <AppTh sticky="left" class="pl-8">
            <AppCheckbox aria-label="全选" />
          </AppTh>
          <AppTh>图片</AppTh>
          <AppTh>
            <button :class="sortButton" type="button" data-testid="product-sort" disabled>
              品名/SKU<AppIcon name="i-sort" icon-class="h-12 w-8 text-sort-icon [stroke:none]" />
            </button>
          </AppTh>
          <AppTh>
            <button :class="sortButton" type="button" data-testid="product-sort" disabled>
              型号<AppIcon name="i-sort" icon-class="h-12 w-8 text-sort-icon [stroke:none]" />
            </button>
          </AppTh>
          <AppTh>单位</AppTh>
          <AppTh>
            <button :class="sortButton" type="button" data-testid="product-sort" disabled>
              分类<AppIcon name="i-sort" icon-class="h-12 w-8 text-sort-icon [stroke:none]" />
            </button>
          </AppTh>
          <AppTh>
            <button :class="sortButton" type="button" data-testid="product-sort" disabled>
              品牌<AppIcon name="i-sort" icon-class="h-12 w-8 text-sort-icon [stroke:none]" />
            </button>
          </AppTh>
          <AppTh>
            <button :class="sortButton" type="button" data-testid="product-sort" disabled>
              采购交期<AppIcon name="i-sort" icon-class="h-12 w-8 text-sort-icon [stroke:none]" />
            </button>
          </AppTh>
          <AppTh>
            <button :class="sortButton" type="button" data-testid="product-sort" disabled>
              采购成本<AppIcon name="i-sort" icon-class="h-12 w-8 text-sort-icon [stroke:none]" />
            </button>
          </AppTh>
          <AppTh>
            <button :class="sortButton" type="button" data-testid="product-sort" disabled>
              供应商报价<AppIcon name="i-sort" icon-class="h-12 w-8 text-sort-icon [stroke:none]" />
            </button>
          </AppTh>
          <AppTh>
            组合情况<AppIcon name="i-info" :size="13" class="ml-2 text-placeholder" />
          </AppTh>
          <AppTh>默认头程费用</AppTh>
          <AppTh>
            <button :class="sortButton" type="button" data-testid="product-sort" disabled>
              状态<AppIcon name="i-sort" icon-class="h-12 w-8 text-sort-icon [stroke:none]" />
            </button>
          </AppTh>
          <AppTh>Listing配对</AppTh>
          <AppTh>
            <button :class="sortButton" type="button" data-testid="product-sort" disabled>
              创建时间<AppIcon name="i-sort" icon-class="h-12 w-8 text-sort-icon [stroke:none]" />
            </button>
          </AppTh>
          <AppTh>
            <button :class="sortButton" type="button" data-testid="product-sort" disabled>
              更新时间<AppIcon name="i-sort" icon-class="h-12 w-8 text-sort-icon [stroke:none]" />
            </button>
          </AppTh>
          <AppTh></AppTh>
          <AppTh sticky="right">操作</AppTh>
        </tr>
      </thead>
      <tbody></tbody>
      <template #overlay>
        <div
          data-testid="product-empty"
          class="pointer-events-none absolute inset-x-0 top-40 bottom-8 z-2 flex items-center justify-center text-12/18 text-muted"
        >
          <span>暂无数据</span>
        </div>
      </template>
      <template #footer>
        <AppPager :page="1" :page-count="1" label="产品分页">
          <template #prefix><span>共0条</span></template>
          <template #suffix>
            <button :class="pageSizeClass" type="button">
              20条/页<AppIcon name="i-chev-down" :size="10" class="text-placeholder" />
            </button>
            <span class="inline-flex items-center gap-7 pl-3 text-body">
              前往<input :class="pageJumpInput" value="1" aria-label="前往页码" />页
            </span>
          </template>
        </AppPager>
      </template>
    </AppTable>
  </section>
  <ProductFormDialog :open="formOpen" @close="formOpen = false" @feedback="showToast" />
  <AppToast :message="toast" data-testid="product-toast" />
</template>
