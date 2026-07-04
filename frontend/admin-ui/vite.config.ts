import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
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
