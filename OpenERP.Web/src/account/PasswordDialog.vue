<script setup lang="ts">
import { ref } from 'vue'
import { useNativeDialog } from '../composables/useNativeDialog'
import PasswordChangeFields from './PasswordChangeFields.vue'

const props = defineProps<{ open: boolean }>()
const dialog = useNativeDialog(() => props.open)
const fields = ref<InstanceType<typeof PasswordChangeFields> | null>(null)

const emit = defineEmits<{
  close: []
  closed: []
}>()

function cancel(event?: Event): void {
  event?.preventDefault()
  if (dialog.value?.open && !fields.value?.submitting) emit('close')
}
</script>

<template>
  <dialog
    id="passwordLayer"
    ref="dialog"
    class="profile-layer"
    :class="{ open }"
    aria-labelledby="passwordTitle"
    @cancel="cancel"
    @close="emit('closed')"
  >
    <form class="profile-dialog password-dialog" novalidate @submit.prevent="fields?.save()">
      <div class="profile-head">
        <div id="passwordTitle" class="profile-title">修改密码</div>
        <button
          id="closePassword"
          class="form-close"
          type="button"
          :disabled="fields?.submitting"
          title="关闭"
          aria-label="关闭"
          @click="cancel"
        >
          <svg aria-hidden="true" class="ic"><use href="#i-close" /></svg>
        </button>
      </div>
      <div class="profile-body">
        <PasswordChangeFields ref="fields" :active="open" @saved="emit('close')" />
      </div>
      <div class="profile-foot">
        <button
          id="cancelPassword"
          class="btn"
          type="button"
          :disabled="fields?.submitting"
          @click="cancel"
        >
          取消
        </button>
        <button id="savePassword" class="btn primary" type="submit" :disabled="fields?.submitting">
          确认修改
        </button>
      </div>
    </form>
  </dialog>
</template>
