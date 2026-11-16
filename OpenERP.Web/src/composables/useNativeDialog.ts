import { nextTick, onUnmounted, ref, watch, type Ref } from 'vue'

export function useNativeDialog(isOpen: () => boolean): Ref<HTMLDialogElement | null> {
  const dialog = ref<HTMLDialogElement | null>(null)

  watch(isOpen, async (open) => {
    await nextTick()
    const element = dialog.value
    if (!element) return

    if (open && !element.open) element.showModal()
    else if (!open && element.open) element.close()
  }, { immediate: true })

  onUnmounted(() => {
    if (dialog.value?.open) dialog.value.close()
  })

  return dialog
}
