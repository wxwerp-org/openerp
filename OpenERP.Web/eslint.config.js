// 前端静态检查使用 ESLint flat config：
// - @eslint/js、typescript-eslint 与 eslint-plugin-vue 的官方配置负责代码质量；
// - Vue 采用完整 flat/recommended 规则集，末尾由 eslint-config-prettier 关闭排版冲突；
// - 缩进、引号、分号等排版统一由 Prettier 负责，不使用 ESLint 格式化规则；
// - 类型检查仍由独立的 `vue-tsc --noEmit` 完成，lint 不读取类型信息。
import js from '@eslint/js'
import vitest from '@vitest/eslint-plugin'
import { withVueTs, vueTsConfigs } from '@vue/eslint-config-typescript'
import skipFormatting from 'eslint-config-prettier/flat'
import playwright from 'eslint-plugin-playwright'
import pluginVue from 'eslint-plugin-vue'
import globals from 'globals'

export default withVueTs(
  {
    name: 'openerp-web/ignores',
    ignores: [
      'coverage/**',
      'dist/**',
      'node_modules/**',
      'playwright-report/**',
      'test-results/**',
    ],
  },
  js.configs.recommended,
  pluginVue.configs['flat/recommended'],
  vueTsConfigs.recommended,
  {
    name: 'openerp-web/browser',
    files: ['src/**/*.{ts,vue}'],
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    name: 'openerp-web/node-config',
    files: ['*.config.{js,ts}', 'eslint.config.js'],
    languageOptions: {
      globals: globals.node,
    },
  },
  {
    name: 'openerp-web/unit-tests',
    files: ['tests/unit/**/*.ts'],
    plugins: {
      vitest,
    },
    languageOptions: {
      globals: {
        ...globals.browser,
        ...globals.node,
        ...vitest.environments.env.globals,
      },
    },
    rules: {
      ...vitest.configs.recommended.rules,
    },
  },
  {
    name: 'openerp-web/e2e-tests',
    files: ['tests/e2e/**/*.ts'],
    ...playwright.configs['flat/recommended'],
    languageOptions: {
      ...playwright.configs['flat/recommended'].languageOptions,
      globals: {
        ...playwright.configs['flat/recommended'].languageOptions.globals,
        ...globals.browser,
        ...globals.node,
      },
    },
    rules: {
      ...playwright.configs['flat/recommended'].rules,
      'playwright/no-skipped-test': ['error', { allowConditional: { skip: true, fixme: false } }],
    },
  },
  skipFormatting,
)
