<script setup lang="ts">
import AppIcon from '../ui/AppIcon.vue'

const emit = defineEmits<{ edit: [] }>()

const rowBase =
  'mb-8 grid grid-cols-[221px_466px_minmax(0,1fr)_100px] items-center rounded-4 border border-line bg-white px-20 last:mb-0'
const rowClass = `${rowBase} min-h-70`
const rowTallClass = `${rowBase} min-h-90`
const rowXTallClass = `${rowBase} min-h-112`
const labelClass = 'min-w-0 pr-12 text-13/22 text-ink'
const middleClass = 'min-w-0 pr-10'
const valueClass = 'min-w-0 text-12/22 font-bold text-body-alt'
const valueNormalClass = 'min-w-0 text-12/20 text-body-alt'
const descClass = 'min-w-0 pr-18 text-12/22 text-config-desc'
const radioListClass = 'flex flex-col gap-8 py-9'
const radioItemClass = 'flex items-center gap-8 whitespace-nowrap text-12/22 text-body-alt'
const radioClass =
  'relative m-0 h-16 w-16 shrink-0 cursor-pointer appearance-none rounded-[50%] border border-radio-border bg-white checked:border-radio-check disabled:cursor-default disabled:border-radio-border-disabled disabled:bg-radio-bg-disabled disabled:checked:border-radio-check-disabled'
const editWrapClass = 'flex justify-end'
const editClass =
  'h-28 w-42 rounded-4 border border-input bg-white text-12/18 text-ink hover:border-primary hover:text-primary'
</script>

<template>
  <div>
    <div class="mt-20 first:mt-0">
      <div class="mb-8 text-12/18 font-bold text-ink">通用</div>
      <div :class="rowClass" data-testid="config-row">
        <div :class="labelClass">数据权限管理</div>
        <div :class="middleClass"><div :class="valueClass">关闭</div></div>
        <div :class="descClass"><p class="truncate">启用角色管理页面【数据权限】功能</p></div>
        <div :class="editWrapClass">
          <button :class="editClass" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
      <div :class="rowTallClass" data-testid="config-row">
        <div :class="labelClass">周起始日</div>
        <div :class="middleClass">
          <div :class="radioListClass" role="radiogroup" aria-label="配置选项">
            <label :class="radioItemClass">
              <input :class="radioClass" type="radio" name="weekStart" disabled />周日
            </label>
            <label :class="radioItemClass">
              <input :class="radioClass" type="radio" name="weekStart" disabled />周一
            </label>
          </div>
        </div>
        <div :class="descClass">
          <p class="truncate">影响销量统计、销量分析、产品表现、利润报表、运营日志页面</p>
        </div>
        <div :class="editWrapClass">
          <button :class="editClass" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
      <div :class="rowTallClass" data-testid="config-row">
        <div :class="labelClass">筛选多站点时的今日取值</div>
        <div :class="middleClass">
          <div :class="radioListClass" role="radiogroup" aria-label="配置选项">
            <label :class="radioItemClass">
              <input
                :class="radioClass"
                type="radio"
                name="multiSiteToday"
                checked
              />各个站点取各自的当天<AppIcon name="i-info" :size="13" class="text-placeholder" />
            </label>
            <label :class="radioItemClass">
              <input
                :class="radioClass"
                type="radio"
                name="multiSiteToday"
              />统一取最晚站点的日期<AppIcon name="i-info" :size="13" class="text-placeholder" />
            </label>
          </div>
        </div>
        <div :class="descClass">
          <p class="truncate">影响首页的实时销量板块、销量分析的今日列表</p>
        </div>
        <div :class="editWrapClass">
          <button :class="editClass" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
    </div>
    <div class="mt-20 first:mt-0">
      <div class="mb-8 text-12/18 font-bold text-ink">部门管理</div>
      <div :class="rowTallClass" data-testid="config-row">
        <div :class="labelClass">部门店铺管理模式</div>
        <div :class="middleClass">
          <div :class="radioListClass" role="radiogroup" aria-label="配置选项">
            <label :class="radioItemClass">
              <input :class="radioClass" type="radio" name="departmentShopMode" checked />分配
            </label>
            <label :class="radioItemClass">
              <input :class="radioClass" type="radio" name="departmentShopMode" />共享
            </label>
          </div>
        </div>
        <div :class="descClass">
          <p class="truncate">1.【分配】：默认只有主管拥有查看权限，主管向下游分配权限</p>
          <p class="mt-1 truncate">2.【共享】：该部门下所有用户均能查看对应数据</p>
        </div>
        <div :class="editWrapClass">
          <button :class="editClass" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
      <div :class="rowClass" data-testid="config-row">
        <div :class="labelClass">数据权限向上级主管共享</div>
        <div :class="middleClass"><div :class="valueClass">开启</div></div>
        <div :class="descClass">
          <p class="truncate">上级主管将拥有下属员工的Listing、SKU及单据数据</p>
        </div>
        <div :class="editWrapClass">
          <button :class="editClass" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
    </div>
    <div class="mt-20 first:mt-0">
      <div class="mb-8 text-12/18 font-bold text-ink">图片显示</div>
      <div :class="rowXTallClass" data-testid="config-row">
        <div :class="labelClass">销售/统计</div>
        <div :class="middleClass">
          <div :class="[valueNormalClass, 'mb-7']">
            订单列表、售后订单、首页、销量分析、销量统计、产品表现、ASIN360
          </div>
          <div class="flex flex-col gap-8" role="radiogroup" aria-label="配置选项">
            <label :class="radioItemClass">
              <input
                :class="radioClass"
                type="radio"
                name="salesImageDisplay"
                checked
              />优先显示本地产品图片
            </label>
            <label :class="radioItemClass">
              <input :class="radioClass" type="radio" name="salesImageDisplay" />优先显示Listing图片
            </label>
          </div>
        </div>
        <div :class="descClass"></div>
        <div :class="editWrapClass">
          <button :class="editClass" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
      <div :class="rowXTallClass" data-testid="config-row">
        <div :class="labelClass">自发货</div>
        <div :class="middleClass">
          <div :class="[valueNormalClass, 'mb-7']">订单管理、订单标发</div>
          <div class="flex flex-col gap-8" role="radiogroup" aria-label="配置选项">
            <label :class="radioItemClass">
              <input
                :class="radioClass"
                type="radio"
                name="selfShipImageDisplay"
                checked
              />优先显示本地产品图片
            </label>
            <label :class="radioItemClass">
              <input
                :class="radioClass"
                type="radio"
                name="selfShipImageDisplay"
              />优先显示Listing/在线商品图片
            </label>
          </div>
        </div>
        <div :class="descClass"></div>
        <div :class="editWrapClass">
          <button :class="editClass" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
input:checked::after {
  content: '';
  position: absolute;
  left: 3px;
  top: 3px;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--color-radio-check);
}

input:disabled:checked::after {
  background: var(--color-radio-check-disabled);
}
</style>
