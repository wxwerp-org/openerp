<script setup lang="ts">
import { ref } from 'vue'
import ProductFormDialog from '../forms/ProductFormDialog.vue'
import { useToast } from '../composables/useToast'
import SegmentedControl from '../components/controls/SegmentedControl.vue'

const skuMode = ref('按SKU')
const productType = ref('全部')

const formOpen = ref(false)
const { toast, showToast } = useToast()
</script>

<template>
<section class="page page-product page-list active" id="page-product">
        <h1 class="visually-hidden">产品管理</h1>
    <div class="list-searchbar product-filter">
          <SegmentedControl v-model="skuMode" group-class="toggle-group toggle-sku" :options="['按SKU', '按SPU']" aria-label="产品维度" />
          <SegmentedControl v-model="productType" group-class="toggle-group toggle-type" :options="['全部', '普通产品', '组合产品']" aria-label="产品类型" />
          <button class="control placeholder" type="button">分类<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down"/></svg></button>
          <button class="control placeholder" type="button">属性<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down"/></svg></button>
          <button class="control placeholder" type="button">品牌<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down"/></svg></button>
          <button class="control placeholder" type="button">状态<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down"/></svg></button>
          <button class="control placeholder" type="button">产品标签<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down"/></svg></button>
          <button class="control placeholder" type="button">开启状态<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down"/></svg></button>
          <div class="created-field">
            <button class="control placeholder" type="button">创建时间<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down"/></svg></button>
            <button class="control date-range" type="button">开始日期 - 结束日期<svg aria-hidden="true" class="ic"><use href="#i-calendar"/></svg></button>
          </div>
          <div class="field-box field-sku"><input aria-label="SKU" placeholder="SKU"><svg aria-hidden="true" class="ic caret"><use href="#i-chev-down"/></svg></div>
          <div class="field-box field-mode"><input aria-label="查询方式" value="模糊" readonly><svg aria-hidden="true" class="ic caret"><use href="#i-chev-down"/></svg></div>
          <div class="search-box">
            <input aria-label="搜索产品" placeholder="输入品名、SKU、识别码搜索">
            <button class="search-btn" type="button" title="搜索" aria-label="搜索"><svg aria-hidden="true" class="ic ic-16"><use href="#i-search"/></svg></button>
            <span class="split-line"></span>
            <button class="search-btn" type="button" title="高级搜索" aria-label="高级搜索"><svg aria-hidden="true" class="ic ic-16"><use href="#i-filter"/></svg></button>
          </div>
          <button class="btn" type="button">更多筛选</button>
          <button class="btn soft" type="button">重置</button>
        </div>
        <div class="list-toolbar">
          <div class="toolbar-left">
            <button class="btn primary" type="button" id="openProductForm" @click="formOpen = true">添加产品</button>
            <button class="btn" type="button">添加多属性产品</button>
            <button class="btn" type="button">导入<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down"/></svg></button>
            <button class="btn" type="button">同步图片<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down"/></svg></button>
            <button class="btn" type="button">打印产品条码</button>
            <button class="btn" type="button">更多<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down"/></svg></button>
          </div>
          <div class="toolbar-right">
            <button class="btn-icon" type="button" title="刷新" aria-label="刷新"><svg aria-hidden="true" class="ic"><use href="#i-refresh"/></svg></button>
            <button class="btn-icon" type="button" title="帮助" aria-label="帮助"><svg aria-hidden="true" class="ic"><use href="#i-help"/></svg></button>
            <button class="btn-icon" type="button" title="导出" aria-label="导出"><svg aria-hidden="true" class="ic"><use href="#i-download"/></svg></button>
            <button class="btn-icon" type="button" title="自定义列" aria-label="自定义列"><svg aria-hidden="true" class="ic"><use href="#i-layout"/></svg></button>
          </div>
        </div>
        <div class="table-region">
          <div class="table-box">
            <div class="table-area">
              <div class="table-scroll" tabindex="0" role="region" aria-label="产品列表">
                <table class="grid-table product-table" style="width:2040px">
                  <caption class="visually-hidden">产品列表</caption>
                  <colgroup>
                    <col style="width:28px"><col style="width:80px"><col style="width:160px"><col style="width:140px"><col style="width:100px">
                    <col style="width:140px"><col style="width:140px"><col style="width:90px"><col style="width:140px"><col style="width:140px">
                    <col style="width:120px"><col style="width:140px"><col style="width:80px"><col style="width:140px"><col style="width:150px">
                    <col style="width:150px"><col style="width:2px"><col style="width:100px">
                  </colgroup>
                  <thead><tr>
                    <th scope="col" class="col-check"><input class="checkbox" type="checkbox" aria-label="全选"></th>
                    <th scope="col">图片</th>
                    <th scope="col"><button class="sort-head" type="button" disabled>品名/SKU<svg class="sort-icon" style="color:#BFC7D2"><use href="#i-sort"/></svg></button></th>
                    <th scope="col"><button class="sort-head" type="button" disabled>型号<svg class="sort-icon" style="color:#BFC7D2"><use href="#i-sort"/></svg></button></th>
                    <th scope="col">单位</th>
                    <th scope="col"><button class="sort-head" type="button" disabled>分类<svg class="sort-icon" style="color:#BFC7D2"><use href="#i-sort"/></svg></button></th>
                    <th scope="col"><button class="sort-head" type="button" disabled>品牌<svg class="sort-icon" style="color:#BFC7D2"><use href="#i-sort"/></svg></button></th>
                    <th scope="col"><button class="sort-head" type="button" disabled>采购交期<svg class="sort-icon" style="color:#BFC7D2"><use href="#i-sort"/></svg></button></th>
                    <th scope="col"><button class="sort-head" type="button" disabled>采购成本<svg class="sort-icon" style="color:#BFC7D2"><use href="#i-sort"/></svg></button></th>
                    <th scope="col"><button class="sort-head" type="button" disabled>供应商报价<svg class="sort-icon" style="color:#BFC7D2"><use href="#i-sort"/></svg></button></th>
                    <th scope="col">组合情况<svg aria-hidden="true" class="ic info-dot"><use href="#i-info"/></svg></th>
                    <th scope="col">默认头程费用</th>
                    <th scope="col"><button class="sort-head" type="button" disabled>状态<svg class="sort-icon" style="color:#BFC7D2"><use href="#i-sort"/></svg></button></th>
                    <th scope="col">Listing配对</th>
                    <th scope="col"><button class="sort-head" type="button" disabled>创建时间<svg class="sort-icon" style="color:#BFC7D2"><use href="#i-sort"/></svg></button></th>
                    <th scope="col"><button class="sort-head" type="button" disabled>更新时间<svg class="sort-icon" style="color:#BFC7D2"><use href="#i-sort"/></svg></button></th>
                    <th scope="col"></th>
                    <th scope="col" class="col-action">操作</th>
                  </tr></thead>
                  <tbody></tbody>
                </table>
              </div>
              <div class="empty-overlay"><span>暂无数据</span></div>
            </div>
            <div class="table-pagination">
              <span>共0条</span>
              <button class="page-btn" type="button" disabled aria-label="上一页"><svg aria-hidden="true" class="ic ic-12"><use href="#i-chev-left"/></svg></button>
              <span class="page-num">1</span>
              <button class="page-btn" type="button" disabled aria-label="下一页"><svg aria-hidden="true" class="ic ic-12"><use href="#i-chev-right"/></svg></button>
              <button class="page-size" type="button">20条/页<svg aria-hidden="true" class="ic"><use href="#i-chev-down"/></svg></button>
              <span class="page-jump">前往<input value="1" aria-label="前往页码">页</span>
            </div>
          </div>
        </div>
      </section>
  <ProductFormDialog :open="formOpen" @close="formOpen = false" @feedback="showToast" />
  <div v-show="toast" class="toast" role="status">{{ toast }}</div>
</template>
