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
      class="app-radio m-0 shrink-0 cursor-pointer"
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
    class="app-radio m-0 shrink-0 cursor-pointer"
    type="radio"
    :name="name"
    :checked="checked"
    :disabled="disabled"
    @change="onChange"
  />
</template>

<style scoped>
.app-radio {
  appearance: none;
  width: 16px;
  height: 16px;
  border: 1px solid var(--color-radio-border);
  border-radius: 50%;
  background: var(--color-white);
  position: relative;
  flex: none;
}

.app-radio:checked {
  border-color: var(--color-radio-check);
}

.app-radio:checked::after {
  content: '';
  position: absolute;
  left: 3px;
  top: 3px;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--color-radio-check);
}

.app-radio:disabled {
  cursor: default;
  border-color: var(--color-radio-border-disabled);
  background: var(--color-tag-gray-bg);
}

.app-radio:disabled:checked {
  border-color: var(--color-radio-check-disabled);
}

.app-radio:disabled:checked::after {
  background: var(--color-radio-check-disabled);
}
</style>
