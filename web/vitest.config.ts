import { defineConfig } from 'vitest/config'

// Test config vite.config.ts'ten AYRI tutulur: vitest kendi vite kopyasını
// getirdiği için (vite 8/rolldown ile plugin tip çakışması), test bloğunu
// buraya alıp vite.config.ts'i saf tutuyoruz. Bu dosya tsc -b build'ine dahil
// değil; vitest çalışma anında kendi bundler'ıyla okur.
export default defineConfig({
  test: {
    environment: 'happy-dom',
    globals: true,
  },
})
