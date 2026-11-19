<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    modelValue?: string
    type?: string
    size?: 'md' | 'sm'
    invalid?: boolean
    readonly?: boolean
    disabled?: boolean
    placeholder?: string
    id?: string
    label?: string
    describedBy?: string
    name?: string
    autocomplete?: string
    maxlength?: number
    required?: boolean
  }>(),
  {
    type: 'text',
    size: 'md',
    invalid: false,
    readonly: false,
    disabled: false,
    required: false,
    modelValue: undefined,
    placeholder: undefined,
    id: undefined,
    label: undefined,
    describedBy: undefined,
    name: undefined,
    autocomplete: undefined,
    maxlength: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const shellClass = computed(() => [
  props.size === 'md'
    ? 'flex h-[var(--control-h-lg)] w-full items-center rounded-4 border bg-white'
    : 'inline-flex h-[var(--control-h)] shrink-0 items-center rounded-4 border bg-white',
  props.invalid
    ? 'border-danger focus-within:border-danger'
    : 'border-input hover:border-input-hover focus-within:border-primary',
  props.readonly ? 'bg-tag-gray-bg' : '',
])

const inputClass = computed(() => [
  props.size === 'md'
    ? 'h-[calc(var(--control-h-lg)-2px)] w-full min-w-0 rounded-4 border-0 bg-transparent px-11 text-12/30 outline-none'
    : 'h-[calc(var(--control-h)-2px)] min-w-0 border-0 bg-transparent px-10 text-12/28 outline-none',
  props.readonly ? 'cursor-default text-muted' : 'text-ink',
  'placeholder:text-placeholder',
])

function onInput(event: Event): void {
  emit('update:modelValue', (event.target as HTMLInputElement).value)
}
</script>

<template>
  <div :data-size="size" :class="shellClass">
    <input
      :id="id"
      :type="type"
      :value="modelValue ?? ''"
      :placeholder="placeholder"
      :readonly="readonly"
      :disabled="disabled"
      :required="required"
      :name="name"
      :autocomplete="autocomplete"
      :maxlength="maxlength"
      :aria-label="label"
      :aria-describedby="describedBy"
      :aria-invalid="invalid ? 'true' : undefined"
      :class="inputClass"
      @input="onInput"
    />
    <slot name="suffix" />
  </div>
</template>
