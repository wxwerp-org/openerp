<script setup lang="ts">
import { computed, useSlots } from 'vue'

defineOptions({ inheritAttrs: false })

type RadioValue = string | number | boolean

const props = withDefaults(
  defineProps<{
    modelValue?: RadioValue
    value: RadioValue
    label?: string
    name?: string
    disabled?: boolean
  }>(),
  {
    disabled: false,
    modelValue: undefined,
    label: undefined,
    name: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: RadioValue]
}>()

const slots = useSlots()
const hasLabel = computed(() => Boolean(props.label) || Boolean(slots.default))
const checked = computed(() =>
  props.modelValue === undefined ? undefined : props.modelValue === props.value,
)

function onChange(): void {
  emit('update:modelValue', props.value)
}
</script>

<template>
  <label
    v-if="hasLabel"
    class="inline-flex items-center gap-8 text-12/22 whitespace-nowrap text-body-alt"
  >
    <input
      v-bind="$attrs"
      class="app-radio relative m-0 h-16 w-16 shrink-0 cursor-pointer appearance-none rounded-full border border-radio-border bg-white checked:border-radio-check disabled:cursor-default disabled:border-radio-border-disabled disabled:bg-radio-bg-disabled disabled:checked:border-radio-check-disabled"
      type="radio"
      :name="name"
      :checked="checked"
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
    class="app-radio relative m-0 h-16 w-16 shrink-0 cursor-pointer appearance-none rounded-full border border-radio-border bg-white checked:border-radio-check disabled:cursor-default disabled:border-radio-border-disabled disabled:bg-radio-bg-disabled disabled:checked:border-radio-check-disabled"
    type="radio"
    :name="name"
    :checked="checked"
    :disabled="disabled"
    @change="onChange"
  />
</template>

<style scoped>
.app-radio:checked::after {
  content: '';
  position: absolute;
  left: 3px;
  top: 3px;
  width: 8px;
  height: 8px;
  border-radius: 9999px;
  background: var(--color-radio-check);
}

.app-radio:disabled:checked::after {
  background: var(--color-radio-check-disabled);
}
</style>
