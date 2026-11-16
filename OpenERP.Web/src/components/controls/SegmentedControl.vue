<script setup lang="ts">
const props = withDefaults(defineProps<{
  options: string[]
  modelValue: string
  groupClass?: string
  buttonClass?: string
  ariaLabel?: string
}>(), {
  groupClass: 'mini-seg',
  buttonClass: '',
  ariaLabel: '视图切换',
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()
</script>

<template>
  <div :class="props.groupClass" role="group" :aria-label="props.ariaLabel">
    <button
      v-for="option in options"
      :key="option"
      :class="[props.buttonClass, { active: option === modelValue }]"
      type="button"
      :aria-pressed="option === modelValue"
      @click="emit('update:modelValue', option)"
    >
      {{ option }}
    </button>
  </div>
</template>
