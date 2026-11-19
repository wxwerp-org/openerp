<script setup lang="ts">
import { computed, ref } from 'vue'
import AppIcon from '../ui/AppIcon.vue'
import AppToast from '../ui/AppToast.vue'
import BaseConfig from '../settings/BaseConfig.vue'
import ErpConfig from '../settings/ErpConfig.vue'
import GenericConfig from '../settings/GenericConfig.vue'
import { useToast } from '../composables/useToast'

type SettingsGroup = { title: string; items: string[] }

const groups: Record<string, SettingsGroup> = {
  base: {
    title: '基础',
    items: ['基础', 'ERP 接入', '箱规信息', '飞书授权', '代理商', '制造商', '公司主体', '备货主体'],
  },
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

const firstItemClass = 'flex h-32 w-full cursor-pointer items-center pl-16 text-left text-12/18'
const secondItemClass =
  'flex h-32 w-full cursor-pointer items-center truncate rounded-4 px-8 text-left text-12/32'

function selectGroup(key: string): void {
  activeGroup.value = key
  activeItem.value = groups[key].items[0]
}

function showEditToast(): void {
  showToast('配置编辑面板为原型占位')
}
</script>

<template>
  <section id="page-business" class="flex h-full w-full min-h-0 overflow-hidden bg-page-alt">
    <div class="flex h-full w-full min-h-0 bg-page-alt">
      <div
        id="settingsFirst"
        class="min-h-0 w-120 flex-none overflow-y-auto border-r border-line bg-page-alt pt-12"
      >
        <button
          v-for="(item, key) in groups"
          :key="key"
          :class="[
            firstItemClass,
            activeGroup === key
              ? 'bg-primary-soft font-medium text-primary'
              : 'text-body-strong hover:bg-primary-faint',
          ]"
          type="button"
          :data-settings-group="key"
          @click="selectGroup(key)"
        >
          {{ item.title }}
        </button>
        <div class="mx-16 mt-10 mb-8 h-1 bg-line"></div>
        <button
          :class="[firstItemClass, 'text-body hover:bg-primary-faint']"
          type="button"
          @click="$router.push('/users')"
        >
          <AppIcon name="i-user" :size="13" class="mr-6 text-settings-user-icon" />用户管理
        </button>
      </div>
      <div class="flex min-h-0 w-200 flex-none flex-col border-r border-line bg-white">
        <div
          class="flex h-56 flex-none items-center justify-between border-b border-line px-20 py-12"
        >
          <h1
            id="settingsSecondTitle"
            class="text-20/28 font-semibold whitespace-nowrap text-body-strong"
          >
            {{ group.title }}
          </h1>
          <button
            class="flex h-24 w-24 items-center justify-center rounded-[50%] border border-settings-help-border bg-white text-settings-help-icon hover:border-primary hover:text-primary"
            type="button"
            title="帮助"
            aria-label="帮助"
          >
            <AppIcon name="i-help" />
          </button>
        </div>
        <div id="settingsSecondMenu" class="min-h-0 flex-1 overflow-y-auto p-12">
          <button
            v-for="item in group.items"
            :key="item"
            :class="[
              secondItemClass,
              activeItem === item
                ? 'bg-primary-soft font-medium text-primary'
                : 'text-body-strong hover:bg-primary-faint hover:text-primary',
            ]"
            type="button"
            :data-settings-item="item"
            @click="activeItem = item"
          >
            {{ item }}
          </button>
        </div>
      </div>
      <div
        id="settingsContent"
        class="min-h-0 min-w-0 flex-1 overflow-y-auto bg-page-alt px-24 pt-30 pb-24"
      >
        <div class="w-full min-w-0 animate-config-fade">
          <BaseConfig v-if="showBase" @edit="showEditToast" />
          <ErpConfig v-else-if="activeGroup === 'base' && activeItem === 'ERP 接入'" />
          <GenericConfig
            v-else
            :group-title="group.title"
            :item="activeItem"
            @edit="showEditToast"
          />
        </div>
      </div>
    </div>
    <AppToast :message="toast" />
  </section>
</template>
