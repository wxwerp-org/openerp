<template>
  <div class="flex h-54 flex-none items-start justify-between gap-12 px-16 pt-16">
    <div class="flex min-w-0 items-center gap-8">
      <slot />
    </div>
    <div v-if="$slots.right" class="flex min-w-0 flex-nowrap items-center gap-10">
      <slot name="right" />
    </div>
  </div>
</template>
