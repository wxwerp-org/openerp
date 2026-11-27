<!-- API权限绑定（ERP 接入）：配置向导。第 1 步填写 Token，第 2 步检测并保存，第 3 步为已配置状态；“重新配置”回到第 1 步。 -->
<template>
  <div class="page-stack">
    <ElTabs v-model="tab" @tab-change="onTabChange">
      <ElTabPane label="绑定配置" name="binding">
        <ElCard shadow="never" class="binding-help">
          <div class="binding-help__body">
            <div class="binding-help__icon">
              <ArtSvgIcon icon="ri:flashlight-line" />
            </div>
            <div class="binding-help__text">
              <h3 class="binding-help__title">功能介绍</h3>
              <p>
                API权限绑定的意义，是让系统直接访问旺销王平台的
                API，从而无缝对接平台的任意功能，而不只是店铺与订单：绑定一次
                AccessToken，之后平台开放的能力都可以在本系统里直接调用和扩展。
              </p>
              <p>
                Token
                加密保存且不会再次显示，可随时重新配置或撤销。公共SaaS用户选择“旺销王公共SaaS”；使用独立部署的用户选择“独立部署”，并填写专属地址。
              </p>
              <div class="binding-help__action">
                <ElButton
                  tag="a"
                  type="primary"
                  plain
                  :href="HELP_URL"
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  了解如何获取 AccessToken
                </ElButton>
              </div>
            </div>
            <!-- 示意图：信息在本系统与平台之间的传递 -->
            <div
              class="flow"
              role="img"
              aria-label="本系统携带 AccessToken 请求旺销王平台 API，平台返回数据"
            >
              <div class="flow__node">
                <ArtSvgIcon icon="ri:computer-line" class="flow__icon" />
                <strong>本系统</strong>
                <span>店铺、订单及更多功能</span>
              </div>
              <div class="flow__link">
                <span class="flow__label">携带 AccessToken 发起请求</span>
                <span class="flow__line flow__line--go" />
                <span class="flow__line flow__line--back" />
                <span class="flow__label">返回平台数据</span>
              </div>
              <div class="flow__node flow__node--api">
                <ArtSvgIcon icon="ri:plug-line" class="flow__icon" />
                <strong>旺销王平台 API</strong>
                <span>公共SaaS 或 独立部署</span>
              </div>
              <div class="flow__link">
                <span class="flow__label">开放全部能力</span>
                <span class="flow__line flow__line--go" />
              </div>
              <div class="flow__node">
                <ArtSvgIcon icon="ri:apps-2-line" class="flow__icon" />
                <strong>平台任意功能</strong>
                <span>无缝对接，按需扩展</span>
              </div>
            </div>
          </div>
        </ElCard>

        <ElCard shadow="never" class="binding-card">
          <ElSkeleton v-if="loading" :rows="4" animated />

          <div v-else-if="loadFailed" class="binding-failed">
            <ArtSvgIcon icon="ri:error-warning-line" class="binding-failed__icon" />
            <p>配置状态读取失败</p>
            <ElButton type="primary" @click="load">重试</ElButton>
          </div>

          <div v-else class="binding">
            <div class="binding__side">
              <div class="binding__badge" :class="{ 'is-done': step === 2 }">
                <ArtSvgIcon icon="ri:plug-line" />
              </div>
              <h2 class="binding__title">API权限绑定</h2>
              <p class="binding__desc">绑定旺销王 ERP 的 AccessToken，直接访问平台 API。</p>
            </div>

            <div class="binding__main">
              <ElSteps :active="step" finish-status="success" align-center>
                <ElStep title="填写 Token" />
                <ElStep title="检测并保存" />
                <ElStep title="已配置" />
              </ElSteps>

              <!-- 第 1 步：填写 -->
              <ElForm
                v-if="step === 0"
                label-position="top"
                class="binding__panel"
                @submit.prevent="check"
              >
                <ElFormItem label="部署方式">
                  <ElRadioGroup v-model="independent" :disabled="checking">
                    <ElRadio :value="false">旺销王公共SaaS</ElRadio>
                    <ElRadio :value="true">独立部署</ElRadio>
                  </ElRadioGroup>
                </ElFormItem>
                <ElFormItem v-if="independent" label="独立部署地址" required>
                  <ElInput
                    v-model="baseUrl"
                    size="large"
                    placeholder="https://123.wxwerp.com"
                    :disabled="checking"
                  />
                </ElFormItem>
                <ElFormItem label="旺销王 ERP 密钥（私有 ERP AccessToken）">
                  <ElInput
                    v-model="token"
                    type="password"
                    show-password
                    size="large"
                    autocomplete="new-password"
                    placeholder="填写 Token"
                    :disabled="checking"
                  />
                </ElFormItem>
                <p v-if="failMessage" class="binding__error">{{ failMessage }}</p>
                <div class="binding__actions">
                  <ElButton
                    type="primary"
                    size="large"
                    native-type="submit"
                    :loading="checking"
                    :disabled="!token.trim() || (independent && !baseUrl.trim())"
                  >
                    检测可用性
                  </ElButton>
                </div>
              </ElForm>

              <!-- 第 2 步：检测通过，确认保存 -->
              <div v-else-if="step === 1" class="binding__panel binding__result">
                <ArtSvgIcon icon="ri:checkbox-circle-fill" class="binding__result-icon is-ok" />
                <p class="binding__result-text">{{ okMessage }}</p>
                <div class="binding__actions">
                  <ElButton size="large" :disabled="saving" @click="step = 0">上一步</ElButton>
                  <ElButton type="primary" size="large" :loading="saving" @click="save"
                    >保存</ElButton
                  >
                </div>
              </div>

              <!-- 第 3 步：已配置 -->
              <div v-else class="binding__panel binding__result">
                <ArtSvgIcon icon="ri:shield-check-fill" class="binding__result-icon is-ok" />
                <p class="binding__result-text">已配置</p>
                <p class="binding__hint">
                  {{ savedIndependent ? `独立部署：${savedBaseUrl}` : '旺销王公共SaaS' }}
                </p>
                <p class="binding__hint">密钥已加密保存，不会再次显示。</p>
                <div class="binding__actions">
                  <ElButton size="large" :disabled="revoking" @click="reconfigure"
                    >重新配置</ElButton
                  >
                  <ElButton size="large" type="danger" plain :loading="revoking" @click="revoke">
                    撤销配置
                  </ElButton>
                </div>
              </div>
            </div>
          </div>
        </ElCard>
      </ElTabPane>
      <ElTabPane label="平台API能力" name="abilities" lazy>
        <PlatformAbilities />
      </ElTabPane>
    </ElTabs>
  </div>
</template>

<script setup lang="ts">
  import { ElMessage, ElMessageBox } from 'element-plus'
  import { fetchErpConfig, revokeErpConfig, saveErpToken, validateErpToken } from '@/api/erp'

  import PlatformAbilities from './PlatformAbilities.vue'

  defineOptions({ name: 'ErpBinding' })

  const route = useRoute()
  const router = useRouter()
  /** 当前页签，与地址 ?tab=abilities&platform=ozon 同步，便于直接分享某平台的能力页 */
  const tab = ref(route.query.tab === 'abilities' ? 'abilities' : 'binding')

  function onTabChange(name: string | number) {
    const query = { ...route.query }
    if (name === 'abilities') query.tab = 'abilities'
    else {
      delete query.tab
      delete query.platform
    }
    void router.replace({ query })
  }

  const HELP_URL = 'https://help.wxwerp.com/module-tutorial/system.open_api.api?q=api&lang=zh'
  const revoking = ref(false)

  const loading = ref(true)
  const loadFailed = ref(false)
  /** 0 填写 Token；1 检测通过待保存；2 已配置 */
  const step = ref(0)
  const token = ref('')
  const checking = ref(false)
  const saving = ref(false)
  const failMessage = ref('')
  const okMessage = ref('')
  /** 部署方式：默认公共版；勾选独立部署后需填写地址 */
  const independent = ref(false)
  const baseUrl = ref('')
  /** 已保存的部署信息，用于已配置状态展示 */
  const savedIndependent = ref(false)
  const savedBaseUrl = ref('')

  /** 公共版不传地址 */
  const targetBaseUrl = () => (independent.value ? baseUrl.value.trim() : undefined)

  async function load() {
    loading.value = true
    loadFailed.value = false
    try {
      const config = await fetchErpConfig()
      savedIndependent.value = config.independent
      savedBaseUrl.value = config.independent ? config.baseUrl : ''
      independent.value = config.independent
      baseUrl.value = savedBaseUrl.value
      step.value = config.configured ? 2 : 0
    } catch {
      loadFailed.value = true
    } finally {
      loading.value = false
    }
  }

  async function revoke() {
    if (revoking.value) return
    try {
      await ElMessageBox.confirm(
        '撤销后将删除已保存的 AccessToken，店铺同步和订单同步将无法使用，直到重新配置。',
        '撤销配置',
        { type: 'warning', confirmButtonText: '撤销', cancelButtonText: '取消' }
      )
    } catch {
      return
    }
    revoking.value = true
    try {
      await revokeErpConfig()
      savedIndependent.value = false
      savedBaseUrl.value = ''
      independent.value = false
      baseUrl.value = ''
      token.value = ''
      step.value = 0
      ElMessage.success('已撤销配置')
    } catch (error) {
      ElMessage.error(error instanceof Error ? error.message : '撤销失败，请重试。')
    } finally {
      revoking.value = false
    }
  }

  function reconfigure() {
    token.value = ''
    failMessage.value = ''
    step.value = 0
  }

  async function check() {
    if (checking.value || !token.value.trim()) return
    if (
      independent.value &&
      !/^https:\/\/[^/\s]+$/i.test(baseUrl.value.trim().replace(/\/$/, ''))
    ) {
      failMessage.value = '独立部署地址须为 https://xxx.wxwerp.com 形式'
      return
    }
    checking.value = true
    failMessage.value = ''
    try {
      const result = await validateErpToken(token.value, targetBaseUrl())
      if (result.valid) {
        okMessage.value = result.message || 'Token 可用，请保存'
        step.value = 1
      } else {
        failMessage.value = result.message || 'Token 不可用'
      }
    } catch (error) {
      failMessage.value = error instanceof Error ? error.message : '检测失败，请重试。'
    } finally {
      checking.value = false
    }
  }

  async function save() {
    if (saving.value) return
    saving.value = true
    try {
      await saveErpToken(token.value, targetBaseUrl())
      savedIndependent.value = independent.value
      savedBaseUrl.value = independent.value ? baseUrl.value.trim().replace(/\/$/, '') : ''
      token.value = ''
      step.value = 2
      ElMessage.success('保存成功')
    } catch (error) {
      ElMessage.error(error instanceof Error ? error.message : '保存失败，请重试。')
    } finally {
      saving.value = false
    }
  }

  onMounted(load)
</script>

<style scoped>
  .binding-card {
    min-height: 420px;
  }

  .binding {
    display: flex;
    gap: 48px;
    align-items: center;
    max-width: 960px;
    padding: 24px;
    margin-inline: auto;
  }

  .binding__side {
    display: flex;
    flex: none;
    flex-direction: column;
    gap: 12px;
    align-items: center;
    width: 220px;
    text-align: center;
  }

  .binding__badge {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 88px;
    height: 88px;
    font-size: 44px;
    color: var(--el-color-primary);
    background: var(--el-color-primary-light-9);
    border-radius: 24px;
  }

  .binding__badge.is-done {
    color: var(--el-color-success);
    background: var(--el-color-success-light-9);
  }

  .binding__title {
    margin-block: 0;
    font-size: 18px;
    font-weight: 600;
  }

  .binding__desc,
  .binding__hint {
    margin-block: 0;
    font-size: 13px;
    line-height: 1.6;
    color: var(--el-text-color-secondary);
  }

  .binding__main {
    display: flex;
    flex: 1;
    flex-direction: column;
    gap: 32px;
    min-width: 0;
  }

  .binding__panel {
    width: 100%;
    max-width: 440px;
    align-self: center;
  }

  .binding__actions {
    display: flex;
    gap: 12px;
    justify-content: center;
  }

  .binding__error {
    margin-block: 0;
    padding-bottom: 12px;
    font-size: 13px;
    color: var(--el-color-danger);
  }

  .binding__result {
    display: flex;
    flex-direction: column;
    gap: 12px;
    align-items: center;
    text-align: center;
  }

  .binding__result-icon {
    font-size: 56px;
  }

  .binding__result-icon.is-ok {
    color: var(--el-color-success);
  }

  .binding__result-text {
    margin-block: 0;
    font-size: 18px;
    font-weight: 600;
  }

  .binding-help__body {
    display: flex;
    gap: 16px;
    align-items: flex-start;
  }

  .binding-help__icon {
    display: flex;
    flex: none;
    align-items: center;
    justify-content: center;
    width: 40px;
    height: 40px;
    font-size: 22px;
    color: var(--el-color-primary);
    background: var(--el-color-primary-light-9);
    border-radius: 12px;
  }

  .binding-help__text {
    font-size: 13px;
    line-height: 1.7;
    color: var(--el-text-color-regular);
  }

  .binding-help__text p {
    margin-block: 0;
  }

  .binding-help__title {
    margin-block: 0 6px;
    font-size: 15px;
    font-weight: 600;
    color: var(--el-text-color-primary);
  }

  .binding-help__text {
    flex: 1;
    min-width: 0;
  }

  .binding-help__action {
    padding-top: 10px;
  }

  .flow {
    display: flex;
    flex: none;
    gap: 6px;
    align-items: center;
    padding: 10px 12px;
    background: var(--el-fill-color-extra-light);
    border-radius: 10px;
  }

  .flow__node {
    display: flex;
    flex: none;
    flex-direction: column;
    gap: 2px;
    align-items: center;
    width: 76px;
    padding: 8px 4px;
    font-size: 10px;
    color: var(--el-text-color-secondary);
    text-align: center;
    background: var(--el-fill-color-lighter);
    border: 1px solid var(--el-border-color-lighter);
    border-radius: 12px;
  }

  .flow__node strong {
    font-size: 12px;
    color: var(--el-text-color-primary);
  }

  .flow__node--api {
    background: var(--el-color-primary-light-9);
    border-color: var(--el-color-primary-light-5);
  }

  .flow__icon {
    font-size: 18px;
    color: var(--el-color-primary);
  }

  .flow__link {
    display: flex;
    flex: none;
    flex-direction: column;
    gap: 3px;
    align-items: stretch;
    width: 64px;
    font-size: 10px;
    color: var(--el-text-color-secondary);
    text-align: center;
  }

  .flow__line {
    position: relative;
    height: 2px;
    background: var(--el-color-primary-light-5);
  }

  .flow__line::after {
    position: absolute;
    top: -3px;
    width: 0;
    height: 0;
    content: '';
    border-block: 4px solid transparent;
  }

  .flow__line--go::after {
    right: -1px;
    border-left: 7px solid var(--el-color-primary-light-5);
  }

  .flow__line--back {
    background: var(--el-color-success-light-5);
  }

  .flow__line--back::after {
    left: -1px;
    border-right: 7px solid var(--el-color-success-light-5);
  }

  @media (width <= 900px) {
    .binding-help__body {
      flex-direction: column;
      align-items: stretch;
    }

    .flow {
      align-self: center;
    }
  }

  .binding-failed {
    display: flex;
    flex-direction: column;
    gap: 12px;
    align-items: center;
    padding: 48px 0;
  }

  .binding-failed__icon {
    font-size: 48px;
    color: var(--el-color-danger);
  }

  .binding-failed p {
    margin-block: 0;
  }

  @media (width <= 768px) {
    .binding {
      flex-direction: column;
      gap: 24px;
      padding: 8px;
    }
  }
</style>
