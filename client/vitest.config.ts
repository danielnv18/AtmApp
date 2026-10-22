import { fileURLToPath } from 'node:url'
import { configDefaults, defineConfig, mergeConfig } from 'vitest/config'
import viteConfig from './vite.config.ts'

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: 'jsdom',
      exclude: [...configDefaults.exclude, 'e2e/**'],
      root: fileURLToPath(new URL('./', import.meta.url)),
      mockReset: true,
      unstubGlobals: true,
      coverage: {
        provider: 'v8',
        include: ['src/**'],
        exclude: ['src/components/ui/**', 'src/**/__tests__/**'],
      },
    },
  }),
)
