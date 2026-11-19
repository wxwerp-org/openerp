<script setup lang="ts">
withDefaults(
  defineProps<{
    label: string
    htmlFor: string
    required?: boolean
    error?: string
    errorId?: string
    full?: boolean
  }>(),
  {
    required: false,
    full: false,
    error: undefined,
    errorId: undefined,
  },
)
</script>

<template>
  <div class="relative min-w-0" :class="{ 'col-span-full': full }">
    <div class="flex min-w-0 items-start">
      <label
        :for="htmlFor"
        class="app-field-label relative w-90 flex-none pr-12 text-right text-12/32 text-body whitespace-nowrap"
      >
        <span v-if="required" class="mr-1 text-danger">*</span>{{ label }}
      </label>
      <div class="relative min-w-0 flex-1">
        <slot />
      </div>
    </div>
    <p
      v-if="error"
      :id="errorId"
      role="alert"
      class="absolute left-0 top-full pt-3 text-12/16 text-danger whitespace-nowrap"
    >
      {{ error }}
    </p>
  </div>
</template>

<style scoped>
.app-field-label::after {
  content: '：';
  position: absolute;
  right: 0;
  top: 0;
  width: 12px;
  text-align: left;
}
</style>
