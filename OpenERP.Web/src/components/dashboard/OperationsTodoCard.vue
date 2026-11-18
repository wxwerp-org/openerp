<template>
  <section class="card span-6 week-card">
    <div class="week-head">
      <div class="card-title">运营待办</div>
      <div class="week-controls">
        <label class="week-check"><input type="checkbox" />仅看自己</label>
        <button class="week-date-pill" type="button">
          <span>2026 第 46 周</span
          ><svg aria-hidden="true" class="ic"><use href="#i-calendar" /></svg>
        </button>
      </div>
    </div>
    <div class="week-days">
      <div class="week-day"><span>日</span><span>11-08</span></div>
      <div class="week-day"><span>一</span><span>11-09</span></div>
      <div class="week-day"><span>二</span><span>11-10</span></div>
      <div class="week-day"><span>三</span><span>11-11</span></div>
      <div class="week-day active"><span>四</span><span>11-12</span></div>
      <div class="week-day"><span>五</span><span>11-13</span></div>
      <div class="week-day"><span>六</span><span>11-14</span></div>
    </div>
    <div class="week-empty">
      <svg aria-hidden="true" class="ic"><use href="#i-clipboard" /></svg>
      <span>暂无数据</span>
    </div>
    <div class="week-actions">
      <button class="btn" type="button">新建待办</button>
      <button class="btn" type="button">写日志</button>
    </div>
  </section>
</template>
