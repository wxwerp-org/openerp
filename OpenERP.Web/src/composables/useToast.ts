import { onUnmounted, ref, type Ref } from 'vue'

export function useToast(): { toast: Ref<string>; showToast: (message: string) => void } {
  const toast = ref('')
  let timer: ReturnType<typeof setTimeout> | undefined

  function showToast(message: string): void {
    toast.value = message
    if (timer) clearTimeout(timer)
    timer = setTimeout(() => {
      toast.value = ''
    }, 2200)
  }

  onUnmounted(() => {
    if (timer) clearTimeout(timer)
  })

  return { toast, showToast }
}
