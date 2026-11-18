<script setup lang="ts">
import InitGuideCard from '../components/dashboard/InitGuideCard.vue'
import RealtimeSalesCard from '../components/dashboard/RealtimeSalesCard.vue'
import RealtimeAdsCard from '../components/dashboard/RealtimeAdsCard.vue'
import PerformanceTrendCard from '../components/dashboard/PerformanceTrendCard.vue'
import RankingCard from '../components/dashboard/RankingCard.vue'
import TodoCard from '../components/dashboard/TodoCard.vue'
import OperationsTodoCard from '../components/dashboard/OperationsTodoCard.vue'
import OrderDistributionCard from '../components/dashboard/OrderDistributionCard.vue'
import StorePerformanceCard from '../components/dashboard/StorePerformanceCard.vue'
import ReviewSummaryCard from '../components/dashboard/ReviewSummaryCard.vue'
import InventoryOverviewCard from '../components/dashboard/InventoryOverviewCard.vue'
import ReplenishmentCard from '../components/dashboard/ReplenishmentCard.vue'
import LogisticsBoardCard from '../components/dashboard/LogisticsBoardCard.vue'
import ProfitCard from '../components/dashboard/ProfitCard.vue'
</script>

<template>
  <section id="page-dashboard" class="page page-dashboard active">
    <h1 class="visually-hidden">工作台</h1>
    <div class="dash-header">
      <div class="dash-header-left">
        <button class="control placeholder" type="button">
          全部国家<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
        </button>
        <button class="control placeholder" type="button">
          全部店铺<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
        </button>
        <button class="control placeholder" type="button">
          Listing负责人<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
        </button>
        <button class="control" type="button">
          USD<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
        </button>
        <button class="btn soft" type="button">重置</button>
        <button class="btn text" type="button" style="padding: 0 8px; color: #303133">
          核对说明
        </button>
      </div>
      <div class="dash-header-right">
        <span class="time-pill"
          ><span class="label">北京</span><span class="tnum">11-12 15:47</span></span
        >
        <span class="time-pill"
          ><span class="label">英国</span><span class="tnum">11-12 07:47</span></span
        >
        <span class="time-pill"
          ><span class="label">美东</span><span class="tnum">11-12 02:47</span></span
        >
        <span class="time-pill"
          ><span class="label">太平洋</span><span class="tnum">11-11 23:47</span></span
        >
        <span class="header-divider"></span>
        <button class="header-mini-btn blue" type="button">
          时钟<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
        </button>
        <span class="header-divider"></span>
        <button
          class="btn soft"
          type="button"
          style="height: 28px; line-height: 26px; padding: 0 10px"
        >
          旧版
        </button>
        <span class="header-divider"></span>
        <button class="header-mini-btn" type="button">
          初始化引导<svg aria-hidden="true" class="ic caret" style="transform: rotate(180deg)">
            <use href="#i-chev-down" />
          </svg>
        </button>
        <span class="header-divider"></span>
        <button class="header-icon-btn" type="button" title="刷新" aria-label="刷新">
          <svg aria-hidden="true" class="ic"><use href="#i-refresh" /></svg>
        </button>
        <button class="header-icon-btn" type="button" title="帮助" aria-label="帮助">
          <svg aria-hidden="true" class="ic"><use href="#i-help" /></svg>
        </button>
        <button class="header-icon-btn" type="button" title="自定义布局" aria-label="自定义布局">
          <svg aria-hidden="true" class="ic"><use href="#i-layout" /></svg>
        </button>
      </div>
    </div>
    <div class="dash-scroll">
      <div class="dash-grid">
        <InitGuideCard />
        <RealtimeSalesCard />
        <RealtimeAdsCard />
        <PerformanceTrendCard />
        <RankingCard />
        <TodoCard />
        <OperationsTodoCard />
        <OrderDistributionCard />
        <StorePerformanceCard />
        <ReviewSummaryCard />
        <InventoryOverviewCard />
        <ReplenishmentCard />
        <LogisticsBoardCard />
        <ProfitCard />
      </div>
    </div>
  </section>
</template>
