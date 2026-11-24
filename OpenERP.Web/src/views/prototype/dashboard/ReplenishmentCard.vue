<script setup lang="ts">
import AppButton from '../../../ui/AppButton.vue'
import AppCard from '../../../ui/AppCard.vue'
import AppIcon from '../../../ui/AppIcon.vue'
</script>

<template>
  <AppCard data-testid="dashboard-card-replenishment" class="col-span-6 flex h-400 flex-col">
    <div class="flex h-54 flex-none items-start justify-between px-16 pt-16">
      <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
        补货建议<AppIcon
          name="i-chev-right"
          :size="12"
          class="text-title-caret [stroke-width:1.8]"
        />
      </div>
      <div class="flex min-w-0 flex-nowrap items-center gap-10">
        <label class="flex items-center gap-6 whitespace-nowrap text-body"
          ><input type="checkbox" class="size-13 accent-primary" />仅看我关注</label
        >
        <AppButton>全部<AppIcon name="i-chev-down" :size="10" class="opacity-65" /></AppButton>
      </div>
    </div>
    <div class="flex min-h-0 flex-1 flex-col px-16 pb-16">
      <div class="mt-1 mb-10 flex items-center gap-8 whitespace-nowrap">
        <span
          class="inline-flex h-20 items-center justify-center rounded-2 border border-tag-red-border bg-danger-faint px-5 text-11/16 text-danger"
          >断货风险 2</span
        ><span
          class="inline-flex h-20 items-center justify-center rounded-2 border border-tag-amber-border bg-tag-amber-bg px-5 text-11/16 text-warning"
          >加急 4</span
        ><span
          class="inline-flex h-20 items-center justify-center rounded-2 border border-tag-green-border bg-tag-green-bg px-5 text-11/16 text-success"
          >正常 8</span
        >
        <span class="ml-auto text-muted"
          >建议补货总量 <b class="font-medium text-ink">1,840</b> 件</span
        >
      </div>
      <table data-testid="replenishment-table" class="w-full table-fixed border-collapse">
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
            <th
              scope="col"
              class="h-36 border-b border-line-soft bg-page-soft px-8 text-left text-12 font-semibold whitespace-nowrap text-ink"
            >
              商品 / SKU
            </th>
            <th
              scope="col"
              class="h-36 border-b border-line-soft bg-page-soft px-8 text-left text-12 font-semibold whitespace-nowrap text-ink"
            >
              当前库存
            </th>
            <th
              scope="col"
              class="h-36 border-b border-line-soft bg-page-soft px-8 text-left text-12 font-semibold whitespace-nowrap text-ink"
            >
              日均销量
            </th>
            <th
              scope="col"
              class="h-36 border-b border-line-soft bg-page-soft px-8 text-left text-12 font-semibold whitespace-nowrap text-ink"
            >
              可售天数
            </th>
            <th
              scope="col"
              class="h-36 border-b border-line-soft bg-page-soft px-8 text-left text-12 font-semibold whitespace-nowrap text-ink"
            >
              建议补货
            </th>
            <th
              scope="col"
              class="h-36 border-b border-line-soft bg-page-soft px-8 text-left text-12 font-semibold whitespace-nowrap text-ink"
            >
              紧急程度
            </th>
          </tr>
        </thead>
        <tbody class="[&>tr:last-child>td]:border-b-0">
          <tr>
            <td
              class="h-47 overflow-hidden border-b border-line-soft px-8 text-12 text-body text-ellipsis whitespace-nowrap"
            >
              <div class="flex min-w-0 items-center gap-7">
                <span
                  class="flex size-26 flex-none items-center justify-center rounded-3 bg-page text-thumb-icon"
                  ><AppIcon name="i-box" :size="18" class="[stroke-width:1.3]"
                /></span>
                <div class="min-w-0">
                  <div class="overflow-hidden text-ellipsis whitespace-nowrap text-ink">
                    磁吸折叠露营灯
                  </div>
                  <div class="text-10/15 text-muted">LX-CL001</div>
                </div>
              </div>
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              186
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              16.2
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              11.5 天
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              260
            </td>
            <td class="h-47 border-b border-line-soft px-8 text-12 text-body">
              <span
                class="inline-flex h-20 items-center rounded-2 bg-danger-faint px-6 text-11/[16.5px] text-danger"
                >断货风险</span
              >
            </td>
          </tr>
          <tr>
            <td
              class="h-47 overflow-hidden border-b border-line-soft px-8 text-12 text-body text-ellipsis whitespace-nowrap"
            >
              <div class="flex min-w-0 items-center gap-7">
                <span
                  class="flex size-26 flex-none items-center justify-center rounded-3 bg-page text-thumb-icon"
                  ><AppIcon name="i-box" :size="18" class="[stroke-width:1.3]"
                /></span>
                <div class="min-w-0">
                  <div class="overflow-hidden text-ellipsis whitespace-nowrap text-ink">
                    宠物除毛梳
                  </div>
                  <div class="text-10/15 text-muted">LX-PT012</div>
                </div>
              </div>
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              342
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              18.6
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              18.4 天
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              320
            </td>
            <td class="h-47 border-b border-line-soft px-8 text-12 text-body">
              <span
                class="inline-flex h-20 items-center rounded-2 bg-notice-amber-bg px-6 text-11/[16.5px] text-warning"
                >加急</span
              >
            </td>
          </tr>
          <tr>
            <td
              class="h-47 overflow-hidden border-b border-line-soft px-8 text-12 text-body text-ellipsis whitespace-nowrap"
            >
              <div class="flex min-w-0 items-center gap-7">
                <span
                  class="flex size-26 flex-none items-center justify-center rounded-3 bg-page text-thumb-icon"
                  ><AppIcon name="i-box" :size="18" class="[stroke-width:1.3]"
                /></span>
                <div class="min-w-0">
                  <div class="overflow-hidden text-ellipsis whitespace-nowrap text-ink">
                    无线快充支架
                  </div>
                  <div class="text-10/15 text-muted">LX-CH028</div>
                </div>
              </div>
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              526
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              12.8
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              41.1 天
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              460
            </td>
            <td class="h-47 border-b border-line-soft px-8 text-12 text-body">
              <span
                class="inline-flex h-20 items-center rounded-2 bg-tag-green-bg px-6 text-11/[16.5px] text-success"
                >正常</span
              >
            </td>
          </tr>
          <tr>
            <td
              class="h-47 overflow-hidden border-b border-line-soft px-8 text-12 text-body text-ellipsis whitespace-nowrap"
            >
              <div class="flex min-w-0 items-center gap-7">
                <span
                  class="flex size-26 flex-none items-center justify-center rounded-3 bg-page text-thumb-icon"
                  ><AppIcon name="i-box" :size="18" class="[stroke-width:1.3]"
                /></span>
                <div class="min-w-0">
                  <div class="overflow-hidden text-ellipsis whitespace-nowrap text-ink">
                    户外防水收纳袋
                  </div>
                  <div class="text-10/15 text-muted">LX-BG031</div>
                </div>
              </div>
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              788
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              10.4
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              75.8 天
            </td>
            <td
              class="h-47 border-b border-line-soft px-8 text-right text-12 text-ink tabular-nums"
            >
              —
            </td>
            <td class="h-47 border-b border-line-soft px-8 text-12 text-body">
              <span
                class="inline-flex h-20 items-center rounded-2 bg-tag-green-bg px-6 text-11/[16.5px] text-success"
                >充足</span
              >
            </td>
          </tr>
        </tbody>
      </table>
      <div class="mt-8 flex items-center justify-between text-11/[16.5px] text-muted">
        <span>库存数据更新于 2026-11-12 15:45</span
        ><button
          type="button"
          class="inline-flex items-center gap-4 text-12 whitespace-nowrap text-primary hover:text-primary-hover"
        >
          查看全部建议<AppIcon name="i-chev-right" :size="12" />
        </button>
      </div>
    </div>
  </AppCard>
</template>
