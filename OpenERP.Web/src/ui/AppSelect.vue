<script setup lang="ts">
import { computed, useAttrs } from 'vue'
import AppIcon from './AppIcon.vue'

defineOptions({ inheritAttrs: false })

type SelectValue = string | number

const props = withDefaults(
  defineProps<{
    modelValue?: SelectValue
    options: { value: SelectValue; label: string }[]
    size?: 'md' | 'sm'
    disabled?: boolean
    /** 键盘聚焦时显示 2px 焦点环；报表等需要完整键盘遍历的页面开启。 */
    focusRing?: boolean
  }>(),
  {
    size: 'md',
    disabled: false,
    focusRing: false,
    modelValue: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: SelectValue]
}>()

const attrs = useAttrs()
const rootAttrs = computed(() => ({ class: attrs.class, style: attrs.style }))
const controlAttrs = computed(() => {
  const rest: Record<string, unknown> = { ...attrs }
  delete rest.class
  delete rest.style
  return rest
})

const shellClass = computed(() => [
  'relative inline-flex items-center rounded-4 border border-input bg-white hover:border-input-hover focus-within:border-primary',
  props.size === 'md' ? 'h-[var(--control-h-lg)] w-full' : 'h-[var(--control-h)] shrink-0',
])

const selectClass = computed(() => [
  'h-full w-full appearance-none border-0 bg-transparent text-12 text-ink outline-none disabled:cursor-default disabled:text-muted',
  props.size === 'md' ? 'pl-11 pr-29' : 'pl-10 pr-29',
  props.focusRing
    ? 'focus-visible:outline-solid focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary'
    : '',
])

function onChange(event: Event): void {
  const select = event.target as HTMLSelectElement
  const option = props.options[select.selectedIndex]
  emit('update:modelValue', option ? option.value : select.value)
}
</script>

<template>
  <span :data-size="size" :class="[shellClass, rootAttrs.class]" :style="rootAttrs.style">
    <select
      v-bind="controlAttrs"
      :value="modelValue === undefined ? '' : String(modelValue)"
      :disabled="disabled"
      :class="selectClass"
      @change="onChange"
    >
      <option v-for="option in options" :key="String(option.value)" :value="String(option.value)">
        {{ option.label }}
      </option>
    </select>
    <AppIcon
      name="i-chev-down"
      :size="10"
      class="pointer-events-none absolute right-10 text-placeholder"
    />
  </span>
</template>
