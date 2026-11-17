import js from '@eslint/js'
import tseslint from 'typescript-eslint'
import playwright from 'eslint-plugin-playwright'
import globals from 'globals'

export default [
  { ignores: ['node_modules/**', 'test-results/**', 'playwright-report/**', 'OpenERP.Web/**'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  { languageOptions: { globals: { ...globals.node, ...globals.browser } } },
  { ...playwright.configs['flat/recommended'], files: ['e2e/**/*.spec.ts'] },
]
