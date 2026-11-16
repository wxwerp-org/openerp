<template>
  <section class="card span-6 logistics-card">
              <div class="logistics-head">
                <div class="card-title">物流看板<svg aria-hidden="true" class="ic title-caret"><use href="#i-chev-right"/></svg></div>
                <button class="link-btn" type="button">物流追踪<svg aria-hidden="true" class="ic ic-12"><use href="#i-chev-right"/></svg></button>
              </div>
              <div class="logistics-body">
                <div class="logistics-stats">
                  <div class="logistics-stat active"><div class="logistics-stat-label">运输途中</div><div class="logistics-stat-value tnum">1,280<small>PCS</small></div><div class="logistics-stat-sub">较昨日 <span style="color:var(--success)">+86</span></div></div>
                  <div class="logistics-stat"><div class="logistics-stat-label">延误</div><div class="logistics-stat-value tnum">46<small>PCS</small></div><div class="logistics-stat-sub">较昨日 <span style="color:var(--danger)">+5</span></div></div>
                  <div class="logistics-stat"><div class="logistics-stat-label">今日已发货</div><div class="logistics-stat-value tnum">320<small>PCS</small></div><div class="logistics-stat-sub">目标 400 PCS</div></div>
                  <div class="logistics-stat"><div class="logistics-stat-label">今日已签收</div><div class="logistics-stat-value tnum">210<small>PCS</small></div><div class="logistics-stat-sub">目标 260 PCS</div></div>
                </div>
                <div class="shipment-table-wrap">
                  <table class="shipment-table">
                    <thead><tr><th scope="col">物流单号</th><th scope="col">渠道</th><th scope="col">目的仓</th><th scope="col">预计到达</th><th scope="col">状态</th></tr></thead>
                    <tbody>
                      <tr><td class="plan-no">LX20261108001</td><td>海派专线</td><td>FBA US-LAX</td><td>11-18</td><td><span class="shipment-status"><i class="status-dot transit"></i>运输中</span></td></tr>
                      <tr><td class="plan-no">LX20261106006</td><td>空派快线</td><td>FBA DE-BER</td><td>11-15</td><td><span class="shipment-status"><i class="status-dot late"></i>延误</span></td></tr>
                      <tr><td class="plan-no">LX20261109018</td><td>卡派</td><td>海外仓 US-ONT</td><td>11-21</td><td><span class="shipment-status"><i class="status-dot"></i>已签收</span></td></tr>
                    </tbody>
                  </table>
                </div>
              </div>
            </section>
</template>
