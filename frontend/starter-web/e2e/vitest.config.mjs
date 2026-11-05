import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';

// The harness's own unit tests (pure Node modules: no Angular, no browser). The app's vitest.config.mts also runs
// them once its include lists e2e/**/*.test.mjs; this file lets them run on their own:
//   npx vitest run --config e2e/vitest.config.mjs
export default defineConfig({
  test: {
    root: fileURLToPath(new URL('.', import.meta.url)),
    environment: 'node',
    include: ['tests/**/*.test.mjs'],
    restoreMocks: true,
  },
});
