<script setup lang="ts">
import { computed, useSlots } from 'vue'

defineOptions({ inheritAttrs: false })

const props = withDefaults(
  defineProps<{
    modelValue?: boolean
    label?: string
    disabled?: boolean
  }>(),
  {
    disabled: false,
    label: undefined,
    modelValue: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()

const slots = useSlots()
const hasLabel = computed(() => Boolean(props.label) || Boolean(slots.default))

function onChange(event: Event): void {
  emit('update:modelValue', (event.target as HTMLInputElement).checked)
}
</script>

<template>
  <label
    v-if="hasLabel"
    class="inline-flex cursor-pointer items-center gap-8 text-12/22 whitespace-nowrap text-body-alt"
  >
    <input
      v-bind="$attrs"
      class="app-checkbox relative m-0 h-14 w-14 shrink-0 cursor-pointer appearance-none rounded-2 border border-check-border bg-white align-middle hover:border-primary checked:border-primary checked:bg-primary disabled:cursor-default"
      type="checkbox"
      :checked="modelValue"
      :disabled="disabled"
      @change="onChange"
    />
    <span
      ><slot>{{ label }}</slot></span
    >
  </label>
  <input
    v-else
    v-bind="$attrs"
    class="app-checkbox relative m-0 h-14 w-14 shrink-0 cursor-pointer appearance-none rounded-2 border border-check-border bg-white align-middle hover:border-primary checked:border-primary checked:bg-primary disabled:cursor-default"
    type="checkbox"
    :checked="modelValue"
    :disabled="disabled"
    @change="onChange"
  />
</template>

<style scoped>
.app-checkbox:checked::after {
  content: '';
  position: absolute;
  left: 4px;
  top: 1px;
  width: 4px;
  height: 8px;
  border: 1.5px solid var(--color-white);
  border-top: 0;
  border-left: 0;
  transform: rotate(42deg);
}
</style>
