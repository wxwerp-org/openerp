<script setup lang="ts">
defineProps<{
  message: string
}>()
</script>

<template>
  <div
    v-show="message"
    role="status"
    class="fixed left-1/2 top-72 z-[9999] -translate-x-1/2 whitespace-nowrap rounded-4 bg-toast-bg px-14 py-8 text-12/18 text-white shadow-toast"
  >
    {{ message }}
  </div>
</template>
