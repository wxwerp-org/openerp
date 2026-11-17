// 前端静态检查使用 ESLint flat config：
// - @eslint/js、typescript-eslint 与 eslint-plugin-vue 的官方配置负责潜在错误；
// - Vue 采用官方 error-prevention（flat/essential）规则集，并补充官方 flat/recommended 中
//   10 条非排版正确性/安全规则（如 vue/no-v-html、vue/require-explicit-emits）；
// - 不启用模板排版与格式化规则；两空格缩进由 .editorconfig 约定，单引号、无分号等继续沿用现有代码约定；
// - 类型检查仍由独立的 `vue-tsc --noEmit` 完成，lint 不读取类型信息。
import js from '@eslint/js'
import vitest from '@vitest/eslint-plugin'
import { withVueTs, vueTsConfigs } from '@vue/eslint-config-typescript'
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
  pluginVue.configs['flat/essential'],
  {
    name: 'openerp-web/vue-recommended-correctness',
    rules: {
      'vue/no-lone-template': 'warn',
      'vue/no-multiple-slot-args': 'warn',
      'vue/no-required-prop-with-default': 'warn',
      'vue/no-template-shadow': 'warn',
      'vue/no-v-html': 'warn',
      'vue/one-component-per-file': 'warn',
      'vue/require-default-prop': 'warn',
      'vue/require-explicit-emits': 'warn',
      'vue/require-prop-types': 'warn',
      'vue/this-in-template': 'warn',
    },
  },
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
)
