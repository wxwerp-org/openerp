<template>
  <section class="card span-3 store-card">
              <div class="card-head">
                <div class="card-title">店铺绩效</div>
                <button class="link-btn" type="button">详情<svg aria-hidden="true" class="ic ic-12"><use href="#i-chev-right"/></svg></button>
              </div>
              <div class="store-body">
                <div class="card-subtip" style="margin-bottom:8px">报表日期：2026-11-11</div>
                <div class="store-metrics">
                  <div class="store-metric"><label>异常店铺数</label><strong class="tnum">2</strong></div>
                  <div class="store-metric"><label>平均目标达成</label><strong class="tnum">96.4%</strong></div>
                </div>
                <table class="store-table">
                  <thead><tr><th scope="col">店铺</th><th scope="col">异常</th><th scope="col">目标值</th></tr></thead>
                  <tbody>
                    <tr><td><span class="store-name"><i class="store-dot"></i>Aurora US</span></td><td>1</td><td>95%</td></tr>
                    <tr><td><span class="store-name"><i class="store-dot" style="background:#65A6FF"></i>NovaTech DE</span></td><td>1</td><td>97%</td></tr>
                    <tr><td><span class="store-name"><i class="store-dot" style="background:#A8CBFF"></i>LumoLife JP</span></td><td>0</td><td>94%</td></tr>
                    <tr><td><span class="store-name"><i class="store-dot" style="background:#D5E5FF"></i>PeakOut US</span></td><td>0</td><td>93%</td></tr>
                  </tbody>
                </table>
              </div>
            </section>
</template>
