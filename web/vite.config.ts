import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Dev: /api çağrılarını yerel Kasa.Api'ye yönlendir (same-origin → CORS yok).
      // API'yi şu komutla çalıştır: dotnet run --project ../Kasa.Api --urls http://localhost:5199
      '/api': {
        target: 'http://localhost:5199',
        changeOrigin: true,
      },
    },
  },
})
