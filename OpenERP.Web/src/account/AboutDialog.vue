<script setup lang="ts">
import AppDialog from '../ui/AppDialog.vue'
import AppDialogClose from '../ui/AppDialogClose.vue'
import { useAboutInfo } from './aboutInfo'

defineProps<{ open: boolean }>()

const emit = defineEmits<{ close: [] }>()

const { info, versionLabel, commitLabel } = useAboutInfo()
</script>

<template>
  <AppDialog
    id="aboutLayer"
    :open="open"
    layer="notice"
    aria-labelledby="aboutTitle"
    @close="emit('close')"
  >
    <div class="w-360 max-w-[calc(100vw_-_48px)] p-24">
      <div class="flex items-center justify-between">
        <h2 id="aboutTitle" class="text-14/22 font-semibold text-ink">关于 OpenERP</h2>
        <AppDialogClose id="closeAbout" aria-label="关闭" @click="emit('close')" />
      </div>
      <dl class="mt-16 grid grid-cols-[80px_minmax(0,1fr)] gap-y-8 text-12/20">
        <dt class="text-muted">版本</dt>
        <dd class="m-0 text-body-strong">{{ versionLabel }}</dd>
        <dt class="text-muted">提交</dt>
        <dd class="m-0 text-body-strong">{{ commitLabel || '—' }}</dd>
        <dt class="text-muted">构建时间</dt>
        <dd class="m-0 text-body-strong">{{ info?.buildTime || '—' }}</dd>
      </dl>
    </div>
  </AppDialog>
</template>
