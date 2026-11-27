import { fileURLToPath } from 'node:url'
import vue from '@vitejs/plugin-vue'
import AutoImport from 'unplugin-auto-import/vite'
import Components from 'unplugin-vue-components/vite'
import { ElementPlusResolver } from 'unplugin-vue-components/resolvers'
import { defineConfig } from 'vitest/config'

const src = (path: string) => fileURLToPath(new URL(`./src/${path}`, import.meta.url))

// 与 vite.config.ts 保持一致：页面依赖 API 与 Element Plus 组件的自动导入。
export default defineConfig({
  // 与 vite.config.ts 的 define 一致，页面链路里的路由模块会读取版本号
  define: { __APP_VERSION__: JSON.stringify('test') },
  plugins: [
    vue(),
    AutoImport({
      imports: ['vue', 'vue-router', 'pinia', '@vueuse/core'],
      resolvers: [ElementPlusResolver({ importStyle: false })],
      dts: false
    }),
    Components({ resolvers: [ElementPlusResolver({ importStyle: false })], dts: false })
  ],
  resolve: {
    alias: {
      '@views': src('views'),
      '@imgs': src('assets/images'),
      '@icons': src('assets/icons'),
      '@utils': src('utils'),
      '@stores': src('store'),
      '@styles': src('assets/styles'),
      '@': src('')
    }
  },
  test: {
    environment: 'jsdom',
    include: ['tests/unit/**/*.spec.ts'],
    // Node 25+ 默认启用实验性 webstorage，会让 jsdom 的 localStorage 变成 undefined；单测关闭它。
    poolOptions: { forks: { execArgv: ['--no-experimental-webstorage'] } },
    // Element Plus 样式在单测中无需解析
    css: false
  }
})
