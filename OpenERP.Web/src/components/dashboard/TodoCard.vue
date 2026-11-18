<template>
  <section class="card span-6 todo-card">
    <div class="card-head">
      <div class="card-title">
        待处理事项<svg aria-hidden="true" class="ic title-caret"><use href="#i-chev-right" /></svg>
      </div>
      <div class="card-head-right">
        <span class="muted" style="font-size: 11px">更新于 15:47</span
        ><button class="trend-icon-btn" type="button" title="刷新" aria-label="刷新">
          <svg aria-hidden="true" class="ic"><use href="#i-refresh" /></svg>
        </button>
      </div>
    </div>
    <div class="todo-body">
      <div class="todo-section">
        <div class="todo-section-title">
          <span class="section-label">运营</span
          ><button class="link-btn" type="button">
            查看全部<svg aria-hidden="true" class="ic ic-12"><use href="#i-chev-right" /></svg>
          </button>
        </div>
        <div class="todo-items">
          <div class="todo-item">
            <span class="icon-circle warn"
              ><svg aria-hidden="true" class="ic"><use href="#i-warning" /></svg></span
            ><span class="todo-name">库存不足</span
            ><span class="todo-count" style="color: #b96800">6</span>
          </div>
          <div class="todo-item">
            <span class="icon-circle"
              ><svg aria-hidden="true" class="ic"><use href="#i-box" /></svg></span
            ><span class="todo-name">库存冗余</span><span class="todo-count">4</span>
          </div>
          <div class="todo-item">
            <span class="icon-circle danger"
              ><svg aria-hidden="true" class="ic"><use href="#i-warning" /></svg></span
            ><span class="todo-name">被跟卖</span
            ><span class="todo-count" style="color: #e52e2e">2</span>
          </div>
          <div class="todo-item">
            <span class="icon-circle warn"
              ><svg aria-hidden="true" class="ic"><use href="#i-chev-down" /></svg></span
            ><span class="todo-name">丢失购物车</span
            ><span class="todo-count" style="color: #b96800">1</span>
          </div>
          <div class="todo-item">
            <span class="icon-circle"
              ><svg aria-hidden="true" class="ic"><use href="#i-mail" /></svg></span
            ><span class="todo-name">待回复邮件</span><span class="todo-count">18</span>
          </div>
          <div class="todo-item">
            <span class="icon-circle danger"
              ><svg aria-hidden="true" class="ic"><use href="#i-file" /></svg></span
            ><span class="todo-name">发票上传失败</span
            ><span class="todo-count" style="color: #e52e2e">1</span>
          </div>
        </div>
      </div>
      <div class="todo-divider"></div>
      <div class="todo-section">
        <div class="todo-section-title">
          <span class="section-label">审批</span
          ><button class="link-btn" type="button">
            查看全部<svg aria-hidden="true" class="ic ic-12"><use href="#i-chev-right" /></svg>
          </button>
        </div>
        <div class="todo-items todo-subgrid">
          <div class="todo-item">
            <span class="icon-circle"
              ><svg aria-hidden="true" class="ic"><use href="#i-clipboard" /></svg></span
            ><span class="todo-name">待我处理</span><span class="todo-count">5</span>
          </div>
          <div class="todo-item">
            <span class="icon-circle"
              ><svg aria-hidden="true" class="ic"><use href="#i-file" /></svg></span
            ><span class="todo-name">我发起</span><span class="todo-count">2</span>
          </div>
          <div class="todo-item">
            <span class="icon-circle"
              ><svg aria-hidden="true" class="ic"><use href="#i-chev-left" /></svg></span
            ><span class="todo-name">已驳回</span><span class="todo-count">1</span>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>
