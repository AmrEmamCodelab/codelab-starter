import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'node',
    include: ['tests/**/*.test.mjs'],
    restoreMocks: true,
    // ESLint and Stylelint start slowly on Windows; real-engine tests need more than the default 5 s.
    testTimeout: 30000,
  },
});
