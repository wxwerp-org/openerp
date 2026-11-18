<script setup lang="ts">
import { ref } from 'vue'
import OrderNoticeDialog from '../forms/OrderNoticeDialog.vue'
import SegmentedControl from '../components/controls/SegmentedControl.vue'

const noticeOpen = ref(false)
const orderView = ref('销售订单')
</script>

<template>
  <section id="page-orders" class="page page-orders active">
    <div class="subtabs-bar">
      <SegmentedControl
        v-model="orderView"
        group-class="subtabs-bar"
        button-class="subtab"
        :options="['销售订单', '多渠道订单', '推广订单']"
        aria-label="订单视图"
      />
      <button id="openNotice" class="notice-entry" type="button" @click="noticeOpen = true">
        <svg aria-hidden="true" class="ic"><use href="#i-bell" /></svg>订单公告
      </button>
    </div>
    <h1 class="visually-hidden">订单列表</h1>
    <div class="list-searchbar order-filter">
      <button class="control placeholder" type="button">
        Listing负责人<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
      </button>
      <button class="control placeholder" type="button">
        全部国家<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
      </button>
      <button class="control placeholder" type="button">
        全部店铺<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
      </button>
      <button class="control placeholder" type="button">
        配送方式<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
      </button>
      <button class="control placeholder" type="button">
        订单类型<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
      </button>
      <button class="control placeholder" type="button">
        订单状态<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
      </button>
      <button class="control placeholder" type="button">
        请求评论状态<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
      </button>
      <button class="control placeholder" type="button">
        下单时间<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
      </button>
      <button class="control date-range" type="button">
        2026-11-05 - 2026-11-12<svg aria-hidden="true" class="ic"><use href="#i-calendar" /></svg>
      </button>
      <div class="field-box" style="width: 71px">
        <input aria-label="订单号" placeholder="订单号" />
      </div>
      <div class="field-box" style="width: 59px; margin-left: -1px">
        <input aria-label="查询方式" value="模糊" readonly /><svg
          aria-hidden="true"
          class="ic caret"
        >
          <use href="#i-chev-down" />
        </svg>
      </div>
      <div class="search-box">
        <input aria-label="搜索订单" placeholder="输入订单号、买家姓名搜索" />
        <button class="search-btn" type="button" title="搜索" aria-label="搜索">
          <svg aria-hidden="true" class="ic ic-16"><use href="#i-search" /></svg>
        </button>
        <span class="split-line"></span>
        <button class="search-btn" type="button" title="高级搜索" aria-label="高级搜索">
          <svg aria-hidden="true" class="ic ic-16"><use href="#i-filter" /></svg>
        </button>
      </div>
      <button class="btn" type="button">更多筛选</button>
      <button class="btn soft" type="button">重置</button>
    </div>
    <div class="list-toolbar">
      <div class="toolbar-left">
        <button class="btn" type="button">
          标记<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
        </button>
        <button class="btn" type="button">
          订单费用<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
        </button>
        <button class="btn" type="button">
          退款<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
        </button>
        <button class="btn" type="button">Request Review</button>
        <button class="btn" type="button">批量备注</button>
      </div>
      <div class="toolbar-right">
        <button class="btn-icon" type="button" title="刷新" aria-label="刷新">
          <svg aria-hidden="true" class="ic"><use href="#i-refresh" /></svg>
        </button>
        <button class="btn-icon" type="button" title="帮助" aria-label="帮助">
          <svg aria-hidden="true" class="ic"><use href="#i-help" /></svg>
        </button>
        <button class="btn-icon" type="button" title="导出" aria-label="导出">
          <svg aria-hidden="true" class="ic"><use href="#i-download" /></svg>
        </button>
        <button class="btn-icon" type="button" title="自定义列" aria-label="自定义列">
          <svg aria-hidden="true" class="ic"><use href="#i-layout" /></svg>
        </button>
      </div>
    </div>
    <div class="table-region">
      <div class="table-box">
        <div class="table-area">
          <div class="table-scroll" tabindex="0" role="region" aria-label="订单列表">
            <table class="grid-table order-table" style="width: 2271px">
              <caption class="visually-hidden">
                订单列表
              </caption>
              <colgroup>
                <col style="width: 28px" />
                <col style="width: 120px" />
                <col style="width: 160px" />
                <col style="width: 400px" />
                <col style="width: 240px" />
                <col style="width: 160px" />
                <col style="width: 80px" />
                <col style="width: 120px" />
                <col style="width: 200px" />
                <col style="width: 100px" />
                <col style="width: 150px" />
                <col style="width: 150px" />
                <col style="width: 80px" />
                <col style="width: 165px" />
                <col style="width: 1px" />
                <col style="width: 1px" />
                <col style="width: 116px" />
              </colgroup>
              <thead>
                <tr>
                  <th scope="col" class="col-check">
                    <input class="checkbox" type="checkbox" aria-label="全选" />
                  </th>
                  <th scope="col" class="col-sticky-2 order-fixed-store">店铺/国家</th>
                  <th scope="col" class="col-sticky-3 order-fixed-no">订单号</th>
                  <th scope="col">商品信息</th>
                  <th scope="col">品名/SKU</th>
                  <th scope="col">促销编码</th>
                  <th scope="col">订单总金额</th>
                  <th scope="col">毛利润/毛利率</th>
                  <th scope="col">买家信息</th>
                  <th scope="col">备注</th>
                  <th scope="col">
                    <button class="sort-head" type="button" disabled>
                      下单时间<svg class="sort-icon" style="color: #bfc7d2">
                        <use href="#i-sort" />
                      </svg>
                    </button>
                  </th>
                  <th scope="col">发货时限</th>
                  <th scope="col">订单状态</th>
                  <th scope="col">请求评论状态</th>
                  <th scope="col"></th>
                  <th scope="col"></th>
                  <th scope="col" class="col-action">操作</th>
                </tr>
              </thead>
              <tbody></tbody>
            </table>
          </div>
          <div class="empty-overlay"><span>暂无数据</span></div>
        </div>
        <div class="table-pagination">
          <span>共0条</span>
          <button class="page-btn" type="button" disabled aria-label="上一页">
            <svg aria-hidden="true" class="ic ic-12"><use href="#i-chev-left" /></svg>
          </button>
          <span class="page-num">1</span>
          <button class="page-btn" type="button" disabled aria-label="下一页">
            <svg aria-hidden="true" class="ic ic-12"><use href="#i-chev-right" /></svg>
          </button>
          <button class="page-size" type="button">
            20条/页<svg aria-hidden="true" class="ic"><use href="#i-chev-down" /></svg>
          </button>
          <span class="page-jump">前往<input value="1" aria-label="前往页码" />页</span>
        </div>
      </div>
    </div>
  </section>
  <OrderNoticeDialog :open="noticeOpen" @close="noticeOpen = false" />
</template>
