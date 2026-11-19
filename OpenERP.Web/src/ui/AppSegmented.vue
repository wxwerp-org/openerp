<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    options: string[]
    modelValue: string
    /** 容器与按钮高度档：sm = 28/26，md = 30/28。 */
    size?: 'sm' | 'md'
    /**
     * 迁移期像素级几何覆盖：lineHeight/color/paddingX/minWidth 用于复刻旧页面的上下文覆盖，
     * 随对应页面（3b 产品、3d 工作台）迁移后收敛为更少的语义档位。
     */
    lineHeight?: 26 | 28
    /** 按钮文字颜色档；个别上下文（如排行榜维度）沿用常规文字色。 */
    color?: 'body' | 'ink'
    /** 按钮左右内边距（px）；按上下文密度显式指定，默认随 size。 */
    paddingX?: 6 | 7 | 8 | 10 | 12
    /** 按钮最小宽度（px）；0 表示不设最小宽度，默认随 size。 */
    minWidth?: 0 | 32 | 36 | 38 | 44 | 52
    /** 额外的按钮工具类（静态字面量，例如窄屏密度覆盖）。 */
    optionClass?: string
  }>(),
  {
    size: 'sm',
    optionClass: '',
    lineHeight: undefined,
    color: undefined,
    paddingX: undefined,
    minWidth: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const groupHeightClass: Record<'sm' | 'md', string> = {
  sm: 'h-[var(--control-h-sm)]',
  md: 'h-[var(--control-h)]',
}

const optionHeightClass: Record<'sm' | 'md', string> = {
  sm: 'h-[calc(var(--control-h-sm)-2px)]',
  md: 'h-[calc(var(--control-h)-2px)]',
}

const lineHeightClass: Record<26 | 28, string> = {
  26: 'text-12/26',
  28: 'text-12/28',
}

const colorClass: Record<'body' | 'ink', string> = {
  body: 'text-body',
  ink: 'text-ink',
}

const paddingXClass: Record<6 | 7 | 8 | 10 | 12, string> = {
  6: 'px-6',
  7: 'px-7',
  8: 'px-8',
  10: 'px-10',
  12: 'px-12',
}

const minWidthClass: Record<0 | 32 | 36 | 38 | 44 | 52, string> = {
  0: '',
  32: 'min-w-32',
  36: 'min-w-36',
  38: 'min-w-38',
  44: 'min-w-44',
  52: 'min-w-52',
}

const resolvedLineHeight = computed(() => props.lineHeight ?? (props.size === 'md' ? 28 : 26))
const resolvedColor = computed(() => props.color ?? (props.size === 'md' ? 'ink' : 'body'))
const resolvedPaddingX = computed(() => props.paddingX ?? (props.size === 'md' ? 12 : 8))
const resolvedMinWidth = computed(() => props.minWidth ?? (props.size === 'md' ? 0 : 38))

const groupClass = computed(() => [
  'inline-flex shrink-0 items-center overflow-hidden rounded-4 border border-input bg-white',
  groupHeightClass[props.size],
])

function optionClasses(index: number, option: string): string[] {
  const active = option === props.modelValue
  const previousActive = index > 0 && props.options[index - 1] === props.modelValue
  return [
    'inline-flex items-center justify-center whitespace-nowrap bg-white hover:text-primary',
    optionHeightClass[props.size],
    lineHeightClass[resolvedLineHeight.value],
    active ? 'text-primary' : colorClass[resolvedColor.value],
    paddingXClass[resolvedPaddingX.value],
    minWidthClass[resolvedMinWidth.value],
    index > 0 ? 'border-y-0 border-r-0 border-l border-solid' : 'border-0',
    index > 0 ? (previousActive ? 'border-l-transparent' : 'border-l-line') : '',
    active ? 'relative z-[1] rounded-3 [box-shadow:inset_0_0_0_1px_var(--color-primary)]' : '',
    props.optionClass,
  ]
}
</script>

<template>
  <div :class="groupClass" :data-size="size" role="group">
    <button
      v-for="(option, index) in options"
      :key="option"
      type="button"
      :class="optionClasses(index, option)"
      :aria-pressed="option === modelValue"
      @click="emit('update:modelValue', option)"
    >
      {{ option }}
    </button>
  </div>
</template>
