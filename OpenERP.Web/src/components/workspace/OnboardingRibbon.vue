<script setup lang="ts">
import AppCard from '../../ui/AppCard.vue'
import { onboardingRibbonCollapsed } from '../../workspace/onboardingState'
import AppIcon from '../../ui/AppIcon.vue'
import type { OnboardingStep } from '../../workspace/workspaceDerivation'

defineProps<{
  steps: OnboardingStep[]
  nextLabel: string
  doneCount: number
}>()

const emit = defineEmits<{ action: [stepId: string] }>()

const collapsed = onboardingRibbonCollapsed

function select(step: OnboardingStep): void {
  if (step.done || !step.executable) return
  emit('action', step.id)
}

function stepClass(step: OnboardingStep): string[] {
  if (step.done) return ['bg-page-soft text-muted']
  if (step.executable) return ['bg-nav-active hover:bg-nav-active']
  return ['bg-page text-muted']
}
</script>

<template>
  <AppCard class="col-span-12" data-testid="workspace-onboarding">
    <div v-if="collapsed" class="flex h-44 items-center gap-8 px-16">
      <span class="text-12 font-semibold text-ink">初始化引导</span>
      <span class="text-12 text-body" data-testid="onboarding-progress">
        初始化进度 {{ doneCount }}/{{ steps.length }} · 下一步：{{ nextLabel }}
      </span>
      <button
        type="button"
        class="ml-auto flex items-center gap-4 text-12 text-body hover:text-primary"
        data-testid="onboarding-toggle"
        @click="collapsed = false"
      >
        展开<AppIcon name="i-chev-down" :size="12" />
      </button>
    </div>
    <div v-else class="flex flex-col lg:h-141">
      <div class="flex flex-none items-center gap-8 px-16 pt-16">
        <span class="text-14/22 font-bold text-ink">初始化引导</span>
        <span class="text-11 text-muted" data-testid="onboarding-progress">
          {{ doneCount }}/{{ steps.length }}
        </span>
        <button
          type="button"
          class="ml-auto flex h-28 items-center gap-4 text-12 text-body hover:text-primary"
          data-testid="onboarding-toggle"
          @click="collapsed = true"
        >
          收起<AppIcon name="i-chev-down" :size="12" class="rotate-180" />
        </button>
      </div>
      <div class="mx-16 mt-9 mb-16 flex min-w-0 flex-col gap-4 lg:h-78 lg:flex-row">
        <button
          v-for="step in steps"
          :key="step.id"
          type="button"
          class="guide-step flex min-h-78 min-w-0 flex-1 items-center pr-20 pl-16 text-left transition-colors lg:first:pl-16"
          :class="stepClass(step)"
          :data-step="step.id"
          :data-step-done="step.done ? 'true' : 'false'"
          :data-step-executable="step.executable ? 'true' : 'false'"
          :disabled="step.done || !step.executable"
          @click="select(step)"
        >
          <svg aria-hidden="true" class="mr-10 size-36 flex-none text-primary">
            <use :href="`#${step.icon}`" />
          </svg>
          <span class="min-w-0">
            <span
              class="flex flex-wrap items-center gap-4 text-14/20 font-bold text-ink lg:whitespace-nowrap"
            >
              {{ step.title }}
              <svg
                v-if="step.done"
                aria-hidden="true"
                class="size-14 flex-none text-success"
                viewBox="0 0 16 16"
              >
                <path
                  d="m3 8.5 3.2 3.2L13 5"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="1.8"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                />
              </svg>
              <span
                v-else-if="step.executable"
                class="h-16 flex-none rounded-2 bg-danger-faint px-4 text-11/16 font-normal text-init-must-text"
                >必做</span
              >
            </span>
            <span class="mt-2 line-clamp-2 max-w-full text-12/18 text-body lg:max-w-210">
              {{ step.description }}
            </span>
          </span>
        </button>
      </div>
    </div>
  </AppCard>
</template>

<style scoped>
/* 切角箭头形状无法用单个工具类表达，与原型引导带保持同一几何规则；手机宽度改为纵向堆叠。 */
@media (min-width: 1024px) {
  .guide-step {
    clip-path: polygon(
      0 0,
      calc(100% - var(--guide-notch)) 0,
      100% 50%,
      calc(100% - var(--guide-notch)) 100%,
      0 100%,
      var(--guide-notch) 50%
    );
  }

  .guide-step:first-child {
    clip-path: polygon(
      0 0,
      calc(100% - var(--guide-notch)) 0,
      100% 50%,
      calc(100% - var(--guide-notch)) 100%,
      0 100%
    );
  }
}
</style>
