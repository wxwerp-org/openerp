/**
 * HTTP 错误处理模块 - 简化版
 *
 * @module utils/http/error
 */

/**
 * 显示错误消息
 * @param message 错误消息字符串
 */
export function showError(message: string): void {
  ElMessage.error(message)
  // 记录错误日志
  console.error('[HTTP Error]', message)
}

/**
 * 显示成功消息
 * @param message 成功消息
 */
export function showSuccess(message: string): void {
  ElMessage.success(message)
}

/**
 * 判断是否为 Error 类型（简化版，替代 isHttpError）
 * @param error 错误对象
 * @returns 是否为 Error 类型
 */
export const isHttpError = (error: unknown): error is Error => {
  return error instanceof Error
}
