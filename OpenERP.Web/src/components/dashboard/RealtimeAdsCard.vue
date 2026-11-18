<template>
  <section class="card span-6 realtime-card">
    <div class="card-head">
      <div class="card-title">
        实时广告<svg aria-hidden="true" class="ic title-caret"><use href="#i-chev-right" /></svg>
      </div>
      <div class="card-head-right">
        <span class="realtime-ad-meta"
          >统计日期：11-11（站点）
          <svg aria-hidden="true" class="ic info-dot"><use href="#i-info" /></svg
        ></span>
        <span class="realtime-ad-meta">更新于 2 分钟前</span>
        <button class="header-icon-btn" type="button" title="广告设置" aria-label="广告设置">
          <svg aria-hidden="true" class="ic"><use href="#i-settings" /></svg>
        </button>
      </div>
    </div>
    <div class="realtime-body realtime-ad-grid">
      <div class="realtime-stat">
        <div class="realtime-label">广告花费</div>
        <div class="realtime-value tnum">$3,264.00</div>
        <div class="realtime-yest">昨 <span class="num">$2,980.00</span></div>
      </div>
      <div class="realtime-stat">
        <div class="realtime-label">广告销售额</div>
        <div class="realtime-value tnum">$18,420.00</div>
        <div class="realtime-yest">昨 <span class="num">$16,530.00</span></div>
      </div>
      <div class="realtime-stat">
        <div class="realtime-label">广告订单量</div>
        <div class="realtime-value tnum">386</div>
        <div class="realtime-yest">昨 <span class="num">342</span></div>
      </div>
      <div class="realtime-stat">
        <div class="realtime-label">ACOS</div>
        <div class="realtime-value tnum">17.7%</div>
        <div class="realtime-yest">昨 <span class="num">18.0%</span></div>
      </div>
      <div class="realtime-stat">
        <div class="realtime-label">TACOS</div>
        <div class="realtime-value tnum">7.3%</div>
        <div class="realtime-yest">昨 <span class="num">7.8%</span></div>
      </div>
    </div>
  </section>
</template>
