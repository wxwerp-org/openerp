<script setup lang="ts">
import AppCard from '../../../ui/AppCard.vue'
import AppIcon from '../../../ui/AppIcon.vue'
</script>

<template>
  <AppCard data-testid="dashboard-card-logistics" class="col-span-6 flex h-400 flex-col">
    <div class="flex h-54 flex-none items-start justify-between px-16 pt-16">
      <div class="flex items-center gap-4 text-14/22 font-bold whitespace-nowrap text-ink">
        物流看板<AppIcon
          name="i-chev-right"
          :size="12"
          class="text-title-caret [stroke-width:1.8]"
        />
      </div>
      <button
        type="button"
        class="inline-flex items-center gap-4 text-12 whitespace-nowrap text-primary hover:text-primary-hover"
      >
        物流追踪<AppIcon name="i-chev-right" :size="12" />
      </button>
    </div>
    <div class="flex min-h-0 flex-1 flex-col px-16 pb-16">
      <div class="grid flex-none grid-cols-4 gap-8">
        <div
          class="flex h-92 flex-col justify-between rounded-4 border border-hover-pill bg-white px-14 py-12 shadow-stat"
        >
          <div class="flex items-center gap-4 text-12 text-body">运输途中</div>
          <div class="flex items-baseline gap-4 text-20/26 font-medium text-ink tabular-nums">
            1,280<small class="text-11/26 font-normal text-muted">PCS</small>
          </div>
          <div class="text-11/[16.5px] text-muted">
            较昨日 <span class="text-success">+86</span>
          </div>
        </div>
        <div class="flex h-92 flex-col justify-between rounded-4 bg-page-soft px-14 py-12">
          <div class="flex items-center gap-4 text-12 text-body">延误</div>
          <div class="flex items-baseline gap-4 text-20/26 font-medium text-ink tabular-nums">
            46<small class="text-11/26 font-normal text-muted">PCS</small>
          </div>
          <div class="text-11/[16.5px] text-muted">较昨日 <span class="text-danger">+5</span></div>
        </div>
        <div class="flex h-92 flex-col justify-between rounded-4 bg-page-soft px-14 py-12">
          <div class="flex items-center gap-4 text-12 text-body">今日已发货</div>
          <div class="flex items-baseline gap-4 text-20/26 font-medium text-ink tabular-nums">
            320<small class="text-11/26 font-normal text-muted">PCS</small>
          </div>
          <div class="text-11/[16.5px] text-muted">目标 400 PCS</div>
        </div>
        <div class="flex h-92 flex-col justify-between rounded-4 bg-page-soft px-14 py-12">
          <div class="flex items-center gap-4 text-12 text-body">今日已签收</div>
          <div class="flex items-baseline gap-4 text-20/26 font-medium text-ink tabular-nums">
            210<small class="text-11/26 font-normal text-muted">PCS</small>
          </div>
          <div class="text-11/[16.5px] text-muted">目标 260 PCS</div>
        </div>
      </div>
      <div class="mt-10 min-h-0 flex-1 overflow-hidden rounded-4 border border-line-soft">
        <table data-testid="logistics-table" class="w-full table-fixed border-collapse">
          <thead>
            <tr>
              <th
                scope="col"
                class="h-32 border-b border-line-soft bg-row-soft px-10 text-left text-12 font-normal whitespace-nowrap text-muted"
              >
                物流单号
              </th>
              <th
                scope="col"
                class="h-32 border-b border-line-soft bg-row-soft px-10 text-left text-12 font-normal whitespace-nowrap text-muted"
              >
                渠道
              </th>
              <th
                scope="col"
                class="h-32 border-b border-line-soft bg-row-soft px-10 text-left text-12 font-normal whitespace-nowrap text-muted"
              >
                目的仓
              </th>
              <th
                scope="col"
                class="h-32 border-b border-line-soft bg-row-soft px-10 text-left text-12 font-normal whitespace-nowrap text-muted"
              >
                预计到达
              </th>
              <th
                scope="col"
                class="h-32 border-b border-line-soft bg-row-soft px-10 text-left text-12 font-normal whitespace-nowrap text-muted"
              >
                状态
              </th>
            </tr>
          </thead>
          <tbody class="[&>tr:last-child>td]:border-b-0">
            <tr>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-ellipsis whitespace-nowrap text-primary"
              >
                LX20261108001
              </td>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-body text-ellipsis whitespace-nowrap"
              >
                海派专线
              </td>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-body text-ellipsis whitespace-nowrap"
              >
                FBA US-LAX
              </td>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-body text-ellipsis whitespace-nowrap"
              >
                11-18
              </td>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-body text-ellipsis whitespace-nowrap"
              >
                <span class="inline-flex items-center gap-5"
                  ><i class="size-5 rounded-full bg-primary"></i>运输中</span
                >
              </td>
            </tr>
            <tr>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-ellipsis whitespace-nowrap text-primary"
              >
                LX20261106006
              </td>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-body text-ellipsis whitespace-nowrap"
              >
                空派快线
              </td>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-body text-ellipsis whitespace-nowrap"
              >
                FBA DE-BER
              </td>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-body text-ellipsis whitespace-nowrap"
              >
                11-15
              </td>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-body text-ellipsis whitespace-nowrap"
              >
                <span class="inline-flex items-center gap-5"
                  ><i class="size-5 rounded-full bg-danger"></i>延误</span
                >
              </td>
            </tr>
            <tr>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-ellipsis whitespace-nowrap text-primary"
              >
                LX20261109018
              </td>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-body text-ellipsis whitespace-nowrap"
              >
                卡派
              </td>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-body text-ellipsis whitespace-nowrap"
              >
                海外仓 US-ONT
              </td>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-body text-ellipsis whitespace-nowrap"
              >
                11-21
              </td>
              <td
                class="h-36 overflow-hidden border-b border-line-soft px-10 text-12 text-body text-ellipsis whitespace-nowrap"
              >
                <span class="inline-flex items-center gap-5"
                  ><i class="size-5 rounded-full bg-success"></i>已签收</span
                >
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </AppCard>
</template>
