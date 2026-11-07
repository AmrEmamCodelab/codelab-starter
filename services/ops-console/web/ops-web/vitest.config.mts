import { defineConfig } from 'vitest/config';
import starterWebConfig from '../../../../frontend/starter-web/vitest.config.mts';

/**
 * Extends the starter web app's Vitest config (zoneless Angular on jsdom, templateUrl inlining and the signal-input
 * JIT transform), so both apps test the same way. Only the spec globs differ: this app has its own sources, its
 * `src/test-setup.ts` and the Node tool tests under `tools/`.
 */
export default defineConfig({
  ...starterWebConfig,
  test: {
    ...starterWebConfig.test,
    include: ['src/**/*.spec.ts', 'tools/**/*.test.mjs'],
  },
});
