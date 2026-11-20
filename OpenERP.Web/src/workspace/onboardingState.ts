import { ref } from 'vue'

/** 初始化引导带的收起状态仅当次会话有效，不写入持久化存储。 */
export const onboardingRibbonCollapsed = ref(false)

/** 完成摘要首次出现时保留一行，收起后本次会话不再默认展开。 */
export const onboardingSummaryDismissed = ref(false)
