<script setup lang="ts">
import { computed } from 'vue'
import AppIcon from './AppIcon.vue'

type SelectValue = string | number

const props = withDefaults(
  defineProps<{
    modelValue?: SelectValue
    options: { value: SelectValue; label: string }[]
    size?: 'md' | 'sm' | 'control'
    disabled?: boolean
    id?: string
    label?: string
    name?: string
  }>(),
  {
    size: 'md',
    disabled: false,
    modelValue: undefined,
    id: undefined,
    label: undefined,
    name: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: SelectValue]
}>()

const shellClass = computed(() => [
  'relative inline-flex items-center rounded-4 border border-input bg-white hover:border-input-hover focus-within:border-primary',
  props.size === 'md' ? 'h-[var(--control-h-lg)] w-full' : 'h-[var(--control-h)] shrink-0',
])

const selectClass = computed(() => [
  'h-full w-full appearance-none border-0 bg-transparent text-12 text-ink outline-none disabled:cursor-default disabled:text-muted',
  props.size === 'md' ? 'pl-11 pr-29' : 'pl-10 pr-29',
])

function onChange(event: Event): void {
  const raw = (event.target as HTMLSelectElement).value
  const matched = props.options.find((option) => String(option.value) === raw)
  emit('update:modelValue', matched ? matched.value : raw)
}
</script>

<template>
  <span :data-size="size" :class="shellClass">
    <select
      :id="id"
      :name="name"
      :value="modelValue === undefined ? '' : String(modelValue)"
      :disabled="disabled"
      :aria-label="label"
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
