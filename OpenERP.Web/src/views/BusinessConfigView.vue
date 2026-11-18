<script setup lang="ts">
import { computed, ref } from 'vue'
import AppIcon from '../components/navigation/AppIcon.vue'
import BaseConfig from '../settings/BaseConfig.vue'
import ErpConfig from '../settings/ErpConfig.vue'
import GenericConfig from '../settings/GenericConfig.vue'
import { useToast } from '../composables/useToast'

type SettingsGroup = { title: string; items: string[] }

const groups: Record<string, SettingsGroup> = {
  base: { title: '基础', items: ['基础', 'ERP 接入', '箱规信息', '飞书授权', '代理商', '制造商', '公司主体', '备货主体'] },
  approval: { title: '审批', items: ['审批设置', '审批流程', '审批模板'] },
  product: { title: '产品', items: ['产品设置', '分类属性', '品牌管理', '产品标签'] },
  sales: { title: '销售', items: ['订单设置', 'Listing设置', '客服邮箱'] },
  selfship: { title: '自发货', items: ['发货设置', '面单模板', '物流渠道'] },
  fba: { title: 'FBA', items: ['FBA设置', '补货规则'] },
  replenish: { title: '补货建议', items: ['补货参数', '预警设置'] },
  purchase: { title: '采购', items: ['采购设置', '供应商管理', '询价设置'] },
  warehouse: { title: '仓库', items: ['仓库设置', '库位管理', 'PDA设置'] },
  pda: { title: 'PDA', items: ['PDA账号', '作业设置'] },
  logistics: { title: '物流', items: ['物流渠道', '运费模板'] },
  service: { title: '客服', items: ['邮件设置', '售后规则'] },
  finance: { title: '财务', items: ['财务设置', '结算规则', '汇率设置'] },
  statistics: { title: '统计', items: ['报表设置', '订阅设置'] },
  multiplatform: { title: '多平台', items: ['平台授权', '店铺设置'] },
}

const activeGroup = ref('base')
const activeItem = ref('基础')
const { toast, showToast } = useToast()
const group = computed(() => groups[activeGroup.value])
const showBase = computed(() => activeGroup.value === 'base' && activeItem.value === '基础')

function selectGroup(key: string): void {
  activeGroup.value = key
  activeItem.value = groups[key].items[0]
}

function showEditToast(): void {
  showToast('配置编辑面板为原型占位')
}
</script>

<template>
  <section class="page page-business active" id="page-business">
    <div class="business-wrap">
      <div class="settings-first" id="settingsFirst">
        <button
          v-for="(item, key) in groups"
          :key="key"
          class="settings-first-item"
          :class="{ active: activeGroup === key }"
          type="button"
          :data-settings-group="key"
          @click="selectGroup(key)"
        >
          {{ item.title }}
        </button>
        <div class="settings-first-divider"></div>
        <button class="settings-first-item user-entry" type="button" @click="$router.push('/users')"><AppIcon name="i-user" />用户管理</button>
      </div>
      <div class="settings-second">
        <div class="settings-second-head">
          <h1 id="settingsSecondTitle">{{ group.title }}</h1>
          <button class="settings-help" type="button" title="帮助" aria-label="帮助"><AppIcon name="i-help" /></button>
        </div>
        <div class="settings-second-menu" id="settingsSecondMenu">
          <button
            v-for="item in group.items"
            :key="item"
            class="settings-second-item"
            :class="{ active: activeItem === item }"
            type="button"
            :data-settings-item="item"
            @click="activeItem = item"
          >
            {{ item }}
          </button>
        </div>
      </div>
      <div class="settings-content" id="settingsContent">
        <div class="settings-content-inner config-fade">
          <BaseConfig v-if="showBase" @edit="showEditToast" />
          <ErpConfig v-else-if="activeGroup === 'base' && activeItem === 'ERP 接入'" />
          <GenericConfig v-else :group-title="group.title" :item="activeItem" @edit="showEditToast" />
        </div>
      </div>
    </div>
    <div v-show="toast" class="toast" role="status">{{ toast }}</div>
  </section>
</template>
