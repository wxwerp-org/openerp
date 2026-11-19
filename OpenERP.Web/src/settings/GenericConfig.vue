<script setup lang="ts">
defineProps<{
  groupTitle: string
  item: string
}>()

const emit = defineEmits<{
  edit: []
}>()

const rowBase =
  'mb-8 grid grid-cols-[221px_466px_minmax(0,1fr)_100px] items-center rounded-4 border border-line bg-white px-20 last:mb-0'
const rowClass = `${rowBase} min-h-70`
const rowTallClass = `${rowBase} min-h-90`
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
        <div :class="labelClass">{{ item }}</div>
        <div :class="middleClass"><div :class="valueClass">开启</div></div>
        <div :class="descClass">
          <p class="truncate">
            配置{{ groupTitle }}模块下的{{ item }}能力，修改后将应用于对应业务页面
          </p>
        </div>
        <div :class="editWrapClass">
          <button :class="editClass" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
      <div :class="rowTallClass" data-testid="config-row">
        <div :class="labelClass">操作权限</div>
        <div :class="middleClass">
          <div :class="radioListClass" role="radiogroup" aria-label="操作权限">
            <label :class="radioItemClass">
              <input
                :class="radioClass"
                type="radio"
                name="operationPermission"
                checked
              />仅管理员可操作
            </label>
            <label :class="radioItemClass">
              <input :class="radioClass" type="radio" name="operationPermission" />按角色权限控制
            </label>
          </div>
        </div>
        <div :class="descClass">
          <p class="truncate">控制{{ item }}的查看与编辑权限，变更记录可在操作日志中追溯</p>
        </div>
        <div :class="editWrapClass">
          <button :class="editClass" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
    </div>
    <div class="mt-20 first:mt-0">
      <div class="mb-8 text-12/18 font-bold text-ink">通知设置</div>
      <div :class="rowClass" data-testid="config-row">
        <div :class="labelClass">业务通知</div>
        <div :class="middleClass"><div :class="valueClass">开启</div></div>
        <div :class="descClass">
          <p class="truncate">相关配置变更时，通过站内信通知{{ groupTitle }}模块负责人</p>
        </div>
        <div :class="editWrapClass">
          <button :class="editClass" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
      <div :class="rowClass" data-testid="config-row">
        <div :class="labelClass">更新说明</div>
        <div :class="middleClass"><div :class="valueNormalClass">当前为原型演示内容</div></div>
        <div :class="descClass">
          <p class="truncate">此页面仅用于展示 Open ERP 业务配置的界面结构与视觉规范</p>
        </div>
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
