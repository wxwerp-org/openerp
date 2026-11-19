<script setup lang="ts">
import { computed, useAttrs } from 'vue'

defineOptions({ inheritAttrs: false })

const props = withDefaults(
  defineProps<{
    modelValue?: string
    type?: string
    size?: 'md' | 'sm'
    invalid?: boolean
    readonly?: boolean
    disabled?: boolean
  }>(),
  {
    type: 'text',
    size: 'md',
    invalid: false,
    readonly: false,
    disabled: false,
    modelValue: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
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
  props.size === 'md'
    ? 'flex h-[var(--control-h-lg)] w-full items-center rounded-4 border'
    : 'inline-flex h-[var(--control-h)] shrink-0 items-center rounded-4 border',
  props.invalid
    ? 'border-danger focus-within:border-danger'
    : 'border-input hover:border-input-hover focus-within:border-primary',
  props.readonly ? 'bg-tag-gray-bg' : 'bg-white',
])

const inputClass = computed(() => [
  props.size === 'md'
    ? 'h-[calc(var(--control-h-lg)-2px)] w-full min-w-0 rounded-4 border-0 bg-transparent px-11 text-12/30 outline-none'
    : 'h-[calc(var(--control-h)-2px)] min-w-0 border-0 bg-transparent px-10 text-12/28 outline-none',
  props.readonly ? 'cursor-default text-muted' : 'text-ink',
  'placeholder:text-placeholder',
])

let composing = false

function onCompositionStart(): void {
  composing = true
}

function onCompositionEnd(event: CompositionEvent): void {
  composing = false
  emit('update:modelValue', (event.target as HTMLInputElement).value)
}

function onInput(event: Event): void {
  if (composing) return
  emit('update:modelValue', (event.target as HTMLInputElement).value)
}
</script>

<template>
  <div :data-size="size" :class="[shellClass, rootAttrs.class]" :style="rootAttrs.style">
    <input
      v-bind="controlAttrs"
      :type="type"
      :value="modelValue ?? ''"
      :readonly="readonly"
      :disabled="disabled"
      :aria-invalid="invalid ? 'true' : undefined"
      :class="inputClass"
      @compositionstart="onCompositionStart"
      @compositionend="onCompositionEnd"
      @input="onInput"
    />
    <slot name="suffix" />
  </div>
</template>
