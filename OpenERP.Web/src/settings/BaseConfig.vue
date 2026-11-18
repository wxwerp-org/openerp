<script setup lang="ts">
const emit = defineEmits<{ edit: [] }>()
</script>

<template>
  <div>
    <div class="config-group">
      <div class="config-group-label">通用</div>
      <div class="config-row">
        <div class="config-label">数据权限管理</div>
        <div class="config-middle"><div class="config-value">关闭</div></div>
        <div class="config-desc"><p>启用角色管理页面【数据权限】功能</p></div>
        <div class="config-edit-wrap">
          <button class="config-edit" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
      <div class="config-row tall">
        <div class="config-label">周起始日</div>
        <div class="config-middle">
          <div class="config-radio-list" role="radiogroup" aria-label="配置选项">
            <label class="config-radio"><input type="radio" name="weekStart" disabled />周日</label>
            <label class="config-radio"><input type="radio" name="weekStart" disabled />周一</label>
          </div>
        </div>
        <div class="config-desc">
          <p>影响销量统计、销量分析、产品表现、利润报表、运营日志页面</p>
        </div>
        <div class="config-edit-wrap">
          <button class="config-edit" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
      <div class="config-row tall">
        <div class="config-label">筛选多站点时的今日取值</div>
        <div class="config-middle">
          <div class="config-radio-list" role="radiogroup" aria-label="配置选项">
            <label class="config-radio"
              ><input type="radio" name="multiSiteToday" checked />各个站点取各自的当天<svg
                aria-hidden="true"
                class="ic info-dot"
              >
                <use href="#i-info" /></svg
            ></label>
            <label class="config-radio"
              ><input type="radio" name="multiSiteToday" />统一取最晚站点的日期<svg
                aria-hidden="true"
                class="ic info-dot"
              >
                <use href="#i-info" /></svg
            ></label>
          </div>
        </div>
        <div class="config-desc"><p>影响首页的实时销量板块、销量分析的今日列表</p></div>
        <div class="config-edit-wrap">
          <button class="config-edit" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
    </div>
    <div class="config-group">
      <div class="config-group-label">部门管理</div>
      <div class="config-row tall">
        <div class="config-label">部门店铺管理模式</div>
        <div class="config-middle">
          <div class="config-radio-list" role="radiogroup" aria-label="配置选项">
            <label class="config-radio"
              ><input type="radio" name="departmentShopMode" checked />分配</label
            >
            <label class="config-radio"><input type="radio" name="departmentShopMode" />共享</label>
          </div>
        </div>
        <div class="config-desc">
          <p>1.【分配】：默认只有主管拥有查看权限，主管向下游分配权限</p>
          <p>2.【共享】：该部门下所有用户均能查看对应数据</p>
        </div>
        <div class="config-edit-wrap">
          <button class="config-edit" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
      <div class="config-row">
        <div class="config-label">数据权限向上级主管共享</div>
        <div class="config-middle"><div class="config-value">开启</div></div>
        <div class="config-desc"><p>上级主管将拥有下属员工的Listing、SKU及单据数据</p></div>
        <div class="config-edit-wrap">
          <button class="config-edit" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
    </div>
    <div class="config-group">
      <div class="config-group-label">图片显示</div>
      <div class="config-row x-tall">
        <div class="config-label">销售/统计</div>
        <div class="config-middle">
          <div class="config-value normal" style="margin-bottom: 7px">
            订单列表、售后订单、首页、销量分析、销量统计、产品表现、ASIN360
          </div>
          <div class="config-radio-list" style="padding: 0" role="radiogroup" aria-label="配置选项">
            <label class="config-radio"
              ><input type="radio" name="salesImageDisplay" checked />优先显示本地产品图片</label
            >
            <label class="config-radio"
              ><input type="radio" name="salesImageDisplay" />优先显示Listing图片</label
            >
          </div>
        </div>
        <div class="config-desc"></div>
        <div class="config-edit-wrap">
          <button class="config-edit" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
      <div class="config-row x-tall">
        <div class="config-label">自发货</div>
        <div class="config-middle">
          <div class="config-value normal" style="margin-bottom: 7px">订单管理、订单标发</div>
          <div class="config-radio-list" style="padding: 0" role="radiogroup" aria-label="配置选项">
            <label class="config-radio"
              ><input type="radio" name="selfShipImageDisplay" checked />优先显示本地产品图片</label
            >
            <label class="config-radio"
              ><input type="radio" name="selfShipImageDisplay" />优先显示Listing/在线商品图片</label
            >
          </div>
        </div>
        <div class="config-desc"></div>
        <div class="config-edit-wrap">
          <button class="config-edit" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
    </div>
  </div>
</template>
