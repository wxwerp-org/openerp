<!-- 各平台 API 能力：读取旺销王开放 API 的清单与 Schema，直观展示每个平台能调用什么。 -->
<template>
  <ElCard shadow="never" class="abilities">
    <ElSkeleton v-if="loading" :rows="6" animated />

    <div v-else-if="loadError" class="abilities__failed">
      <ArtSvgIcon icon="ri:error-warning-line" class="abilities__failed-icon" />
      <p>{{ loadError }}</p>
      <ElButton type="primary" @click="load">重试</ElButton>
    </div>

    <div v-else class="abilities__layout">
      <aside class="abilities__aside">
        <div class="abilities__aside-title">支持的平台</div>
        <ul class="abilities__menu">
          <li
            v-for="item in sortedPlatforms"
            :key="item.key"
            class="abilities__menu-item"
            :class="{ 'is-active': item.key === activeKey, 'is-muted': item.apis.length === 0 }"
            @click="selectPlatform(item.key)"
          >
            <span class="abilities__menu-name">{{ item.name }}</span>
            <span class="abilities__menu-count">{{ usableCount(item) || '—' }}</span>
          </li>
        </ul>
      </aside>

      <section v-if="active" class="abilities__main">
        <header class="abilities__head">
          <h3>{{ active.name }}</h3>
          <a
            v-if="active.officialUrl"
            :href="active.officialUrl"
            target="_blank"
            rel="noopener noreferrer"
          >
            {{ active.officialUrlText || active.officialUrl }}
          </a>
        </header>
        <p class="abilities__desc">{{ active.description }}</p>

        <div class="abilities__stats">
          <div class="abilities__stat">
            <b>{{ usableApis.length }}</b>
            <span>可用接口</span>
          </div>
          <div class="abilities__stat">
            <b>{{ usableCategories.length }}</b>
            <span>业务分类</span>
          </div>
          <div v-for="stat in methodStats" :key="stat.method" class="abilities__stat">
            <b>{{ stat.count }}</b>
            <span>{{ stat.method }}</span>
          </div>
        </div>

        <ElAlert
          v-if="!active.apis.length"
          type="info"
          :closable="false"
          show-icon
          title="该平台已开放代理调用，但开放 API 暂未提供接口清单；可参考下面的调用方式或平台官方文档。"
        />

        <template v-else>
          <div class="abilities__bars">
            <div class="abilities__section-title">能力分布（点击分类筛选接口）</div>
            <div
              v-for="bar in topCategories"
              :key="bar.id"
              class="abilities__bar"
              :class="{ 'is-active': categoryId === bar.id }"
              @click="categoryId = categoryId === bar.id ? null : bar.id"
            >
              <span class="abilities__bar-name">{{ bar.name }}</span>
              <span class="abilities__bar-track"><i :style="{ width: `${bar.percent}%` }" /></span>
              <span class="abilities__bar-count">{{ bar.count }}</span>
            </div>
          </div>

          <div class="abilities__filter">
            <ElSelect v-model="categoryId" clearable placeholder="全部分类" style="width: 220px">
              <ElOption
                v-for="category in active.categories"
                :key="category.id"
                :label="`${category.name}（${category.frozen ? '不适用' : category.count}）`"
                :value="category.id"
              />
            </ElSelect>
            <ElInput
              v-model="keyword"
              clearable
              placeholder="搜索接口名称、API 名称或说明"
              style="width: 320px"
            />
            <span class="abilities__total">共 {{ filteredApis.length }} 个接口</span>
          </div>

          <ElTable :data="pagedApis" size="small" border empty-text="暂无接口">
            <ElTableColumn prop="name" label="接口名称" min-width="180" show-overflow-tooltip />
            <ElTableColumn prop="api" label="API 名称" min-width="220" show-overflow-tooltip />
            <ElTableColumn prop="category" label="分类" min-width="120" show-overflow-tooltip />
            <ElTableColumn prop="method" label="方式" width="90" />
            <ElTableColumn prop="description" label="说明" min-width="260" show-overflow-tooltip />
            <ElTableColumn label="操作" width="100" fixed="right">
              <template #default="{ row }">
                <ElTooltip v-if="row.frozen" :content="row.frozenReason || '不适合卖家使用'">
                  <span class="abilities__muted">不适用</span>
                </ElTooltip>
                <ElButton
                  v-else-if="row.schemaName"
                  link
                  type="primary"
                  @click="openSchema(row as PlatformAbilityApi)"
                >
                  查看文档
                </ElButton>
                <span v-else class="abilities__muted">暂无文档</span>
              </template>
            </ElTableColumn>
          </ElTable>
          <ElPagination
            v-if="filteredApis.length > PAGE_SIZE"
            class="abilities__pager"
            size="small"
            layout="prev, pager, next"
            :page-size="PAGE_SIZE"
            :total="filteredApis.length"
            v-model:current-page="page"
          />
        </template>

        <div v-if="active.steps.length" class="abilities__block">
          <div class="abilities__section-title">接口调用方案</div>
          <ol class="abilities__steps">
            <li v-for="(step, index) in active.steps" :key="index">{{ step }}</li>
          </ol>
        </div>
        <div v-if="requestExample" class="abilities__block">
          <div class="abilities__section-title">请求示例（POST {{ active.apiPath }}）</div>
          <pre class="abilities__code">{{ requestExample }}</pre>
        </div>
      </section>
    </div>

    <ElDrawer v-model="schemaVisible" :title="schema?.name || '接口文档'" size="720px">
      <div v-loading="schemaLoading" class="schema">
        <ElAlert v-if="schemaError" type="error" :closable="false" show-icon :title="schemaError" />
        <template v-if="schema">
          <div class="schema__head">
            <ElTag size="small">{{ schema.platformApi?.method }}</ElTag>
            <code>{{ schema.platformApi?.api }}</code>
          </div>
          <p class="schema__desc">{{ schema.description }}</p>
          <p v-if="schema.platformApi?.sourceUrl" class="schema__desc">
            <a :href="schema.platformApi.sourceUrl" target="_blank" rel="noopener noreferrer">
              平台官方文档
            </a>
          </p>
          <ElAlert v-if="schema.notice" type="warning" :closable="false" show-icon>
            {{ clean(schema.notice) }}
          </ElAlert>

          <template v-for="block in fieldBlocks" :key="block.title">
            <div class="abilities__section-title">{{ block.title }}</div>
            <ElTable :data="block.fields" size="small" border empty-text="无">
              <ElTableColumn prop="name" label="字段" min-width="170" />
              <ElTableColumn prop="type" label="类型" min-width="120" />
              <ElTableColumn label="必填" width="70">
                <template #default="{ row }">{{ clean(row.required) }}</template>
              </ElTableColumn>
              <ElTableColumn label="说明" min-width="220">
                <template #default="{ row }">{{ clean(row.description) }}</template>
              </ElTableColumn>
            </ElTable>
          </template>
          <template v-for="block in exampleBlocks" :key="block.title">
            <div class="abilities__section-title">{{ block.title }}</div>
            <pre class="abilities__code">{{ block.json }}</pre>
          </template>
        </template>
      </div>
    </ElDrawer>
  </ElCard>
</template>

<script setup lang="ts">
  import {
    fetchPlatformAbilities,
    fetchPlatformApiSchema,
    type PlatformAbility,
    type PlatformAbilityApi,
    type PlatformApiSchema
  } from '@/api/erp'

  defineOptions({ name: 'PlatformAbilities' })

  const PAGE_SIZE = 50
  const route = useRoute()
  const router = useRouter()

  const loading = ref(true)
  const loadError = ref('')
  const platforms = ref<PlatformAbility[]>([])
  const activeKey = ref('')
  const categoryId = ref<number | null>(null)
  const keyword = ref('')
  const page = ref(1)

  const schemaVisible = ref(false)
  const schemaLoading = ref(false)
  const schemaError = ref('')
  const schema = ref<PlatformApiSchema | null>(null)
  let schemaRequestId = 0

  const usableCount = (item: PlatformAbility) => item.apis.filter((api) => !api.frozen).length

  /** 有接口清单的平台排在前面，其余保持上游顺序 */
  const sortedPlatforms = computed(() =>
    [...platforms.value].sort((a, b) => Number(b.apis.length > 0) - Number(a.apis.length > 0))
  )
  const active = computed(() => platforms.value.find((item) => item.key === activeKey.value))
  const usableApis = computed(() => active.value?.apis.filter((api) => !api.frozen) ?? [])
  const usableCategories = computed(
    () => active.value?.categories.filter((category) => !category.frozen) ?? []
  )

  const methodStats = computed(() => {
    const counts = new Map<string, number>()
    for (const api of usableApis.value) {
      const method = (api.method || 'POST').toUpperCase()
      counts.set(method, (counts.get(method) ?? 0) + 1)
    }
    return [...counts]
      .map(([method, count]) => ({ method, count }))
      .sort((a, b) => b.count - a.count)
  })

  const topCategories = computed(() => {
    const list = [...usableCategories.value].sort((a, b) => b.count - a.count).slice(0, 8)
    const max = list[0]?.count ?? 1
    return list.map((category) => ({
      ...category,
      percent: Math.max(4, Math.round((category.count / max) * 100))
    }))
  })

  const filteredApis = computed<PlatformAbilityApi[]>(() => {
    const text = keyword.value.trim().toLowerCase()
    return (active.value?.apis ?? []).filter(
      (api) =>
        (categoryId.value === null || api.categoryId === categoryId.value) &&
        (!text || [api.name, api.api, api.description].some((v) => v.toLowerCase().includes(text)))
    )
  })
  const pagedApis = computed(() =>
    filteredApis.value.slice((page.value - 1) * PAGE_SIZE, page.value * PAGE_SIZE)
  )
  watch([categoryId, keyword, activeKey], () => (page.value = 1))

  /** 请求示例由上游以 JSON 返回，格式化后展示 */
  const requestExample = computed(() => {
    const raw = active.value?.requestExample
    if (!raw) return ''
    try {
      return JSON.stringify(JSON.parse(raw), null, 2)
    } catch {
      return raw
    }
  })

  /** 上游语言包标记以 “_” 开头，页面里直接显示去掉标记的文本 */
  const clean = (text?: string) => (text ?? '').replace(/^_/, '')

  const fieldBlocks = computed(() => [
    { title: '请求参数', fields: schema.value?.requestFields ?? [] },
    { title: '响应字段', fields: schema.value?.responseFields ?? [] }
  ])
  const exampleBlocks = computed(() =>
    [
      { title: '请求示例', value: schema.value?.requestExample },
      { title: '返回示例', value: schema.value?.responseExample }
    ]
      .filter((block) => block.value !== undefined && block.value !== null)
      .map((block) => ({ title: block.title, json: JSON.stringify(block.value, null, 2) }))
  )

  function selectPlatform(key: string) {
    activeKey.value = key
    categoryId.value = null
    keyword.value = ''
    void router.replace({ query: { ...route.query, tab: 'abilities', platform: key } })
  }

  async function load() {
    loading.value = true
    loadError.value = ''
    try {
      platforms.value = await fetchPlatformAbilities()
      const wanted = typeof route.query.platform === 'string' ? route.query.platform : ''
      activeKey.value = platforms.value.some((item) => item.key === wanted)
        ? wanted
        : (sortedPlatforms.value[0]?.key ?? '')
    } catch (error) {
      loadError.value = error instanceof Error ? error.message : '平台能力读取失败，请重试。'
    } finally {
      loading.value = false
    }
  }

  async function openSchema(row: PlatformAbilityApi) {
    if (!row.schemaName) return
    const requestId = ++schemaRequestId
    schemaVisible.value = true
    schemaLoading.value = true
    schemaError.value = ''
    schema.value = null
    try {
      const result = await fetchPlatformApiSchema(activeKey.value, row.schemaName)
      if (requestId === schemaRequestId) schema.value = result
    } catch (error) {
      if (requestId === schemaRequestId)
        schemaError.value = error instanceof Error ? error.message : '接口文档读取失败。'
    } finally {
      if (requestId === schemaRequestId) schemaLoading.value = false
    }
  }

  onMounted(load)
</script>

<style scoped>
  .abilities {
    min-height: 420px;
  }

  .abilities__failed {
    display: flex;
    flex-direction: column;
    gap: 12px;
    align-items: center;
    padding: 48px 0;
  }

  .abilities__failed-icon {
    font-size: 48px;
    color: var(--el-color-danger);
  }

  .abilities__failed p {
    margin-block: 0;
  }

  .abilities__layout {
    display: flex;
    gap: 16px;
    align-items: flex-start;
  }

  .abilities__aside {
    flex: none;
    width: 200px;
    padding: 4px;
    border: 1px solid var(--el-border-color-lighter);
    border-radius: 6px;
  }

  .abilities__aside-title {
    padding: 8px;
    font-size: 12px;
    color: var(--el-text-color-secondary);
  }

  .abilities__menu {
    padding: 0;
    margin: 0;
    list-style: none;
  }

  .abilities__menu-item {
    display: flex;
    align-items: center;
    justify-content: space-between;
    height: 34px;
    padding: 0 10px;
    font-size: 13px;
    cursor: pointer;
    border-radius: 4px;
  }

  .abilities__menu-item:hover {
    background: var(--el-fill-color-light);
  }

  .abilities__menu-item.is-active {
    color: var(--el-color-primary);
    background: var(--el-color-primary-light-9);
  }

  .abilities__menu-item.is-muted:not(.is-active) {
    color: var(--el-text-color-placeholder);
  }

  .abilities__menu-count {
    font-size: 12px;
    color: var(--el-text-color-secondary);
  }

  .abilities__main {
    flex: 1;
    min-width: 0;
  }

  .abilities__head {
    display: flex;
    gap: 12px;
    align-items: baseline;
  }

  .abilities__head h3 {
    margin: 0;
  }

  .abilities__desc {
    margin: 8px 0 12px;
    color: var(--el-text-color-regular);
  }

  .abilities__stats {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    margin-bottom: 12px;
  }

  .abilities__stat {
    min-width: 90px;
    padding: 6px 12px;
    background: var(--el-fill-color-lighter);
    border: 1px solid var(--el-border-color-lighter);
    border-radius: 6px;
  }

  .abilities__stat b {
    display: block;
    font-size: 20px;
    line-height: 26px;
  }

  .abilities__stat span {
    font-size: 12px;
    color: var(--el-text-color-secondary);
  }

  .abilities__section-title {
    margin: 12px 0 8px;
    font-weight: 600;
  }

  .abilities__bar {
    display: flex;
    align-items: center;
    height: 24px;
    font-size: 12px;
    cursor: pointer;
  }

  .abilities__bar.is-active .abilities__bar-name {
    color: var(--el-color-primary);
  }

  .abilities__bar-name {
    flex: none;
    width: 160px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .abilities__bar-track {
    flex: 1;
    height: 8px;
    margin-inline: 8px;
    background: var(--el-fill-color);
    border-radius: 4px;
  }

  .abilities__bar-track i {
    display: block;
    height: 100%;
    background: var(--el-color-primary);
    border-radius: 4px;
  }

  .abilities__bar-count {
    flex: none;
    width: 36px;
    color: var(--el-text-color-secondary);
    text-align: right;
  }

  .abilities__filter {
    display: flex;
    flex-wrap: wrap;
    gap: 12px;
    align-items: center;
    margin: 16px 0 8px;
  }

  .abilities__total,
  .abilities__muted {
    font-size: 12px;
    color: var(--el-text-color-secondary);
  }

  .abilities__pager {
    justify-content: flex-end;
    margin-top: 8px;
  }

  .abilities__steps {
    padding-left: 20px;
    margin: 0;
    line-height: 1.8;
  }

  .abilities__code {
    max-height: 320px;
    padding: 10px 12px;
    margin: 0;
    overflow: auto;
    font-size: 12px;
    line-height: 1.5;
    background: var(--el-fill-color-light);
    border-radius: 6px;
  }

  .schema {
    padding-inline: 4px;
  }

  .schema__head {
    display: flex;
    gap: 8px;
    align-items: center;
    margin-bottom: 8px;
  }

  .schema__desc {
    margin: 0 0 8px;
    line-height: 1.6;
    word-break: break-word;
    color: var(--el-text-color-regular);
  }

  @media (width <= 768px) {
    .abilities__layout {
      flex-direction: column;
    }

    .abilities__aside {
      width: 100%;
    }
  }
</style>
