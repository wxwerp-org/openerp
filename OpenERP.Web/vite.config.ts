import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

export default defineConfig(({ command }) => {
  const apiBaseUrl = command === 'serve' ? process.env.API_BASE_URL : undefined

  return {
    plugins: [vue()],
    server: {
      proxy: apiBaseUrl
        ? {
            '/api': {
              target: apiBaseUrl,
              changeOrigin: true,
              secure: false,
            },
          }
        : undefined,
    },
  }
})
