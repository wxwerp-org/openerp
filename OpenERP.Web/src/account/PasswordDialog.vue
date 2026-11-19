<script setup lang="ts">
import { ref } from 'vue'
import AppButton from '../ui/AppButton.vue'
import AppDialog from '../ui/AppDialog.vue'
import AppDialogClose from '../ui/AppDialogClose.vue'
import PasswordChangeFields from './PasswordChangeFields.vue'

defineProps<{ open: boolean }>()

const fields = ref<InstanceType<typeof PasswordChangeFields> | null>(null)

const emit = defineEmits<{
  close: []
  closed: []
}>()

function cancel(): void {
  if (!fields.value?.submitting) emit('close')
}
</script>

<template>
  <AppDialog
    id="passwordLayer"
    :open="open"
    layer="profile"
    backdrop="none"
    aria-labelledby="passwordTitle"
    class="bg-mask"
    @close="cancel"
    @closed="emit('closed')"
  >
    <div class="absolute inset-0 flex items-center justify-center">
      <form
        class="w-440 max-w-[calc(100%_-_40px)] overflow-hidden rounded-8 bg-white shadow-dialog"
        novalidate
        @submit.prevent="fields?.save()"
      >
        <div class="flex h-52 items-center justify-between border-b border-line pr-16 pl-20">
          <div id="passwordTitle" class="text-dialog-title font-semibold text-ink">修改密码</div>
          <AppDialogClose id="closePassword" :disabled="fields?.submitting" @click="cancel" />
        </div>
        <div class="max-h-[calc(100vh_-_220px)] overflow-y-auto px-24 pt-20 pb-4">
          <PasswordChangeFields ref="fields" :active="open" @saved="emit('close')" />
        </div>
        <div class="flex h-60 items-center justify-end gap-8 border-t border-line px-20">
          <AppButton
            id="cancelPassword"
            size="lg"
            class="min-w-64 px-16"
            type="button"
            :disabled="fields?.submitting"
            @click="cancel"
          >
            取消
          </AppButton>
          <AppButton
            id="savePassword"
            size="lg"
            variant="primary"
            type="submit"
            :disabled="fields?.submitting"
            class="min-w-64 px-16"
          >
            确认修改
          </AppButton>
        </div>
      </form>
    </div>
  </AppDialog>
</template>
