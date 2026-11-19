<script setup lang="ts">
import { computed, useSlots } from 'vue'

defineOptions({ inheritAttrs: false })

type CheckboxValue = string | number

const props = withDefaults(
  defineProps<{
    /** 布尔用法；传入数组时按 `value` 组合成多选组（与原生 `v-model` 数组用法一致）。 */
    modelValue?: boolean | CheckboxValue[]
    /** 多选组里代表本选项的值。 */
    value?: CheckboxValue
    label?: string
    disabled?: boolean
  }>(),
  {
    disabled: false,
    label: undefined,
    value: undefined,
    modelValue: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: boolean | CheckboxValue[]]
}>()

const slots = useSlots()
const hasLabel = computed(() => Boolean(props.label) || Boolean(slots.default))
const isGroup = computed(() => Array.isArray(props.modelValue))
const checked = computed(() =>
  isGroup.value
    ? (props.modelValue as CheckboxValue[]).includes(props.value as CheckboxValue)
    : (props.modelValue as boolean | undefined),
)

function onChange(event: Event): void {
  const next = (event.target as HTMLInputElement).checked
  if (!isGroup.value) {
    emit('update:modelValue', next)
    return
  }

  const current = [...(props.modelValue as CheckboxValue[])]
  const index = current.indexOf(props.value as CheckboxValue)
  if (next && index < 0) current.push(props.value as CheckboxValue)
  if (!next && index >= 0) current.splice(index, 1)
  emit('update:modelValue', current)
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
      :checked="checked"
      :value="value"
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
    :checked="checked"
    :value="value"
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
