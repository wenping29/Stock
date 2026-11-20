import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    proxy: {
      // '/api': { target: 'http://43.142.147.244:9999', changeOrigin: true },
      // 量化分析接口已并入 FundAnalysis .NET 后端：/analysis-api/* -> http://localhost:5000/api/*
      '/analysis-api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        rewrite: (p) => p.replace(/^\/analysis-api/, '/api'),
      },
      '/api': { target: 'http://localhost:5000', changeOrigin: true },
    },
  },
})
