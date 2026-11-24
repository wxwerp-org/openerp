import type { OrderSyncStage, OrderSyncStatus, OrderSyncTask } from '../composables/orderSyncTask'

/** 订单同步任务在面板里的文案、标签色与进度计算。 */

export type TagClass = 'green' | 'red' | 'gray' | 'amber'

export function orderSyncStatusLabel(task: OrderSyncTask): string {
  switch (task.status) {
    case 'running':
      return task.stage === 'queued' ? '等待同步' : '正在同步'
    case 'succeeded':
      return '同步完成'
    case 'partial':
      return '部分成功'
    case 'failed':
      return '同步失败'
  }
}

export function orderSyncStatusClass(status: OrderSyncStatus): TagClass {
  if (status === 'succeeded') return 'green'
  if (status === 'partial') return 'amber'
  if (status === 'failed') return 'red'
  return 'gray'
}

export function orderSyncStageLabel(stage: OrderSyncStage): string {
  switch (stage) {
    case 'queued':
      return ''
    case 'shop':
      return '店铺信息'
    case 'pages':
      return '订单列表'
    case 'details':
      return '商品详情'
    case 'saving':
      return '保存结果'
    case 'done':
      return '已完成'
  }
}

export function orderSyncProgress(task: OrderSyncTask): number {
  if (task.status === 'succeeded' || task.status === 'partial') return 100
  if (task.stage === 'saving' || task.stage === 'done') return 99
  if (task.total <= 0) return task.stage === 'details' ? 50 : 0

  const progress =
    task.stage === 'pages'
      ? Math.min(1, task.read / task.total) * 50
      : task.stage === 'details'
        ? 50 + Math.min(1, task.processed / task.total) * 49
        : 0

  return Math.min(99, Math.round(progress))
}

export function orderSyncProgressTone(
  task: OrderSyncTask,
): 'primary' | 'success' | 'warning' | 'danger' {
  if (task.status === 'succeeded') return 'success'
  if (task.status === 'partial') return 'warning'
  if (task.status === 'failed') return 'danger'
  return 'primary'
}
