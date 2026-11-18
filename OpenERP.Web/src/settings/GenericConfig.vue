<script setup lang="ts">
defineProps<{
  groupTitle: string
  item: string
}>()

const emit = defineEmits<{
  edit: []
}>()
</script>

<template>
  <div>
    <div class="config-group">
      <div class="config-group-label">通用</div>
      <div class="config-row">
        <div class="config-label">{{ item }}</div>
        <div class="config-middle"><div class="config-value">开启</div></div>
        <div class="config-desc">
          <p>配置{{ groupTitle }}模块下的{{ item }}能力，修改后将应用于对应业务页面</p>
        </div>
        <div class="config-edit-wrap">
          <button class="config-edit" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
      <div class="config-row tall">
        <div class="config-label">操作权限</div>
        <div class="config-middle">
          <div class="config-radio-list" role="radiogroup" aria-label="操作权限">
            <label class="config-radio"
              ><input type="radio" name="operationPermission" checked />仅管理员可操作</label
            >
            <label class="config-radio"
              ><input type="radio" name="operationPermission" />按角色权限控制</label
            >
          </div>
        </div>
        <div class="config-desc">
          <p>控制{{ item }}的查看与编辑权限，变更记录可在操作日志中追溯</p>
        </div>
        <div class="config-edit-wrap">
          <button class="config-edit" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
    </div>
    <div class="config-group">
      <div class="config-group-label">通知设置</div>
      <div class="config-row">
        <div class="config-label">业务通知</div>
        <div class="config-middle"><div class="config-value">开启</div></div>
        <div class="config-desc">
          <p>相关配置变更时，通过站内信通知{{ groupTitle }}模块负责人</p>
        </div>
        <div class="config-edit-wrap">
          <button class="config-edit" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
      <div class="config-row">
        <div class="config-label">更新说明</div>
        <div class="config-middle"><div class="config-value normal">当前为原型演示内容</div></div>
        <div class="config-desc"><p>此页面仅用于展示 Open ERP 业务配置的界面结构与视觉规范</p></div>
        <div class="config-edit-wrap">
          <button class="config-edit" type="button" @click="emit('edit')">编辑</button>
        </div>
      </div>
    </div>
  </div>
</template>
