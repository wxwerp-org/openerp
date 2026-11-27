import { describe, expect, it } from 'vitest'
import {
  orderSyncProgress,
  orderSyncProgressTone,
  orderSyncStageLabel,
  orderSyncStatusClass,
  orderSyncStatusLabel
} from '@/hooks/business/orderSyncPresentation'
import type { OrderSyncTask } from '@/hooks/business/orderSyncTask'

function task(patch: Partial<OrderSyncTask>): OrderSyncTask {
  return {
    status: 'running',
    stage: 'queued',
    total: 0,
    read: 0,
    processed: 0,
    ...patch
  } as OrderSyncTask
}

describe('订单同步任务展示', () => {
  it('Given 排队与运行中的任务 When 取状态文案 Then 区分等待与正在同步', () => {
    expect(orderSyncStatusLabel(task({ stage: 'queued' }))).toBe('等待同步')
    expect(orderSyncStatusLabel(task({ stage: 'pages' }))).toBe('正在同步')
  })

  it('Given 已结束的任务 When 取状态文案与标签色 Then 成功绿、部分成功琥珀、失败红', () => {
    expect(orderSyncStatusLabel(task({ status: 'succeeded' }))).toBe('同步完成')
    expect(orderSyncStatusLabel(task({ status: 'partial' }))).toBe('部分成功')
    expect(orderSyncStatusLabel(task({ status: 'failed' }))).toBe('同步失败')
    expect(orderSyncStatusClass('succeeded')).toBe('green')
    expect(orderSyncStatusClass('partial')).toBe('amber')
    expect(orderSyncStatusClass('failed')).toBe('red')
    expect(orderSyncStatusClass('running')).toBe('gray')
  })

  it('Given 同步阶段 When 取阶段文案 Then 排队阶段不显示文字', () => {
    expect(orderSyncStageLabel('queued')).toBe('')
    expect(orderSyncStageLabel('pages')).toBe('订单列表')
    expect(orderSyncStageLabel('details')).toBe('商品详情')
    expect(orderSyncStageLabel('saving')).toBe('保存结果')
    expect(orderSyncStageLabel('done')).toBe('已完成')
  })

  it('Given 列表读取进行中 When 计算进度 Then 读取阶段占前 50% 且不会提前到 100%', () => {
    expect(orderSyncProgress(task({ stage: 'pages', total: 10, read: 5 }))).toBe(25)
    expect(orderSyncProgress(task({ stage: 'pages', total: 10, read: 99 }))).toBe(50)
  })

  it('Given 详情阶段 When 计算进度 Then 从 50% 起算且保存前封顶 99%', () => {
    expect(orderSyncProgress(task({ stage: 'details', total: 10, processed: 5 }))).toBe(75)
    expect(orderSyncProgress(task({ stage: 'details', total: 10, processed: 10 }))).toBe(99)
    expect(orderSyncProgress(task({ stage: 'saving', total: 10 }))).toBe(99)
  })

  it('Given 总数未知 When 计算进度 Then 详情阶段为 50% 其余为 0', () => {
    expect(orderSyncProgress(task({ stage: 'details', total: 0 }))).toBe(50)
    expect(orderSyncProgress(task({ stage: 'pages', total: 0 }))).toBe(0)
  })

  it('Given 任务结束 When 计算进度与进度条色调 Then 完成为 100% 并按结果着色', () => {
    expect(orderSyncProgress(task({ status: 'succeeded' }))).toBe(100)
    expect(orderSyncProgress(task({ status: 'partial' }))).toBe(100)
    expect(orderSyncProgressTone(task({ status: 'succeeded' }))).toBe('success')
    expect(orderSyncProgressTone(task({ status: 'partial' }))).toBe('warning')
    expect(orderSyncProgressTone(task({ status: 'failed' }))).toBe('danger')
    expect(orderSyncProgressTone(task({ status: 'running' }))).toBe('primary')
  })
})
