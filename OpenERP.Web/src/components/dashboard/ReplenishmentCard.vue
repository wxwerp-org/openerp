<template>
  <section class="card span-6 replenish-card">
    <div class="replenish-head">
      <div class="card-title">
        补货建议<svg aria-hidden="true" class="ic title-caret"><use href="#i-chev-right" /></svg>
      </div>
      <div class="card-head-right">
        <label class="week-check"><input type="checkbox" />仅看我关注</label>
        <button class="btn" type="button" style="height: 28px">
          全部<svg aria-hidden="true" class="ic caret"><use href="#i-chev-down" /></svg>
        </button>
      </div>
    </div>
    <div class="replenish-body">
      <div class="replenish-summary">
        <span class="tag red">断货风险 2</span><span class="tag amber">加急 4</span
        ><span class="tag green">正常 8</span>
        <span class="muted" style="margin-left: auto"
          >建议补货总量 <b style="color: var(--text); font-weight: 500">1,840</b> 件</span
        >
      </div>
      <table class="replenish-table">
        <colgroup>
          <col style="width: 31%" />
          <col style="width: 14%" />
          <col style="width: 13%" />
          <col style="width: 14%" />
          <col style="width: 14%" />
          <col style="width: 14%" />
        </colgroup>
        <thead>
          <tr>
            <th scope="col">商品 / SKU</th>
            <th scope="col">当前库存</th>
            <th scope="col">日均销量</th>
            <th scope="col">可售天数</th>
            <th scope="col">建议补货</th>
            <th scope="col">紧急程度</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <div class="rp-product">
                <span class="rp-thumb"
                  ><svg aria-hidden="true" class="ic"><use href="#i-box" /></svg
                ></span>
                <div style="min-width: 0">
                  <div class="rp-name">磁吸折叠露营灯</div>
                  <div style="font-size: 10px; color: var(--muted)">LX-CL001</div>
                </div>
              </div>
            </td>
            <td class="r">186</td>
            <td class="r">16.2</td>
            <td class="r days danger">11.5 天</td>
            <td class="r">260</td>
            <td><span class="urgency urgent">断货风险</span></td>
          </tr>
          <tr>
            <td>
              <div class="rp-product">
                <span class="rp-thumb"
                  ><svg aria-hidden="true" class="ic"><use href="#i-box" /></svg
                ></span>
                <div style="min-width: 0">
                  <div class="rp-name">宠物除毛梳</div>
                  <div style="font-size: 10px; color: var(--muted)">LX-PT012</div>
                </div>
              </div>
            </td>
            <td class="r">342</td>
            <td class="r">18.6</td>
            <td class="r days warn">18.4 天</td>
            <td class="r">320</td>
            <td><span class="urgency high">加急</span></td>
          </tr>
          <tr>
            <td>
              <div class="rp-product">
                <span class="rp-thumb"
                  ><svg aria-hidden="true" class="ic"><use href="#i-box" /></svg
                ></span>
                <div style="min-width: 0">
                  <div class="rp-name">无线快充支架</div>
                  <div style="font-size: 10px; color: var(--muted)">LX-CH028</div>
                </div>
              </div>
            </td>
            <td class="r">526</td>
            <td class="r">12.8</td>
            <td class="r days">41.1 天</td>
            <td class="r">460</td>
            <td><span class="urgency normal">正常</span></td>
          </tr>
          <tr>
            <td>
              <div class="rp-product">
                <span class="rp-thumb"
                  ><svg aria-hidden="true" class="ic"><use href="#i-box" /></svg
                ></span>
                <div style="min-width: 0">
                  <div class="rp-name">户外防水收纳袋</div>
                  <div style="font-size: 10px; color: var(--muted)">LX-BG031</div>
                </div>
              </div>
            </td>
            <td class="r">788</td>
            <td class="r">10.4</td>
            <td class="r days">75.8 天</td>
            <td class="r">—</td>
            <td><span class="urgency normal">充足</span></td>
          </tr>
        </tbody>
      </table>
      <div class="replenish-foot">
        <span>库存数据更新于 2026-11-12 15:45</span
        ><button class="link-btn" type="button">
          查看全部建议<svg aria-hidden="true" class="ic ic-12"><use href="#i-chev-right" /></svg>
        </button>
      </div>
    </div>
  </section>
</template>
