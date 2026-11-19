<script setup lang="ts">
import type { ComponentPublicInstance } from 'vue'
import { useNativeDialog } from '../composables/useNativeDialog'

const props = withDefaults(
  defineProps<{
    open: boolean
    id: string
    layer?: 'center' | 'form' | 'notice' | 'profile'
    ariaLabelledby?: string
    ariaLabel?: string
    closable?: boolean
    backdrop?: 'none' | 'dim' | 'strong'
  }>(),
  {
    layer: 'center',
    closable: true,
    backdrop: 'none',
    ariaLabelledby: undefined,
    ariaLabel: undefined,
  },
)

const emit = defineEmits<{
  close: []
  closed: []
}>()

const dialog = useNativeDialog(() => props.open)

function bindDialog(element: Element | ComponentPublicInstance | null): void {
  dialog.value = element as HTMLDialogElement | null
}

function requestClose(event: Event): void {
  event.preventDefault()
  if (props.closable) emit('close')
}

function handleClose(): void {
  emit('closed')
}
</script>

<template>
  <dialog
    :id="id"
    :ref="bindDialog"
    :data-layer="layer"
    :aria-labelledby="ariaLabelledby"
    :aria-label="ariaLabel"
    :class="[
      'app-dialog',
      `app-dialog--${layer}`,
      backdrop === 'none' ? '' : `app-dialog--backdrop-${backdrop}`,
    ]"
    @cancel="requestClose"
    @close="handleClose"
  >
    <slot />
  </dialog>
</template>

<style scoped>
.app-dialog--form,
.app-dialog--notice,
.app-dialog--profile {
  position: fixed;
  inset: 38px 0 0 var(--sidebar-width, 200px);
  width: auto;
  height: auto;
  max-width: none;
  max-height: none;
  margin: 0;
  padding: 0;
  border: 0;
}

.app-dialog::backdrop {
  background: transparent;
}

.app-dialog--backdrop-dim::backdrop {
  background: var(--color-mask);
}

.app-dialog--backdrop-strong::backdrop {
  background: var(--color-mask-strong);
}
</style>
