import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Production build lands in the API's wwwroot so one process serves the
  // whole app: share http://<host>:5099 and others get UI + API together.
  build: {
    outDir: '../../src/ECARMF.Kernel.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      // The kernel API; avoids CORS in local development.
      '/api': {
        target: process.env.VITE_API_TARGET ?? 'http://localhost:5099',
        changeOrigin: true,
      },
    },
  },
})
