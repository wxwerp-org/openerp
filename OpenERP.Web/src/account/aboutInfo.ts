import { computed, ref } from 'vue'

export type AboutInfo = {
  version: string
  commit: string
  buildTime: string
}

const info = ref<AboutInfo | null>(null)
let pending: Promise<void> | null = null

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function parseAbout(value: unknown): AboutInfo | null {
  if (!isRecord(value)) return null
  const version = typeof value.version === 'string' ? value.version : ''
  const commit = typeof value.commit === 'string' ? value.commit : ''
  const buildTime = typeof value.buildTime === 'string' ? value.buildTime : ''
  if (!version && !commit && !buildTime) return null
  return { version, commit, buildTime }
}

/** 版本信息按公共请求读取：匿名接口、不使用会话请求管道，失败时静默回退。 */
async function load(): Promise<void> {
  try {
    const response = await fetch('/api/about', { cache: 'no-store' })
    if (!response.ok) return
    info.value = parseAbout(await response.json())
  } catch {
    // 版本号只是辅助信息，读取失败不打扰用户。
  }
}

export function useAboutInfo() {
  if (!info.value && !pending) pending = load()

  const versionLabel = computed(() => {
    const version = info.value?.version
    if (!version) return 'dev'
    return version.startsWith('v') ? version : `v${version}`
  })
  const commitLabel = computed(() => info.value?.commit ?? '')

  return { info, versionLabel, commitLabel }
}
